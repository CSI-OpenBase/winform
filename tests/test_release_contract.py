import json
import re
import subprocess
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
POWERSHELL = "powershell.exe"
VERSION_FILE_PATTERN = re.compile(
    rb"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.[1-9][0-9](?:\r?\n)?\Z"
)


def current_version() -> str:
    return (ROOT / "VERSION").read_text(encoding="ascii").strip()


def run_powershell(script: Path, *arguments: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            POWERSHELL,
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(script),
            *arguments,
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )


class ReleaseContractTests(unittest.TestCase):
    def test_documentation_separates_local_testing_from_release_builds(self) -> None:
        agents = (ROOT / "AGENTS.md").read_text(encoding="utf-8")
        readme = (ROOT / "README.md").read_text(encoding="utf-8")

        self.assertIn("## Build Modes", agents)
        self.assertIn("**Local testing**", agents)
        self.assertIn("**Release builds**", agents)
        self.assertIn("## 构建模式约定", readme)
        self.assertIn("### 本地测试：频繁编译", readme)
        self.assertIn("### 发布版本：完整构建", readme)
        self.assertIn(".\\scripts\\build_local.ps1", readme)
        self.assertIn("Release\\local", readme)
        self.assertIn("build_windows.ps1", readme)

    def test_local_build_has_a_fixed_non_release_output(self) -> None:
        script = ROOT / "scripts" / "build_local.ps1"
        result = run_powershell(script, "-PlanOnly")

        self.assertEqual(result.returncode, 0, result.stderr)
        plan = json.loads(result.stdout)
        expected_output = ROOT / "Release" / "local"
        self.assertEqual(plan["mode"], "local-testing")
        self.assertEqual(plan["configuration"], "Debug")
        self.assertEqual(Path(plan["outputDirectory"]), expected_output)
        self.assertEqual(
            Path(plan["executable"]),
            expected_output / "CSI.OpenBase.Desktop.exe",
        )
        self.assertFalse(plan["freezesBackend"])
        self.assertFalse(plan["bundlesChromium"])
        self.assertFalse(plan["bumpsVersion"])

        source = script.read_text(encoding="utf-8")
        self.assertNotIn("build_windows.ps1", source)
        self.assertNotIn("playwright install", source.casefold())

    def test_version_file_uses_release_format(self) -> None:
        raw_version = (ROOT / "VERSION").read_bytes()
        self.assertRegex(raw_version, VERSION_FILE_PATTERN)

    def test_desktop_version_is_derived_from_version_file(self) -> None:
        project = ET.parse(ROOT / "CSI.OpenBase.Desktop" / "CSI.OpenBase.Desktop.csproj")
        properties = {
            node.tag: (node.text or "").strip()
            for node in project.getroot().iter()
            if node.tag in {
                "VersionFile",
                "Version",
                "AssemblyVersion",
                "FileVersion",
                "InformationalVersion",
                "IncludeSourceRevisionInInformationalVersion",
            }
        }
        self.assertEqual(properties["VersionFile"], "$(MSBuildProjectDirectory)\\..\\VERSION")
        self.assertIn("ReadAllText", properties["Version"])
        self.assertEqual(properties["AssemblyVersion"], "$(Version).0")
        self.assertEqual(properties["FileVersion"], "$(Version).0")
        self.assertEqual(properties["InformationalVersion"], "$(Version)")
        self.assertEqual(properties["IncludeSourceRevisionInInformationalVersion"], "false")

    def test_release_plan_uses_versioned_project_directory(self) -> None:
        result = run_powershell(ROOT / "scripts" / "build_windows.ps1", "-PlanOnly")
        self.assertEqual(result.returncode, 0, result.stderr)
        plan = json.loads(result.stdout)
        version = current_version()
        release_directory = ROOT / "Release" / version
        archive_name = f"CSI-OpenBase-{version}-win-x64-portable.zip"
        self.assertEqual(plan["version"], version)
        self.assertEqual(Path(plan["releaseDirectory"]), release_directory)
        self.assertEqual(Path(plan["portableDirectory"]), release_directory / "portable")
        self.assertEqual(Path(plan["installerDirectory"]), release_directory / "installer")
        self.assertEqual(
            Path(plan["portableArchive"]),
            release_directory / archive_name,
        )
        self.assertEqual(
            Path(plan["portableChecksum"]),
            release_directory / f"{archive_name}.sha256",
        )
        self.assertTrue(plan["archiveEnabled"])
        self.assertTrue(plan["installerEnabled"])

        compile_only = run_powershell(
            ROOT / "scripts" / "build_windows.ps1",
            "-PlanOnly",
            "-SkipArchive",
            "-SkipInstaller",
        )
        self.assertEqual(compile_only.returncode, 0, compile_only.stderr)
        compile_plan = json.loads(compile_only.stdout)
        self.assertFalse(compile_plan["archiveEnabled"])
        self.assertFalse(compile_plan["installerEnabled"])

    def test_version_increment_and_rollover(self) -> None:
        script = ROOT / "scripts" / "bump_version.ps1"
        for current, expected in (
            ("0.0.10", "0.0.11"),
            ("1.1.98", "1.1.99"),
            ("1.1.99", "1.2.10"),
        ):
            with self.subTest(current=current):
                result = run_powershell(script, "-CurrentVersion", current)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(result.stdout.strip(), expected)

    def test_invalid_versions_are_rejected(self) -> None:
        script = ROOT / "scripts" / "bump_version.ps1"
        for invalid in ("0.0.09", "01.0.10", "0.0.100", "1.2.10-beta"):
            with self.subTest(invalid=invalid):
                result = run_powershell(script, "-CurrentVersion", invalid)
                self.assertNotEqual(result.returncode, 0)

    def test_installer_receives_versioned_paths_from_build(self) -> None:
        build_script = (ROOT / "scripts" / "build_windows.ps1").read_text(encoding="utf-8")
        installer_script = (ROOT / "packaging" / "CSI.OpenBase.iss").read_text(encoding="utf-8")
        self.assertIn('"--define=MyAppVersion=$releaseVersion"', build_script)
        self.assertIn('"--define=PortableSource=$outputRoot"', build_script)
        self.assertIn('"--output-dir=$installerOutput"', build_script)
        self.assertIn("if (-not $SkipArchive)", build_script)
        self.assertNotIn("dist\\windows", build_script.lower())
        self.assertNotIn('#define MyAppVersion "', installer_script)
        self.assertIn('Source: "{#PortableSource}\\*"', installer_script)


if __name__ == "__main__":
    unittest.main()

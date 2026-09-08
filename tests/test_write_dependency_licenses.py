from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


SCRIPTS_DIRECTORY = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS_DIRECTORY))

import write_dependency_licenses as licenses  # noqa: E402


class _Metadata(dict[str, str]):
    def get_all(self, _name: str, default: list[str]) -> list[str]:
        return default


class _Distribution:
    def __init__(
        self,
        files: dict[str, Path],
        *,
        name: str = "demo",
        version: str = "1.0",
        requires: list[str] | None = None,
    ) -> None:
        self.files = list(files)
        self._files = files
        self.metadata = _Metadata(Name=name)
        self.requires = requires or []
        self.version = version

    def locate_file(self, packaged_file: object) -> Path:
        return self._files[str(packaged_file)]


class DependencyLicenseTests(unittest.TestCase):
    def test_include_package_adds_its_dependency_closure_once(self) -> None:
        root = _Distribution(
            {},
            name="csi-openbase",
            requires=["shared-runtime>=1"],
        )
        pyinstaller = _Distribution(
            {},
            name="PyInstaller",
            version="6.22",
            requires=["shared_runtime>=1"],
        )
        shared = _Distribution({}, name="shared-runtime", version="1.5")
        distributions = {
            "csi-openbase": root,
            "pyinstaller": pyinstaller,
            "shared-runtime": shared,
        }

        def find_distribution(name: str) -> _Distribution:
            return distributions[licenses._normalized_name(name)]

        with tempfile.TemporaryDirectory() as temporary_directory:
            output = Path(temporary_directory) / "licenses.txt"
            with mock.patch.object(
                licenses.importlib.metadata,
                "distribution",
                side_effect=find_distribution,
            ):
                licenses.write_report(
                    output,
                    include_packages=["PyInstaller", "pyinstaller"],
                )
            report = output.read_text(encoding="utf-8")

        self.assertEqual(report.count("\nPyInstaller 6.22\n"), 1)
        self.assertEqual(report.count("\nshared-runtime 1.5\n"), 1)
        self.assertNotIn("csi-openbase 1.0", report)

    def test_license_texts_reject_unsafe_paths_and_binary_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            valid = root / "LICENSE.txt"
            valid.write_text("Example license text\n", encoding="utf-8")
            bytecode = root / "license.pyc"
            bytecode.write_bytes(b"\x42\x0d\x0d\x0a\x00binary")
            json_file = root / "manifest.json"
            json_file.write_text('{"not": "a license"}', encoding="utf-8")
            outside = root / "outside-license"
            outside.write_text("must not be exposed", encoding="utf-8")

            distribution = _Distribution(
                {
                    "demo.dist-info/licenses/LICENSE.txt": valid,
                    "demo.dist-info/licenses/__pycache__/license.pyc": bytecode,
                    "demo.dist-info/licenses/manifest.json": json_file,
                    "../../outside/LICENSE": outside,
                    "C:/outside/NOTICE": outside,
                }
            )

            found = licenses._license_texts(distribution)

        self.assertEqual(
            found,
            [("demo.dist-info/licenses/LICENSE.txt", "Example license text")],
        )
        self.assertNotIn(str(root), repr(found))

    def test_copy_project_notices_uses_installed_dist_info_namespace(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            package_files: dict[str, Path] = {}
            expected = {
                "LICENSE": b"license bytes\n",
                "NOTICE": b"notice bytes\n",
                "THIRD-PARTY-NOTICES.md": b"third-party bytes\n",
            }
            for name, payload in expected.items():
                source = root / f"source-{name}"
                source.write_bytes(payload)
                package_files[f"csi_openbase-1.0.dist-info/licenses/{name}"] = source
            unrelated = root / "unrelated-LICENSE"
            unrelated.write_text("wrong source", encoding="utf-8")
            package_files["elsewhere/LICENSE"] = unrelated
            distribution = _Distribution(package_files)
            output = root / "backend-notices"

            with mock.patch.object(
                licenses.importlib.metadata,
                "distribution",
                return_value=distribution,
            ):
                licenses.copy_project_notices(output)

            self.assertEqual(
                {path.name: path.read_bytes() for path in output.iterdir()},
                expected,
            )

    def test_copy_project_notices_requires_all_three_files(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "LICENSE"
            source.write_text("license", encoding="utf-8")
            distribution = _Distribution(
                {"csi_openbase-1.0.dist-info/licenses/LICENSE": source}
            )

            with mock.patch.object(
                licenses.importlib.metadata,
                "distribution",
                return_value=distribution,
            ):
                with self.assertRaisesRegex(RuntimeError, "NOTICE"):
                    licenses.copy_project_notices(root / "output")


if __name__ == "__main__":
    unittest.main()

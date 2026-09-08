#!/usr/bin/env python3
"""Collect installed Python license texts for the Windows distribution."""

from __future__ import annotations

import argparse
import importlib.metadata
import re
import shutil
from collections import deque
from collections.abc import Iterable
from pathlib import Path, PurePosixPath


_REQUIREMENT_NAME = re.compile(r"^\s*([A-Za-z0-9][A-Za-z0-9_.-]*)")
_WINDOWS_ABSOLUTE_PATH = re.compile(r"^[A-Za-z]:/")
_LICENSE_NAMES = ("license", "licence", "copying", "notice", "copyright")
_LICENSE_DIRECTORIES = {"licenses", "license-files"}
_TEXT_SUFFIXES = {
    "",
    ".apache",
    ".bsd",
    ".copying",
    ".htm",
    ".html",
    ".lesser",
    ".license",
    ".md",
    ".mit",
    ".notice",
    ".rst",
    ".txt",
}
_MAX_LICENSE_BYTES = 4 * 1024 * 1024
_PROJECT_NOTICES = {
    "license": "LICENSE",
    "notice": "NOTICE",
    "third-party-notices.md": "THIRD-PARTY-NOTICES.md",
}


def _normalized_name(value: str) -> str:
    return re.sub(r"[-_.]+", "-", value).casefold()


def _distribution(root_name: str) -> importlib.metadata.Distribution:
    try:
        return importlib.metadata.distribution(root_name)
    except importlib.metadata.PackageNotFoundError as exc:
        raise RuntimeError(
            f"{root_name} must be installed before collecting dependency licenses"
        ) from exc


def _dependency_closure(
    root_name: str,
    include_packages: Iterable[str] = (),
) -> list[importlib.metadata.Distribution]:
    seed_names = [root_name, *include_packages]
    queue = deque(_distribution(name) for name in seed_names)
    found: dict[str, importlib.metadata.Distribution] = {}
    while queue:
        distribution = queue.popleft()
        name = str(distribution.metadata.get("Name") or "").strip()
        if not name:
            continue
        key = _normalized_name(name)
        if key in found:
            continue
        found[key] = distribution
        for requirement in distribution.requires or ():
            match = _REQUIREMENT_NAME.match(requirement)
            if not match:
                continue
            try:
                queue.append(importlib.metadata.distribution(match.group(1)))
            except importlib.metadata.PackageNotFoundError:
                # Inactive environment markers and unselected extras are absent.
                continue
    return [found[key] for key in sorted(found)]


def _safe_packaged_path(packaged_file: object) -> str | None:
    raw = str(packaged_file).replace("\\", "/")
    if (
        not raw
        or "\x00" in raw
        or raw.startswith("/")
        or _WINDOWS_ABSOLUTE_PATH.match(raw)
    ):
        return None
    parts = raw.split("/")
    if any(part in {"", ".", ".."} for part in parts):
        return None
    return PurePosixPath(*parts).as_posix()


def _is_license_path(relative: str) -> bool:
    path = PurePosixPath(relative)
    parts = tuple(part.casefold() for part in path.parts)
    leaf = path.name.casefold()
    if "__pycache__" in parts or path.suffix.casefold() not in _TEXT_SUFFIXES:
        return False
    return leaf.startswith(_LICENSE_NAMES) or any(
        part in _LICENSE_DIRECTORIES for part in parts[:-1]
    )


def _read_license_text(source: Path) -> str | None:
    try:
        if not source.is_file() or source.stat().st_size > _MAX_LICENSE_BYTES:
            return None
        payload = source.read_bytes()
    except OSError:
        return None
    if not payload or b"\x00" in payload:
        return None
    try:
        text = payload.decode("utf-8-sig")
    except UnicodeDecodeError:
        return None
    if any(ord(character) < 32 and character not in "\t\r\n" for character in text):
        return None
    return text if text.strip() else None


def _license_texts(
    distribution: importlib.metadata.Distribution,
) -> list[tuple[str, str]]:
    texts: list[tuple[str, str]] = []
    seen: set[str] = set()
    for packaged_file in sorted(distribution.files or (), key=lambda item: str(item)):
        relative = _safe_packaged_path(packaged_file)
        if relative is None or not _is_license_path(relative):
            continue
        source = Path(distribution.locate_file(packaged_file))
        content = _read_license_text(source)
        if content is None:
            continue
        normalized_content = content.strip()
        if normalized_content in seen:
            continue
        seen.add(normalized_content)
        texts.append((relative, normalized_content))
    return texts


def copy_project_notices(output_directory: Path, root_name: str = "csi-openbase") -> None:
    distribution = _distribution(root_name)
    sources: dict[str, Path] = {}
    for packaged_file in sorted(distribution.files or (), key=lambda item: str(item)):
        relative = _safe_packaged_path(packaged_file)
        if relative is None:
            continue
        path = PurePosixPath(relative)
        parts = tuple(part.casefold() for part in path.parts)
        is_dist_info_license = any(
            part.endswith(".dist-info") and index + 1 < len(parts)
            and parts[index + 1] == "licenses"
            for index, part in enumerate(parts)
        )
        destination_name = _PROJECT_NOTICES.get(path.name.casefold())
        if not is_dist_info_license or destination_name is None:
            continue
        source = Path(distribution.locate_file(packaged_file))
        if _read_license_text(source) is not None:
            sources.setdefault(destination_name, source)

    missing = sorted(set(_PROJECT_NOTICES.values()) - set(sources))
    if missing:
        raise RuntimeError(
            f"{root_name} distribution is missing required notice files: "
            + ", ".join(missing)
        )

    output_directory.mkdir(parents=True, exist_ok=True)
    for destination_name in sorted(sources):
        shutil.copyfile(sources[destination_name], output_directory / destination_name)


def write_report(
    output_path: Path,
    root_name: str = "csi-openbase",
    include_packages: Iterable[str] = (),
) -> None:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    sections = [
        "CSI OpenBase bundled Python dependency licenses",
        "Generated from the installed build environment. Do not edit manually.",
    ]
    for distribution in _dependency_closure(root_name, include_packages):
        name = str(distribution.metadata.get("Name") or "unknown")
        if _normalized_name(name) == _normalized_name(root_name):
            continue
        version = distribution.version
        expression = str(distribution.metadata.get("License-Expression") or "").strip()
        declared = expression or str(distribution.metadata.get("License") or "").strip()
        classifiers = [
            value
            for value in distribution.metadata.get_all("Classifier", [])
            if value.startswith("License ::")
        ]
        sections.extend(["", "=" * 78, f"{name} {version}"])
        if declared:
            sections.append(f"Declared license: {declared}")
        for classifier in classifiers:
            sections.append(f"Classifier: {classifier}")
        license_texts = _license_texts(distribution)
        if not license_texts:
            sections.append("License file: not included in the installed distribution metadata")
        for relative, content in license_texts:
            sections.extend(["", f"--- {relative} ---", content])
    output_path.write_text("\n".join(sections) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--project-notices-dir", type=Path, required=True)
    parser.add_argument("--root-package", default="csi-openbase")
    parser.add_argument("--include-package", action="append", default=[])
    arguments = parser.parse_args()
    root_name = arguments.root_package
    copy_project_notices(arguments.project_notices_dir.resolve(), root_name)
    write_report(
        arguments.output.resolve(),
        root_name,
        arguments.include_package,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

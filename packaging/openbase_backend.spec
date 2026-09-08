# -*- mode: python ; coding: utf-8 -*-

import os
from pathlib import Path

from PyInstaller.utils.hooks import collect_all, collect_data_files, collect_submodules


SPEC_DIRECTORY = Path(SPECPATH).resolve()
browser_root_value = os.environ.get("CSI_OPENBASE_PLAYWRIGHT_BROWSERS", "")
browser_root = Path(browser_root_value).resolve() if browser_root_value else None
if browser_root is None or not browser_root.is_dir():
    raise SystemExit(
        "CSI_OPENBASE_PLAYWRIGHT_BROWSERS must point to a populated browser directory"
    )
playwright_datas, playwright_binaries, playwright_hidden = collect_all("playwright")
browser_datas = [
    (str(path), f"ms-playwright/{path.name}")
    for path in browser_root.iterdir()
    if not path.name.startswith(".")
]

analysis = Analysis(
    [str(SPEC_DIRECTORY / "backend_entry.py")],
    pathex=[],
    binaries=playwright_binaries,
    datas=collect_data_files("admin_app") + browser_datas + playwright_datas,
    hiddenimports=playwright_hidden
    + collect_submodules("uvicorn")
    + ["anyio._backends._asyncio"],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=["numpy", "pytest", "tkinter", "trio"],
    noarchive=False,
    optimize=1,
)
pyz = PYZ(analysis.pure)

exe = EXE(
    pyz,
    analysis.scripts,
    [],
    exclude_binaries=True,
    name="CSI.OpenBase.Backend",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=False,
    disable_windowed_traceback=False,
)

bundle = COLLECT(
    exe,
    analysis.binaries,
    analysis.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name="CSI.OpenBase.Backend",
)

# WinForms Project Guidance

Canonical remote: `git@github.com:CSI-OpenBase/winform.git`

This repository owns the Windows WinForms/WebView2 host and Windows packaging.
Creator authorization, collection, archives, comments, and the local web UI belong
to the `csi-openbase` Python package from
`git@github.com:CSI-OpenBase/local-web.git`.

- Do not duplicate backend business logic in C#.
- Keep this repository independently cloneable; sibling backend source is an
  optional development input, not an aggregate-repository requirement.
- Preserve authenticated loopback health checks and full process-tree cleanup.
- Keep WebView messages source-checked against the active authenticated backend.
  Native directory pickers may return a user selection to the page, but path
  validation, persistence, and export behavior remain owned by the Python backend.
- Keep source-checkout, wheel, and frozen-backend development paths working.
- Keep all bundled legal files and dependency reports in release artifacts.
- Treat the root `VERSION` file as the only application and release version source.
  Versions use `x.x.xx`, start at `0.0.10`, and roll `1.1.99` to `1.2.10`.
- Complete builds belong under `Release/<version>/`; rebuilds may replace
  only that version directory and must preserve other release directories.
  The sole local-testing exception is `Release/local/`, owned by
  `scripts/build_local.ps1`.
- Do not commit `build/`, `dist/`, `Release/`, `bin/`, `obj/`, user data, or
  browser profiles.

## Build Modes

- **Local testing** is the default for frequent WinForms changes. Run
  `scripts/build_local.ps1`, which incrementally publishes only the native host
  to the fixed `Release/local/` directory. Rely on the adjacent Python source
  checkout or set `CSI_OPENBASE_BACKEND` to an already frozen backend. Do not
  bump `VERSION`, run `build_windows.ps1`, or download/copy Playwright Chromium
  for routine local testing. The script may replace only `Release/local/`.
- **Release builds** are deliberate full builds. Bump `VERSION`, verify the
  Python input, run the complete test suites, then use `build_windows.ps1` to
  recreate `Release/<version>/`, freeze the backend, install and embed the
  matching Chromium, collect legal files, and create the requested archive and
  installer. A local compile-only release candidate may use `-SkipArchive
  -SkipInstaller`, but it is not the routine local-testing path.
- Run a full release build whenever Python backend code, Python dependencies,
  Playwright, packaging inputs, or release contents change. WinForms-only UI
  iteration stays on the local-testing path until a release candidate is
  requested.

Verify changes with:

```powershell
dotnet build CSI.OpenBase.Desktop.slnx -c Release
python -m unittest discover -s tests -v
```

# CSI OpenBase WinForms

<p align="center">
  <img src=".github/assets/csi-openbase-logo.svg" alt="CSI OpenBase" width="420">
</p>

<p align="center">
  <strong>English</strong> | <a href="README.zh-CN.md">简体中文</a>
</p>

This project is the Windows Forms and WebView2 host for CSI OpenBase. It owns the
Windows UI, desktop settings, backend process lifetime, portable distribution, and
installer. Creator authorization, collection, archiving, and the local web UI are
provided by the separately versioned `csi-openbase` Python package.

Canonical repository: [CSI-OpenBase/winform](https://github.com/CSI-OpenBase/winform)

SSH clone: `git@github.com:CSI-OpenBase/winform.git`

The Python backend is maintained separately in
[CSI-OpenBase/local-web](https://github.com/CSI-OpenBase/local-web).

## Interface Preview

These screenshots use an isolated demonstration workspace and synthetic data to
show the local management interface embedded in the WinForms application.

### Local Archive Overview

![CSI OpenBase local archive overview](.github/assets/screenshots/local-archive-overview.png)

### Video Archive and Comment Collection

![Video archive, comment counts, and incremental export actions](.github/assets/screenshots/video-archive.png)

### Data Management

![Clear platform exports, comments, or all local data by scope](.github/assets/screenshots/clear-data-dialog.png)

### Settings

![Configure the comment export directory](.github/assets/screenshots/settings.png)

## First Run

On first launch, the desktop host requires the user to choose a work directory
before it starts the local backend. Exported tables, video archives, comment
data, and the local index are stored there. The selected path is persisted for
later launches and can be changed from the main window. Cancelling the initial
picker leaves the backend stopped until a directory is selected.

The Windows shell includes a collapsible task panel beside the local web UI. It
shows the current workspace's active count and recent task status already
presented by the authenticated local page and follows the page's live task
updates. The panel is read-only; task creation and execution remain owned by
the Python backend and local web UI. Task times, refresh times, and desktop log
timestamps are always displayed in Beijing time (`UTC+08:00`), regardless of
the Windows system timezone. A single top toolbar groups workspace settings,
logs, tasks, About, and backend restart controls. The About dialog shows the
project author, official GitHub repository, application version, and executable
update date.

## Development

Requirements:

- Windows 10 or later
- .NET 10 SDK
- Microsoft Edge WebView2 Runtime
- Python 3.12 or later when running the Python backend from source or a wheel

From this directory, build or run the desktop host with:

```powershell
dotnet build .\CSI.OpenBase.Desktop\CSI.OpenBase.Desktop.csproj
dotnet run --project .\CSI.OpenBase.Desktop\CSI.OpenBase.Desktop.csproj
```

When a separately cloned `local-web` checkout is placed in a sibling `../python`
directory, the host walks upward only from its own application directory to
discover `python\scripts\run_openbase.py`; it never uses
the process's current working directory for backend discovery. It prefers the Python project's
`.venv\Scripts\python.exe`, then `venv\Scripts\python.exe`, then `python` on
`PATH`.

To run against an installed Python wheel without a sibling source tree, specify the
interpreter that contains `csi-openbase`:

```powershell
$env:CSI_OPENBASE_PYTHON = "C:\path\to\venv\Scripts\python.exe"
dotnet run --project .\CSI.OpenBase.Desktop\CSI.OpenBase.Desktop.csproj
```

The configured interpreter is launched as `python -m scripts.run_openbase`.
`CSI_OPENBASE_BACKEND` can instead point directly to a frozen backend executable;
this explicit override takes precedence over packaged and source backends.

## Build Modes

### Local Testing: Frequent Builds

For routine WinForms UI and interaction work, use the fixed local build entry point:

```powershell
.\scripts\build_local.ps1
```

The output path is always:

```text
D:\www\csi\csi-openbase\winform\Release\local\CSI.OpenBase.Desktop.exe
```

To launch immediately after compilation:

```powershell
.\scripts\build_local.ps1 -Run
```

You can also reuse an existing frozen backend:

```powershell
$version = (Get-Content .\VERSION -Raw).Trim()
$env:CSI_OPENBASE_BACKEND = `
  "$PWD\Release\$version\portable\backend\CSI.OpenBase.Backend.exe"
.\scripts\build_local.ps1 -Run
```

Local testing does not bump `VERSION`, run `build_windows.ps1`, or download or
copy Playwright Chromium again. It only cleans and rebuilds `Release\local` and
does not modify any `Release\<version>` directory. Regenerate the frozen backend
only when the Python backend or its dependencies change.

### Release: Complete Build

For a formal release, bump `VERSION`, complete the tests, and then run
`build_windows.ps1`. The full workflow rebuilds an isolated Python environment,
freezes the backend, installs and embeds the matching Chromium, gathers license
materials, and optionally creates the ZIP archive and installer.
`-SkipArchive -SkipInstaller` is only for a complete local release-candidate
build, not routine WinForms testing.

## Versioning

The root `VERSION` file is the only source for the desktop application and release
version. Versions use `x.x.xx`; the patch component runs from `10` through `99`.
The initial version is `0.0.10`, and a rollover such as `1.1.99` produces
`1.2.10`.

Preview or apply the next version with:

```powershell
.\scripts\bump_version.ps1
.\scripts\bump_version.ps1 -Apply
```

The build script reads this value for the executable metadata, installer, and
release directory. Use `-PlanOnly` to inspect all output paths without building.

## Backend Contract

Installed and portable distributions place the frozen backend at:

```text
backend\CSI.OpenBase.Backend.exe
```

The host chooses an available loopback port and sets `CSI_OPENBASE_HOME`,
`CSI_OPENBASE_HOST`, `CSI_OPENBASE_PORT`, `CSI_OPENBASE_DESKTOP_TOKEN`,
`CSI_OPENBASE_INSTANCE_NONCE`, and `CSI_OPENBASE_SESSION_SECRET` for the child
process. It requires an authenticated `/health` response for the same process
instance before navigating WebView2.

While connected, the native task panel mirrors the task records rendered by the
authenticated local page. It does not add another task API polling loop. A backend
restart or workspace change invalidates any in-flight task refresh before the new
page state is shown.

The local page includes a Settings entry for an optional comment export directory.
On Windows, its directory button uses the host's native folder picker through a
source-checked WebView2 message. The Python backend validates and persists the
selection; the host does not duplicate comment-export business logic.
The same page provides incremental comment delivery and an explicit full resync.
Incremental runs retain a complete validated snapshot locally while exporting only
new or materially changed comments plus the relationship context needed to keep
reply threads independently valid.

On exit, the host first posts the token to `/api/shutdown`. If the endpoint or
process does not respond in time, it terminates the complete backend process tree.
User settings, logs, WebView2 state, and isolated creator browser sessions are kept
under `%LOCALAPPDATA%\CSI OpenBase`; creator data remains in the work directory
selected in the application.

## Windows Distribution

The complete build consumes either a Python source directory or a wheel. It creates
an isolated Python environment under `build/python-env`, installs the selected
package and its runtime dependencies there, and packages only that installed
distribution. It does not install anything into the user's active Python
environment.

When a separate `local-web` checkout is available as sibling `../python`, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_windows.ps1 -SkipInstaller
```

To build only the runnable directory without a ZIP archive or installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_windows.ps1 `
  -PythonSource ..\python -SkipArchive -SkipInstaller
```

From a standalone `winform` checkout, clone `local-web` separately and pass its
path with `-PythonSource`, or consume a published wheel with `-PythonWheel`.

Use another source checkout explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_windows.ps1 `
  -PythonSource C:\source\local-web -SkipInstaller
```

Or build from a release wheel without any Python source tree:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build_windows.ps1 `
  -PythonWheel C:\artifacts\csi_openbase-<backend-version>-py3-none-any.whl -SkipInstaller
```

`-PythonSource` and `-PythonWheel` are mutually exclusive. The build installs the
matching Playwright Chromium, verifies Microsoft's Authenticode signature on the
WebView2 bootstrapper, publishes the .NET host as self-contained `win-x64`, and
collects the applicable dependency license materials. Other runtime identifiers are
not supported; the build also rejects a non-64-bit Python interpreter.
The generated Python license report explicitly includes PyInstaller and its
dependency closure because its bootloader and runtime enter the distribution.

Artifacts are project-local:

- `Release/<version>/portable/` is the complete portable directory.
- `Release/<version>/portable/backend/` contains the frozen Python runtime.
- `Release/<version>/CSI-OpenBase-<version>-win-x64-portable.zip` is the
  distributable archive; the adjacent `.sha256` file verifies it.
- `Release/<version>/installer/` contains the Inno Setup installer when
  it is enabled.

`-SkipArchive` retains the complete `portable/` build but does not create a ZIP
or SHA-256 file. Use it together with `-SkipInstaller` for a local build-only
release candidate.

Each build recreates only the directory for the current version and preserves
other version directories under `Release/`.

Omit `-SkipInstaller` only on a machine with Inno Setup's `ISCC.exe` on `PATH`.
A missing compiler is a build failure so automation cannot mistake a portable-only
output for an installer. The installed Python package's own legal files are copied
from its distribution metadata to the portable directory's `licenses/backend/`.
Sign the desktop executable, frozen backend, and installer before public
distribution.

## Host-Only Publish

For UI development, publish only the .NET host with:

```powershell
dotnet publish .\CSI.OpenBase.Desktop\CSI.OpenBase.Desktop.csproj `
  -c Release -r win-x64 --self-contained true
```

The complete portable application requires the frozen backend directory and the
WebView2 bootstrapper produced by `scripts/build_windows.ps1`.

## License

CSI OpenBase WinForms is licensed under the Apache License 2.0. See `LICENSE` and
`NOTICE`. Bundled third-party license materials are described in
`THIRD-PARTY-NOTICES.md`.

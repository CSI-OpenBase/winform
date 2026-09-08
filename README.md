# CSI OpenBase WinForms

This project is the Windows Forms and WebView2 host for CSI OpenBase. It owns the
Windows UI, desktop settings, backend process lifetime, portable distribution, and
installer. Creator authorization, collection, archiving, and the local web UI are
provided by the separately versioned `csi-openbase` Python package.

Canonical repository: [CSI-OpenBase/winform](https://github.com/CSI-OpenBase/winform)

SSH clone: `git@github.com:CSI-OpenBase/winform.git`

The Python backend is maintained separately in
[CSI-OpenBase/local-web](https://github.com/CSI-OpenBase/local-web).

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
  -PythonWheel C:\artifacts\csi_openbase-0.1.0-py3-none-any.whl -SkipInstaller
```

`-PythonSource` and `-PythonWheel` are mutually exclusive. The build installs the
matching Playwright Chromium, verifies Microsoft's Authenticode signature on the
WebView2 bootstrapper, publishes the .NET host as self-contained `win-x64`, and
collects the applicable dependency license materials. Other runtime identifiers are
not supported; the build also rejects a non-64-bit Python interpreter.
The generated Python license report explicitly includes PyInstaller and its
dependency closure because its bootloader and runtime enter the distribution.

Artifacts are project-local:

- `dist/windows/` is the complete portable directory.
- `dist/windows/backend/` contains the isolated frozen Python runtime.
- `dist/installer/` contains the Inno Setup installer.

Omit `-SkipInstaller` only on a machine with Inno Setup's `ISCC.exe` on `PATH`.
A missing compiler is a build failure so automation cannot mistake a portable-only
output for an installer. Portable-only builds remove any stale `dist/installer/`
output before starting. The installed Python package's own legal files are copied
from its distribution metadata to `dist/windows/licenses/backend/`. Sign the desktop executable, frozen backend, and installer
before public distribution.

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

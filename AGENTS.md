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
- Keep source-checkout, wheel, and frozen-backend development paths working.
- Keep all bundled legal files and dependency reports in release artifacts.
- Do not commit `build/`, `dist/`, `bin/`, `obj/`, user data, or browser profiles.

Verify changes with:

```powershell
dotnet build CSI.OpenBase.Desktop.slnx -c Release
python -m unittest discover -s tests -v
```

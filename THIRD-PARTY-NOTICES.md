# Third-Party Notices

CSI OpenBase for Windows bundles the CSI OpenBase Python backend and its local web
interface. The interface includes these browser-side dependencies:

- htmx 2.0.4, licensed under the Zero-Clause BSD License. See
  `licenses/htmx-0BSD.txt`.
- Lucide 0.468.0, licensed under the ISC License. See
  `licenses/lucide-ISC.txt`.

The Windows distribution also includes Microsoft Edge WebView2, the .NET runtime,
CPython, Playwright Chromium, and Python runtime dependencies. Their applicable
license and notice materials are collected under the distribution's `licenses/`
directory by `scripts/build_windows.ps1`. Chromium's own license files remain in
the bundled `backend/_internal/ms-playwright` tree.

"""PyInstaller entry point for the installed CSI OpenBase Python package."""

from scripts.run_openbase import main


if __name__ == "__main__":
    raise SystemExit(main())

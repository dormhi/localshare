# Tablet Display

Open-source Windows 11 and Android project for using an Android tablet as a wired extended desktop display.

## Current state

This repository is in the foundation phase. The Windows WinUI 3 host shell and Android Compose viewer shell are placeholders only. USB/ADB transport, the IddCx display driver, video streaming, display settings, installation, and touch input are not implemented yet.

The detailed product decisions and phase workflow are in local-only `AGENTS.md` and `plan.md` files in the maintainer's working copy. Those two files are deliberately not published in this public repository. Public technical setup and build instructions are maintained in [`docs/development.md`](docs/development.md).

## Components

- `src/windows/TabletDisplay.Host/`: Windows host UI shell (C#, WinUI 3).
- `src/windows/driver/`: reserved for the C++ IddCx driver; no driver implementation yet.
- `src/android/app/`: Android viewer shell (Kotlin, Jetpack Compose).
- `protocol/`: reserved for a later approved host/client protocol.

## License

Apache-2.0. See [`LICENSE`](LICENSE).

# Tablet Display

Open-source Windows 11 and Android project for using an Android tablet as a wired extended desktop display.

## Current state

Phase 3 is in progress. The committed Android app has the permanent ID `com.dormhi.localshare` and an abstract local ADB socket handshake endpoint. The host-side ADB adapter and WinUI connection UI are awaiting Windows testing and remain uncommitted. No Windows/tablet end-to-end handshake has passed yet. The IddCx display driver, desktop video streaming, settings, installation, and touch input are not implemented.

See [`docs/project-status.md`](docs/project-status.md) for progress, test results, remaining work, and the agent reading map. The complete product decisions and phase workflow are in local-only `AGENTS.md` and `plan.md`; those two files are deliberately not published in this repository and must be copied into a fresh checkout. Build instructions are in [`docs/development.md`](docs/development.md), Phase 3 decisions in [ADR-003](docs/decisions/ADR-003-phase-3-android-identity-and-usb-handshake.md), protocol details in [`protocol/README.md`](protocol/README.md), and ordered Windows/device tests in [`docs/testing/phase-3-windows-checklist.md`](docs/testing/phase-3-windows-checklist.md).

## Components

- `src/windows/TabletDisplay.Host/`: Windows host UI shell (C#, WinUI 3).
- `src/windows/driver/`: reserved for the C++ IddCx driver; no driver implementation yet.
- `src/android/app/`: Android viewer and Phase 3 local ADB endpoint (Kotlin, Jetpack Compose).
- `protocol/`: Phase 3 handshake/control-message contract; image streaming is out of scope so far.

## License

Apache-2.0. See [`LICENSE`](LICENSE).

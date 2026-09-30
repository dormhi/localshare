# Windows components

Contains the WinUI host shell and the Phase 3 ADB lifecycle draft. Phase 4 adds a USB-owned monitor control draft and driver sources; the actual IddCx project still requires Windows. Keep user-mode host concerns separate from driver callbacks and follow the root `AGENTS.md` test, security, signing, and user-approval gates. Host build/runtime and driver validation remain unverified on Windows.

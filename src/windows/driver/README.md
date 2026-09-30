# C++ IddCx driver (Phase 4 source draft)

The approved display-driver direction is a separate C++ UMDF indirect display driver using Microsoft's supported IddCx APIs. Phase 4 now has portable-tested logic under `core/` and uncompiled Windows source under `iddcx/`. This is a source draft, not a built, signed or validated driver. Read [WDK-HANDOFF.md](WDK-HANDOFF.md) before Windows integration. See the [design preparation](../../../docs/decisions/phase-4-driver-design.md) and [combined Windows checklist](../../../docs/testing/phase-3-4-windows-checklist.md).

Create the actual WDK project from the current Windows Driver Kit/Visual Studio environment in the Windows driver phase. Do not fabricate project metadata from this placeholder or introduce a kernel-mode display driver. Driver build, signing, installation, and runtime checks require Windows 11 and explicit approval for installation or machine configuration changes.

Useful primary references, summarized for offline planning:

- IddCx/UMDF represents a virtual adapter and monitor, reports display modes, and processes desktop images supplied through a Direct3D swap chain. IddCx drivers are user-mode; the Microsoft sample is illustrative, not production-ready: <https://learn.microsoft.com/en-us/windows-hardware/drivers/display/indirect-display-driver-model-overview> (checked 2026-09-23).
- Test signing and public driver distribution use different signing workflows. Never store a private signing key in this tree: <https://learn.microsoft.com/en-us/windows-hardware/drivers/install/driver-signing> (checked 2026-09-23).

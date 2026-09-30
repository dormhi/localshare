# Decision records

Store accepted product and technical decisions here. Create one Markdown record per decision using `000-template.md`. Include context, options, recommendation, the user's decision, consequences, date, and any sources reviewed. Do not replace a decision silently; add a superseding record and link the prior decision.

## Current records

- [ADR-001: Component stack](ADR-001-component-stack.md) — Phase 1 stack selection. Its temporary Android package ID was superseded by ADR-003.
- [ADR-002: Buildable foundation](ADR-002-buildable-foundation.md) — Phase 2 toolchain and verification baseline.
- [ADR-003: Phase 3 Android identity and ADB USB handshake](ADR-003-phase-3-android-identity-and-usb-handshake.md) — selected permanent package ID, abstract socket path, token bootstrap, and Phase 3 trust boundary.

- [ADR-004: USB-owned display](ADR-004-phase-4-usb-owned-display.md) — accepted Phase 4 lifecycle and macOS work scope; Windows validation pending.

Phase 4 working notes: [driver design](phase-4-driver-design.md) records the approved virtual modes and remaining Windows dependencies.

For current test status and the Windows step sequence, see [project status](../project-status.md) and the [Phase 3 Windows checklist](../testing/phase-3-windows-checklist.md).

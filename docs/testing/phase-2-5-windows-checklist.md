# Combined deferred validation — Phases 2 through 5

Owner scheduling decision: 2026-09-30. Phase 5 is active on macOS; unavailable Windows/tablet checks from Phases 2–4 are carried into this Windows session. Starting a later phase does not pass, waive or replace an earlier acceptance check. Keep separate results and fix failures in the affected component before claiming phase completion.

## Order and prerequisites

1. Transfer the complete current working tree and ignored `AGENTS.md` / `plan.md`. A bundle contains only commits. The Phase 4 handoff archive predates Phase 5 and must not be mistaken for the latest source.
2. Run the host build/launch baseline from the [Phase 3 checklist](phase-3-windows-checklist.md), then the USB-only checks with virtual display and video disabled. Record Windows architecture, version and relevant tool versions without device serials or keys.
3. Run Phase 5's generated-pattern proof independently of the IddCx driver. Use one authorized tablet at a time, then repeat for the other target. Inspect the Android merged manifest, negotiated codec/profile and supported decode size/rate. Do not infer physical panel refresh from stream FPS.
4. Exercise malformed/truncated/oversized video packets, unsupported codec/profile, slow decoding, backpressure, stop/cable pull/reconnect and surface destruction/rotation. Verify bounded application memory, useful failure state and exact cleanup of the session's ADB forwards. Do not log/store image data or tokens.
5. Follow the [Phase 4 driver checklist](phase-3-4-windows-checklist.md) and [WDK handoff](../../src/windows/driver/WDK-HANDOFF.md) for real WDK project creation, source fixes, static analysis, ACL review and explicitly approved install/recovery tests. Retain x64 and ARM64 as separate coverage rows. No driver installation or boot/security-setting action is authorized by the scheduling change.
6. Rerun the affected regression tests after fixes. The generated pattern must not be fed from the IddCx desktop: actual desktop-stream integration remains Phase 6 and is not authorized here.

## Result matrix

| Phase / check | Status | Evidence |
| --- | --- | --- |
| 2: Windows host baseline | PASS x64, 2026-10-01 | Visible build/launch/close; ARM64 cross-build only; [report](phase-2-5-windows-report.md) |
| 3: host no-device / unauthorized state | PARTIAL | Real zero-device UI PASS; synthetic unauthorized PASS; physical unauthorized pending |
| 3: Samsung USB handshake/lifecycle | NOT RUN | Deferred |
| 3: Huawei USB handshake/lifecycle | NOT RUN | Deferred |
| 3: malformed handshake / heartbeat / exact cleanup | PARTIAL | Synthetic actual-adapter tests PASS; physical USB pending |
| 4: WDK x64 build/static analysis | NOT RUN | Template-generated project prepared; signed kit installation pending |
| 4: WDK ARM64 build/static analysis | NOT RUN | Template-generated project prepared; signed kit installation pending |
| 4: access policy / signing / install / rollback | NOT RUN | Windows environment and explicit action approvals required |
| 4: modes / extended desktop / stop / reboot / USB-owned cleanup | NOT RUN | Deferred |
| 5: Windows encoder build and generated-pattern output | NOT RUN | Windows required |
| 5: Samsung decoder capabilities and rendered pattern | NOT RUN | Physical tablet required |
| 5: Huawei Android runtime, decoder capabilities and rendered pattern | NOT RUN | Physical tablet required |
| 5: unsupported format and malformed stream rejection | NOT RUN | Runtime tests pending |
| 5: memory / backpressure / slow decoding | NOT RUN | Runtime measurements pending |
| 5: disconnect / surface lifecycle / all session-forward cleanup | NOT RUN | Runtime tests pending |

For each run record command, date, source revision plus uncommitted snapshot identity, sanitized diagnostics, observed result, and any required follow-up. A static check or mock codec does not count as a rendered video, Windows encoder or physical USB pass.

## Activation-document checks — 2026-09-30

PASS: `python3 tools/validate_scaffold.py` (including the configured secret/permission checks); `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -p 'test_*.py'` (8/8 static contracts); `git diff --check`. These checks validate this documentation checkpoint and existing source contracts; they do not satisfy any Windows/device/video row above.

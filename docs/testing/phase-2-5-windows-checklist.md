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
| 3: host no-device / unauthorized state | PASS | Real zero-device UI and physical unauthorized Samsung (2026-10-15) |
| 3: Samsung USB handshake/lifecycle | PASS, 2026-10-15 | SM-X130 Android 16; see [Phase 3 checklist](phase-3-windows-checklist.md) findings |
| 3: Huawei USB handshake/lifecycle | NOT RUN | Deferred |
| 3: malformed handshake / heartbeat / exact cleanup | PASS (Samsung) | Synthetic adapter tests plus physical heartbeat, cable pull and exact cleanup; Huawei pending |
| 4: WDK x64 build/static analysis | NOT RUN | Template-generated project prepared; signed kit installation pending |
| 4: WDK ARM64 build/static analysis | NOT RUN | Template-generated project prepared; signed kit installation pending |
| 4: access policy / signing / install / rollback | NOT RUN | Windows environment and explicit action approvals required |
| 4: modes / extended desktop / stop / reboot / USB-owned cleanup | NOT RUN | Deferred |
| 5: Windows encoder build and generated-pattern output | PASS x64, 2026-10-15 | Hardware-first helper (NVENC on RTX 2060) and software fallback, 1–144 FPS conformance; commit 57e1e97 |
| 5: Samsung decoder capabilities and rendered pattern | PASS, 2026-10-15 | SM-X130: smooth, artefact-free pattern at 30, 90 and 144 FPS limits; decoder advertises at least 144 FPS at 1280×800; panel reports 90 Hz |
| 5: Huawei Android runtime, decoder capabilities and rendered pattern | NOT RUN | Physical tablet required |
| 5: unsupported format and malformed stream rejection | PARTIAL | Synthetic Kotlin/C# suites PASS; not injected on a physical tablet |
| 5: memory / backpressure / slow decoding | PARTIAL | Credit backpressure observed on SM-X130 (144 requested, 104.7 accepted); tablet memory not measured |
| 5: disconnect / surface lifecycle / all session-forward cleanup | PASS (Samsung) | Disconnect, rotation and mid-stream cable pull: encoder exits, forwards removed, no tablet sockets or codec left |

For each run record command, date, source revision plus uncommitted snapshot identity, sanitized diagnostics, observed result, and any required follow-up. A static check or mock codec does not count as a rendered video, Windows encoder or physical USB pass.

## Activation-document checks — 2026-09-30

PASS: `python3 tools/validate_scaffold.py` (including the configured secret/permission checks); `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -p 'test_*.py'` (8/8 static contracts); `git diff --check`. These checks validate this documentation checkpoint and existing source contracts; they do not satisfy any Windows/device/video row above.

## Samsung Galaxy Tab A11 video session — 2026-10-15

Device SM-X130 (Android 16 / SDK 36, panel 800×1340 at 213 dpi, 90 Hz reported); host Windows 11 x64, RTX 2060, Platform-Tools 37.0.1. Generated 1280×800 Baseline H.264 pattern only; no desktop capture.

| Measurement | Result |
| --- | --- |
| Encoder selected | NVIDIA H.264 Encoder MFT (hardware) |
| Host CPU at 90 FPS limit, 10 s, share of whole 12-thread PC | encoder helper 1.1 %, host app 0.3 %, adb 0.2 % |
| Host CPU at 144 FPS limit | encoder helper 1.2 %, host app 0.5 %, adb 0.4 % |
| NVIDIA encoder engine / GPU utilisation | 10 % / 11–13 % at 90 FPS; 12 % / 15–16 % at 144 FPS |
| Accepted frame rate at 144 FPS limit | 104.7 frames/s (one frame in flight; round trip ≈ 9.5 ms) |

Findings for later phases (not defects of the Phase 5 proof):

- The video fills only a box under the app's status text. A real second screen must be full-screen and immersive (Phase 6).
- Rotating the tablet recreates the activity and ends the whole session safely. A real second screen must survive rotation and re-layout instead (Phase 6 lifecycle).
- With one frame in flight, throughput tops out near 105 FPS over ADB. 120/144 Hz panels will need two frames in flight, still within the approved two-frame application buffer (Phase 7/8).
- Stream FPS above the panel refresh (144 on a 90 Hz panel) costs power without visible benefit; settings should default to the panel rate and warn above it (Phase 7).
- Fixed during the session: a mid-stream cable pull reported "Video cleanup failed" because adb had already removed the device's forwards; video forward removal is now best effort like the control forward.

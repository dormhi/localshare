# Project status and agent reading map

Status checked: 2026-10-01. See the [Windows checkpoint report](testing/phase-2-5-windows-report.md) for commands, tool versions, coverage and outstanding gates.

## Current phase

**Phase 5 activation (owner request, 2026-09-30):** generated-image transport/decoder proof is active. OS H.264 and a bounded, authenticated secondary ADB channel are approved; 30 FPS is a default, with other rates subject to measured encoder/decoder support. Windows development now covers outstanding checks from Phases 2–5 in the [validation index](testing/phase-2-5-windows-checklist.md). No desktop-stream integration is authorized.

**v0.2 Phase 4 — IddCx proof: macOS source draft prepared, Windows validation pending; Phase 3 USB handshake validation remains open.** The owner selected Android application ID `com.dormhi.localshare` and ADB forwarding to Android's abstract `LocalServerSocket` endpoint. No Phase 3 product decisions remain open.

The Android identity and endpoint are in local commit `dfe35be` (`feat(android): add ADB handshake endpoint`). The Windows ADB lifecycle adapter and host UI changes are currently in the maintainer's working tree and are not committed. The end-to-end USB handshake has not been validated on Windows or on either tablet.

Phase 2's Windows x64 host baseline **PASSed on 2026-10-01**: restore/build, visible window and normal close. ARM64 cross-build passed; native ARM64 execution remains unavailable. Phase 3 no-device UI and synthetic adapter tests passed; physical USB acceptance remains open. Phase 4 now has a Windows template-generated project, but SDK/WDK builds and driver runtime validation remain pending. See the [Windows report](testing/phase-2-5-windows-report.md) and earlier [macOS report](testing/phase-4-macos-report.md) for exact coverage.

## Validation results

| Check | Result | Evidence / limit |
| --- | --- | --- |
| Android APK build and lint | PASS, 2026-09-30 | Gradle 9.7.1, JDK 22, Android SDK 37; zero lint findings |
| Merged APK permissions | PASS, 2026-09-30 | Only AndroidX's package-scoped, signature-protected permission; no `INTERNET` |
| Android offline contract suite | PASS, 4/4 | `python3 -m unittest discover -s tests -p 'test_android_phase3_contract.py'` |
| Windows source-contract suite | PASS, 4/4 static checks | `python3 -m unittest discover -s tests -p 'test_windows_phase3_contract.py'`; this is not a Windows build/runtime test |
| Full offline contract suites | PASS, 8/8 | `python3 -m unittest discover -s tests -p 'test_*.py'` |
| Scaffold/security validator and whitespace | PASS | `python3 tools/validate_scaffold.py`; `git diff --check` |
| Isolated ADB lifecycle C# class compilation | PASS, 0 warnings/errors | .NET 10 compile only; does not compile the WinUI app |
| Phase 2 Windows host baseline | PASS x64, 2026-10-01 | Build/launch/normal close; ARM64 cross-build only |
| Phase 3 Windows host build and launch | PASS x64, 2026-10-01 | Missing-ADB and zero-device UI observed; synthetic adapter scenarios pass |
| Physical USB/ADB tests | NOT RUN | Neither Samsung Tab A11 nor Huawei MatePad 11 was tested |

No driver was installed and no boot/security setting was changed.

## Remaining Phase 3 work

Run the steps in [the Phase 3 Windows and tablet checklist](testing/phase-3-windows-checklist.md) one at a time after switching to Windows 11:

1. The x64 host baseline passed on 2026-10-01. Rerun `tools/verify-windows.ps1` after relevant host changes.
2. Exercise the host's no-device and unauthorized-device states.
3. Test an authorized ADB handshake with Samsung Tab A11, then Huawei MatePad 11, in separate sessions.
4. Check heartbeat, disconnect, reconnect, cable pull, and removal of only the session's ADB forward.
5. Record each result in the checklist. Keep device serials, tokens, ADB keys, and screen content out of the repository/logs.

On 2026-09-30 the owner authorized Phase 4 to start before these checks, and combined the outstanding Phase 3 and Phase 4 Windows validation into one later session. Follow the [combined checklist](testing/phase-3-4-windows-checklist.md). Phase 3 remains incomplete; Phase 4 has portable-tested logic and uncompiled Windows driver source drafts. See [ADR-004](decisions/ADR-004-phase-4-usb-owned-display.md) and the [WDK handoff](../src/windows/driver/WDK-HANDOFF.md).

## Read these documents before making changes

1. [`AGENTS.md`](../AGENTS.md) — local-only project rules and settled product decisions. It is ignored by Git; copy it to a fresh checkout.
2. [`plan.md`](../plan.md) — local-only complete roadmap, phase acceptance, and current detailed status. It is ignored by Git; copy it to a fresh checkout.
3. [`docs/project-status.md`](project-status.md) — this page, for the latest concise progress/test summary.
4. [`docs/development.md`](development.md) — pinned toolchain, build commands, and offline implementation facts.
5. [`docs/decisions/ADR-003-phase-3-android-identity-and-usb-handshake.md`](decisions/ADR-003-phase-3-android-identity-and-usb-handshake.md) — Phase 3 decisions and security boundary.
6. [`protocol/README.md`](../protocol/README.md) — message format, bounds, ADB endpoint, cleanup, and trust boundary.
7. [`docs/testing/phase-3-windows-checklist.md`](testing/phase-3-windows-checklist.md) — Windows/device test sequence and result table.
8. [`tests/README.md`](../tests/README.md) — test suites, commands, and limits.

The Windows ADB adapter and host UI changes are still uncommitted in the current working tree. Sync those files, `AGENTS.md`, and `plan.md` to the Windows checkout before running the checklist. Do not push; no push has been authorized.

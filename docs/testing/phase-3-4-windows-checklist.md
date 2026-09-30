# Combined Phase 3 and Phase 4 Windows validation

## Owner-approved scheduling change — 2026-09-30

The owner deferred the outstanding Phase 3 Windows/tablet checks into the Phase 4 Windows test session and authorized starting Phase 4 from the macOS checkout. This combines scheduling only: Phase 2, Phase 3 and Phase 4 retain separate acceptance results. No deferred check is a pass. Phase 4 now has macOS-tested portable logic and Windows source drafts; see [the report](phase-4-macos-report.md). The actual WDK project must be created and validated on Windows. No driver build, signing, installation or runtime result exists yet.

## Prerequisites and approval gates

- Transfer the complete working tree, including the uncommitted Windows host source and source-contract test, plus local-only `AGENTS.md` and `plan.md`. A Git bundle alone omits uncommitted files. The old Phase 2 bundle does not contain current Phase 3 work.
- Use Windows 11; record edition/build, x64 or ARM64 architecture, GPU/adapter, existing display layout and scaling. Results on one architecture do not validate the other.
- Before creating the driver project, record a compatible Visual Studio, SDK, WDK and IddCx/UMDF target from official documentation and the installed supported template. Do not invent project metadata on macOS. Record versions, source links, checked date and dependency/license implications locally.
- Before driver installation, obtain the owner's choice of an isolated Windows test PC/VM (recommended, with IddCx feasibility verified) or daily-use PC with an agreed recovery path. Record the exact proposed signing/install method and rollback. The owner's scheduling approval does not authorize installation or boot/security changes.
- Do not change Secure Boot, Memory Integrity/HVCI, test-signing boot configuration, or install a driver without explicit approval for that action. Keep signing keys and private certificates outside the repository.
- Owner-approved proof modes (2026-09-30): 1280×800 and 1920×1080, both at 60 Hz. Validate both in Windows; these describe the virtual display, not a tablet panel or video stream.

## Execution order

1. **Host baseline:** run section A of [the Phase 3 checklist](phase-3-windows-checklist.md), using the current host source. Record the deferred Phase 2 build/launch result separately from Phase 3 runtime behavior.
2. **USB/control proof:** leave the host virtual-display option off and run sections B–D of that checklist with one tablet at a time. Include no-device, unauthorized, handshake, heartbeat, stop, reconnect, cable pull and exact forward cleanup. No display driver is needed for these checks.
3. **Protocol rejection:** verify wrong magic/version/type/length, truncated or idle handshake, invalid token, unexpected control messages and missed heartbeat against synthetic inputs in a controlled test session. Record rejection, bounded timeout and ability to establish a subsequent valid session. Never store real tokens, device serials or ADB keys in results. Static source-contract checks are not runtime evidence.
4. **Driver project and build:** create the C++ UMDF/IddCx project from the supported Windows template. Build and run the applicable WDK static analysis for each claimed architecture. Record commands and results before any driver behavior commit. A build-only result is not an install/runtime pass.
5. **Approved install:** only after the explicit approval and recovery gates above, verify the signed package and install using the documented method. Verify a controlled failure can be recovered from without changing the existing GPU driver.
6. **Monitor proof:** Windows Display Settings must show exactly one additional monitor with the approved modes and extended desktop behavior. Confirm the existing desktop remains usable. Exercise supported swap-chain acquisition/release and any proof surface locally; no Android video pipeline belongs to Phase 4.
7. **Lifecycle:** explicitly stop/remove the proof monitor and verify its removal. Repeat start/stop and host reboot, inspect sanitized driver logs, and verify no orphan monitor or crash remains. Record the actual repetition count. The owner approved USB-owned monitor lifecycle in ADR-004. Enable the host virtual-display option for these tests. Verify cable loss, host termination, stalled heartbeats and file cleanup remove the monitor; verify another file cannot renew/stop it. Standalone removal does not prove this behavior.
8. **Regression and closure:** rerun the existing offline checks and affected platform builds, retain separate Phase 3 and Phase 4 results, and record remaining architecture/device coverage. Do not declare either phase complete while its required checks are missing or failed.

A Phase 3 failure blocks Phase 3 closure and any combined-session acceptance that depends on it; it need not prevent independent driver build/standalone monitor investigation. Desktop encoding/streaming remains Phase 5–6 work.

## Results

| Check | Result | Environment / evidence |
| --- | --- | --- |
| Phase 2 Windows host baseline | NOT RUN | Deferred |
| Phase 3 current host build/launch | NOT RUN | Deferred |
| Phase 3 no-device / unauthorized UI | NOT RUN | Deferred |
| Phase 3 Samsung Tab A11 handshake/lifecycle | NOT RUN | Deferred |
| Phase 3 Huawei MatePad 11 handshake/lifecycle | NOT RUN | Deferred |
| Phase 3 protocol rejection / idle / heartbeat timeout | NOT RUN | Synthetic runtime checks pending |
| Phase 3 exact ADB forward cleanup | NOT RUN | Deferred |
| Phase 4 Windows toolchain/template selection | NOT RUN | Windows environment unavailable |
| Phase 4 x64 WDK build/static analysis | NOT RUN | WDK project absent; Windows source draft present |
| Phase 4 ARM64 WDK build/static analysis | NOT RUN | WDK project absent; Windows source draft present |
| Phase 4 signed package/install/recovery | NOT RUN | Explicit approval required |
| Phase 4 one extended monitor / modes | NOT RUN | Approved: 1280×800 and 1920×1080 at 60 Hz; runtime unverified |
| Phase 4 stop/remove, repetition and reboot | NOT RUN | Windows test environment required |
| Phase 4 USB-triggered monitor cleanup | NOT RUN | Approved USB ownership; source drafted, runtime pending |

Record exact commands, date, sanitized diagnostics and failures next to each result. Do not fill rows from source inspection alone.

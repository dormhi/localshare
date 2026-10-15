# Phase 3 Windows and tablet checks

Scheduling update (owner request, 2026-09-30): run these outstanding checks in the [combined Phase 3/4 Windows session](phase-3-4-windows-checklist.md). Phase 3 remains incomplete; starting Phase 4 does not waive any result.

Run these checks on Windows 11 in order, marking each result separately. Use x64 or ARM64 to match the test machine. Do not treat a skipped or unavailable check as a pass. Phase 2's Windows host baseline is still pending and must be recorded first.

## A. Phase 2 Windows baseline and Phase 3 host build

**Status:** NOT RUN

From the repository root in PowerShell:

```powershell
.\tools\verify-windows.ps1 -Architecture x64
```

Use `-Architecture ARM64` on Windows on ARM. The script verifies Windows 11 and .NET 10, restores and builds the current WinUI host, launches it, and waits for the window to close. It does not install a driver, modify boot settings, or exercise ADB. Record Windows edition/build, architecture, .NET version, result, and any Windows App Runtime prerequisite. This records the deferred Phase 2 baseline and the Phase 3 host compile/launch check.

## B. Android APK build and merged permission review

**Status:** PASS on macOS 2026-09-25; rerun on the Windows checkout if its source differs.

```powershell
.\tools\verify-android.ps1
```

This builds the debug APK and checks that no permission beyond AndroidX's package-scoped signature permission was added. It does not install the APK.

## C. No-device and unauthorized-device UI

**Status:** PASS (Samsung SM-X130, 2026-10-15)

Leave the virtual-display option off for Phase 3-only tests. With Android Platform-Tools available on `PATH`, open the host app and click **Connect tablet** first with no ADB device, then with one USB-connected device whose host authorization has not been approved. Verify the UI reports a useful missing-device/authorization message, does not create a persistent forward, and remains usable. Record outcomes without saving the tablet serial or ADB key.

## D. Test one tablet per session

**Status: Samsung Tab A11 PASS (2026-10-15, after the fixes below); Huawei MatePad 11 NOT RUN.**

For each tablet separately:

1. Install the Phase 3 debug APK only for this explicitly requested device test, connect by USB, unlock the tablet, enable USB debugging, and approve the host RSA prompt on the tablet.
2. Confirm `adb devices -l` reports one `device` entry. Do not enable `adb tcpip` or Wi-Fi debugging.
3. Open the Windows host, click **Connect tablet**, and verify the host and tablet both show connected. Allow at least 10 seconds so several ping/pong heartbeats complete.
4. Run `adb forward --list`. Verify that this session has one mapping from `tcp:<port>` to `localabstract:com.dormhi.localshare.session`. Do not commit output containing a serial.
5. Click **Disconnect**. Verify both UI states return to disconnected and the matching mapping disappears. Do not use `--remove-all`.
6. Repeat Connect/Disconnect three times. Then test a cable pull during a session and verify the host reports a lost connection and removes the session's forward. Reconnect the cable, approve ADB again if prompted, and verify a fresh session works.

Record each device's model/OS and each result. Do not capture or log screen content, tokens, serials, or ADB private keys. No display driver, boot setting, or security configuration is changed in Phase 3.

## Results to record

| Check | Result (PASS/FAIL/NOT RUN) | Machine/device and build | Notes |
| --- | --- | --- | --- |
| A. Phase 2 baseline + Phase 3 host build | PASS x64 | Windows 11 Home 25H2 x64 (26200), .NET 10, Windows App SDK 2.5.1 | 0 warnings/errors; see the [Windows report](phase-2-5-windows-report.md) |
| B. Android APK and permission review | PASS | Same host, Gradle 9.7.1, SDK 37 | `assembleDebug`, `lintDebug` (0 errors; 2 "newer version available" notices), `videoProtocolTest`; installed package grants no runtime/user permissions |
| C. No-device and unauthorized UI | PASS | Samsung SM-X130 | Real unauthorized device: "authorize USB debugging" message, no forward created, Connect works after approval |
| D. Samsung Tab A11 | PASS | SM-X130, Android 16 / SDK 36, Platform-Tools 37.0.1 | Handshake, heartbeats, Disconnect, three Connect/Disconnect cycles with a single live forward |
| D. Huawei MatePad 11 | NOT RUN | | Separate session required |
| D. Reconnect, cable pull, and forwarding cleanup | PASS (Samsung) | SM-X130 | Cable pull: tablet "Disconnected", host clear message, forward and port removed; replug + fresh session; closing the host window leaves no process, forward, tablet socket or token file |

### Samsung session findings — 2026-10-15

The first physical run found defects the synthetic harness had masked because the fake `adb` accepted what real `adb` rejects. All were fixed and retested on the device before the PASS rows above:

1. Platform-Tools 37.0.1 rejects `adb forward tcp:localhost:0` (and `tcp:127.0.0.1:0`) with "Invalid source port". The host now uses `tcp:0` and verifies from the OS listener table that the port is bound to loopback only; an ADB server started with `-a` is refused before any token is sent.
2. `adb shell` joins arguments with spaces for the device shell, so `sh -c cat > files/session_token` redirected in the outer shell and failed. The command is now passed quoted.
3. Reconnecting after a session could reach the previous endpoint (old token) or a not-yet-listening endpoint; adb then closes the connection (end of stream). Tokens are now single-use on Android (the endpoint closes when its session ends) and the host retries the handshake on a fresh connection for up to 15 seconds.
4. `LocalServerSocket.close()` does not wake a thread blocked in `accept()`, so the video listener stayed bound after a session and the next session reported "Video endpoint unavailable". Android now wakes the blocked accept after closing.
5. Host disconnect messages showed raw socket errors (for example Windows error 10053); they now explain the disconnect and what to do.

Environment notes for users and later phases: SuperDisplay's `MirrorService` runs its own ADB server (version 40) and repeatedly replaced ours (version 41), dropping the device; the test stopped that service temporarily. A faulty cable produced "no device" with no ADB interface. Both need clear in-app guidance (Phase 8 compatibility work).

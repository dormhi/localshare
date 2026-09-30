# Windows integration handoff — Phase 4 source draft

The owner asked to finish macOS-feasible work before moving to Windows. `core/` has executable portable tests; `iddcx/` is **uncompiled Windows source**, not an installable or validated driver. No `.vcxproj`, INF, package, catalog or signing key is supplied. Generate the actual project from the supported WDK template on Windows, retaining its platform metadata. Do not use a fake header shim to claim these sources compile with WDK.

## Files and project integration

- `core/ProofModes.hpp`: only 1280×800 and 1920×1080, both progressive 60 Hz. Timings are virtual proof values, not tablet EDIDs. 1280×800 is preferred. Pixel-rate arithmetic and list bounds have native tests.
- `core/MonitorSession.hpp`: single-owner lifecycle, five-second renewal lease, failed-removal retention/retry, power-off and adapter-destruction transitions. The caller must serialize access.
- `core/ControlProtocol.hpp`: fixed 16-byte local control request; no frame data, pointers or allocation sizes from the caller.
- `iddcx/Driver.cpp`: UMDF/IddCx entry/callback draft, one adapter/monitor, per-file owner IDs, watchdog, and monitor arrival/departure.
- `iddcx/DisplayCallbacks.cpp`: no-EDID default modes and target-mode callbacks. Monitor `vSyncFreqDivider` is 0; target divider is 1.
- `iddcx/SwapChain.cpp`: detected render-adapter LUID, D3D11/DXGI resource setup, one worker, acquire/finish/release loop. No pixel mapping, encoding, file output or networking. Protected surfaces are discarded.

Use C++17 or newer, supported UMDF/IddCx headers, and the template's entry/link settings; add these three `.cpp` files and link the required Windows SDK D3D11/DXGI and IddCx stub libraries. Match the installed WDK/SDK/Visual Studio versions rather than silently choosing an unverified target. Build x64 and ARM64 separately. No new redistributed library or third-party source was introduced by the draft; WDK/SDK/toolchain version/license inventory must be recorded when selected.

The host's `DriverControl.cs` uses the project-owned device interface GUID `7e0a104a-6999-4f54-9262-58a25b3d878b`. Do not reuse Microsoft's sample interface or container identities. Register only the interface in `Driver.cpp`; preserve stable connector 0 and one monitor maximum. Do not advertise FP16, HDR, protected-output/OPM, audio or touch capabilities.

## Access policy and installation gate

Before installing, review the generated INF/device security descriptor. The proof control device must be limited to SYSTEM, administrators and the explicitly selected local test user. Do not grant Everyone, Anonymous or Network access, and do not silently grant every interactive user. Keep a machine-specific user SID out of source control. The exact machine/ACL/signing configuration is a Windows-session decision, not an authorization implied by this document.

All control IOCTLs require read **and** write access and use METHOD_BUFFERED. The driver assigns an owner ID when a file opens; callers cannot supply another owner's ID. Another open file cannot renew or stop the active monitor. ACL enforcement has **NOT BEEN TESTED**: the source cannot establish the safety of an INF that does not yet exist. No driver may be installed before this policy, signature and recovery path are reviewed and explicitly approved.

Do not add driver install/uninstall or boot-configuration commands to a script until the actual test machine, package/device identities, signing route and rollback have been approved. Generic `pnputil /add-driver` alone is not evidence of creating a root/software device instance. Record the supported device-enumeration method from the chosen template and provide exact install/remove commands for that instance only; never remove another display adapter or driver package.

## Required Windows source review and tests

1. Compile against real headers, fix API/ABI or template integration errors and run applicable WDK static analysis. The source has not passed this gate. Verify asynchronous adapter initialization/context publication and the adapter lifetime across D0 transitions against the selected WDK.
2. Verify the default-mode callback is used for the empty EDID description and that both modes appear in Windows. Check the virtual timings are accepted; any necessary timing fix must retain the approved resolutions/rate.
3. Verify file-create/cleanup callbacks coexist with IddCx device-control routing. Confirm denied users cannot open/send controls and that a second file cannot affect an existing lease. Test malformed/unknown/short/long requests and repeated Start without monitor duplication.
4. Verify one-shot watchdog scheduling, D0 exit, initialization failure, and parent/child cleanup ordering. Confirm no timer/worker can outlive its state or deadlock with monitor departure. Test host termination as well as orderly close.
5. Inject D3D creation/acquisition failure and resource-allocation failure. The draft retires the monitor through a fault flag instead of repeatedly returning ABANDON. Confirm ownership transfer/deletion in the assignment failure path with the selected IddCx version; never accept a silent retry loop or a driver crash as recovery.
6. Check frame format/dimensions, mode changes, GPU loss, stop while idle/processing, and reassignment. The worker joins on shutdown instead of detaching live references. Windows GPU/framework calls can stall: a passed portable test does not establish bounded Windows teardown.
7. Verify overlapped `DeviceIoControl` completion and cancellation on x64/ARM64. The host retains unmanaged request buffers until native completion. A three-second timeout requests cancellation but cannot promise to terminate a broken driver call; test cancellation/drain under device removal and fault injection.
8. Run USB-only checks with the host's virtual-display option **off**, then enable it after installing the approved test driver. A missing driver is an explicit error, never an automatic installation. After a valid USB handshake, Start creates the monitor; only valid tablet pongs renew it. USB loss stops renewal; the watchdog removes it after lease expiry (five seconds plus scheduling delay, normally within one additional timer interval). Explicit Disconnect/Close also sends Stop and closes the file. A failed Stop still closes the file.
9. Select **Extend** for the new monitor in Windows Display Settings; the draft does not rewrite the user's display topology. Verify existing display layout/scaling and both proof modes. No image appears on the tablet in Phase 4.

Use the [combined acceptance checklist](../../../docs/testing/phase-3-4-windows-checklist.md). Phase 2/3 checks remain separate pending results. These sources must stay uncommitted until their required Windows checks pass; portable tests do not satisfy the driver behavior commit gate.

## Recovery record to complete on Windows

Record: selected test machine and owner approval; backup/recovery access; exact Windows/toolchain versions; intended package and device instance; security descriptor; signing certificate public identity and external key location; approved install/remove commands; pre-install display topology; failed-install rollback; reboot/start/stop results. Never record private keys, tablet serials or session tokens.

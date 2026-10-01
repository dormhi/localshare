# Windows validation checkpoint — 2026-10-01

This report supersedes the earlier Windows deferral only for the checks listed as PASS below. Source baseline: `fc76b0b` plus the uncommitted Phase 3–5 working tree. No phase completion is claimed. No LocalShare driver was installed, signed or submitted; no boot or security setting was changed.

## Environment and tools

Windows 11 Home 25H2 x64, build 26200; .NET SDK 10.0.203; Windows App SDK 2.5.1. Visual Studio Community 2026 18.10.3 and MSVC 14.51.36231 were installed with x64/ARM64 tools and AddressSanitizer. Platform-Tools 37.0.1 is a local development tool under ignored `.cache/android-sdk/platform-tools`; it is not bundled for distribution. No physical tablet was detected.

The owner selected signed Microsoft installers: SDK bootstrapper 10.1.28000.2957 and WDK bootstrapper 10.1.28000.2526. Both Authenticode signatures were Valid, Microsoft Corporation. Automated elevation returned cancellation without a visible UAC prompt; the owner will launch the installers manually. SDK/WDK installation and native build validation are still pending at this checkpoint. An inspected WDK NuGet package was not used for compilation because its pre-release license requires further clarification.

Version facts checked 2026-10-01: the supported WDK/SDK must share the build number; QFE versions may differ. See [Microsoft WDK setup](https://learn.microsoft.com/en-us/windows-hardware/drivers/download-the-wdk), [SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-sdk/downloads), and [Platform-Tools release notes](https://developer.android.com/tools/releases/platform-tools).

## Passed checks

Commands below ran from the repository root. `python` denotes the bundled Python 3 runtime, invoked with `-X utf8`. NuGet packages were restored to ignored `.cache/nuget`.

| Command / observation | Result and boundary |
| --- | --- |
| `tools/verify-windows.ps1 -Architecture x64` | PASS: restore/build, 0 warnings/errors, visible WinUI window and normal close with exit 0 |
| `dotnet restore src/windows/TabletDisplay.Host/TabletDisplay.Host.csproj -p:Platform=ARM64` followed by `dotnet build ... -c Debug -p:Platform=ARM64 --no-restore` | PASS: cross-build, 0 warnings/errors; no native ARM64 runtime test |
| Host with ADB unavailable | PASS: useful installation message, controls recover |
| Host with real ADB and zero devices | PASS: no-tablet message, connection controls recover, proof features remain disabled |
| `dotnet run --project tests/HostAdbSession/HostAdbSession.csproj` | PASS: actual host adapter with a synthetic child-process ADB and loopback tablet; no-device, unauthorized, multiple/non-USB selection, provisioning failure, fragmented/invalid/truncated hello, heartbeat failure, simulated cable loss and exact owned-forward cleanup |
| `dotnet run --project tests/HostDriverControl/HostDriverControl.csproj` | PASS: fake-driver control/lease, cancellation and concurrent disposal |
| Same command with `-- --probe-missing-driver` | PASS: real Windows SetupAPI enumeration reports the project driver absent; no real IOCTL exercised |
| `dotnet run --project tests/VideoProtocol/VideoProtocol.csproj` | PASS: real C# framing with synthetic golden, fragmented/truncated/oversized input, metadata/FPS and cancellation cases |
| `python -X utf8 -m unittest discover -s tests -p 'test_*.py'` | PASS: 8 source-contract checks; static coverage only |
| `python -X utf8 tools/validate_scaffold.py`; `git diff --check` | PASS: configured source secret/permission/structure checks and whitespace |
| `dotnet list src/windows/TabletDisplay.Host/TabletDisplay.Host.csproj package --vulnerable --include-transitive` | PASS: NuGet reported no vulnerable packages; not a complete security audit |

## Fixes and preparation

Architecture-specific restore fixes missing ARM64 apphost assets. Imported AppleDouble metadata had broken source compilation; verified worktree sidecars were quarantined locally and future metadata is ignored/excluded. Generated dependency/build trees are excluded from the source secret scan, while real project source remains scanned. A single verified fake Git pack index was quarantined; remaining imported Git metadata has not passed full repository integrity validation.

ADB selection now verifies that the selected authorized device is the unique USB device using `adb -d get-serialno`. Raw ADB error output is no longer displayed because it can contain device identifiers or private paths. The new synthetic ADB harness tests the actual process and socket code without a real tablet.

A UMDF project was generated from the installed WDK Visual Studio UMDF2Empty template and adapted to the existing source drafts. `tools/build-driver.ps1` builds unsigned x64/ARM64 packages, optionally with code analysis; it does not install them. The INF's initial security policy grants only SYSTEM/Administrators and remains build-only pending review and runtime tests. The video helper builds into architecture-specific output, preventing an ARM64 build from overwriting the x64 helper. Neither native build script has passed its required SDK/WDK build yet.

## Still required

- SDK/WDK installation; x64/ARM64 driver build, analysis and INF validation; native portable tests with MSVC AddressSanitizer; Windows Media Foundation encoder build and generated output.
- Explicit driver test-target/recovery and exact signing/install approval, followed by monitor modes, extension, lifecycle, reboot and removal tests. Driver behavior changes remain uncommitted until their Windows gate passes.
- Authorized USB handshake, reconnect/cable pull, rendered pattern, decoder capabilities, malformed-stream rejection, backpressure/memory and surface lifecycle on Samsung Tab A11 and Huawei MatePad 11 separately.
- Native Windows ARM64 execution on actual hardware. Cross-compilation does not establish ARM64 compatibility.

Follow the [combined checklist](phase-2-5-windows-checklist.md); preserve separate physical-device results. Real desktop capture and driver-to-video integration remain Phase 6.

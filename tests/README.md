# Test strategy

Organize tests by component and level (unit, protocol, host integration, Android/device, security). Run `python3 -m unittest discover -s tests -p 'test_*.py'` for both platform-independent source-contract suites. `test_android_phase3_contract.py` covers the Android endpoint, identity, and permission boundary; `test_windows_phase3_contract.py` checks host source invariants. The Windows test file is currently uncommitted alongside its host source; sync both from the maintainer's working tree before running it in another checkout. These offline checks do not replace Android builds, Windows builds/runtime, or device tests. Windows runtime and tablet checks are tracked separately in [`docs/testing/phase-3-windows-checklist.md`](../docs/testing/phase-3-windows-checklist.md). The owner combined the pending Windows/device validation with Phase 4; use the [combined checklist](../docs/testing/phase-3-4-windows-checklist.md) while keeping each phase's acceptance separate. Each test documents its prerequisites and whether it can run offline. Follow root `AGENTS.md`: relevant tests and fast static security checks before each commit; broader regression/security checks at phase completion. Never label unavailable Windows/device checks as passed.


## Phase 4 portable executable tests

Run `python3 tools/test-driver-core.py --sanitize` with Apple Clang (or a compatible Clang C++17 toolchain) and `dotnet run --project tests/HostDriverControl/HostDriverControl.csproj` with .NET 10. Native executables are created in a temporary directory and removed. The .NET project clears NuGet package sources and uses only SDK libraries; its `bin/` and `obj/` output is ignored. These tests use fake monitor/driver backends and no device/network connections.

They test mode enumeration, control bounds and ownership/expiry, plus host lease failures/cancellation/concurrency. They also compile the C# native transport declarations and ADB class, but never invoke Windows P/Invoke or ADB. See [the macOS report](../docs/testing/phase-4-macos-report.md) for results and limitations. The new driver/core/host test files remain uncommitted until required Windows validation.

## Phase 5 executable protocol tests

Run `./src/android/gradlew --offline --no-daemon -p src/android :app:videoProtocolTest` with the documented Java/Android SDK environment, and `dotnet run --project tests/VideoProtocol/VideoProtocol.csproj`. Both use real protocol implementations with synthetic inputs and no physical device/network service. The native test runner additionally checks the NV12 generated pattern. Android `:app:assembleDebug :app:lintDebug` and merged-APK permission review remain required before Android commits. See [the video protocol](../protocol/VIDEO.md) for exact bounds; Windows encoder/rendering tests remain pending.

## Windows executable adapter tests

Run `dotnet run --project tests/HostAdbSession/HostAdbSession.csproj`. This invokes a synthetic child-process ADB and a local loopback mock tablet through the actual adapter implementation. It covers device selection, provisioning failures, fragmented/malformed hello, heartbeat failure, cable loss and exact owned-forward cleanup; it never contacts a real ADB daemon or tablet. It does not prove USB behavior.

On Windows 11, `dotnet run --project tests/HostDriverControl/HostDriverControl.csproj -- --probe-missing-driver` performs real SetupAPI enumeration and expects the project driver to be absent. It must be run before installing the driver and exercises no IOCTL. For native portable tests in a Visual Studio developer shell, use `python tools/test-driver-core.py --compiler cl.exe --sanitize`; MSVC enables AddressSanitizer only, with no UndefinedBehaviorSanitizer. See the [Windows checkpoint](../docs/testing/phase-2-5-windows-report.md).

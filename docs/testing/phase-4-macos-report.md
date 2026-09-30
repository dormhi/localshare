# Phase 4 macOS implementation report

Date: 2026-09-30. Phase 4 remains **IN PROGRESS**, with Windows validation unavailable. The owner's clarified scope is all macOS-feasible work now, followed by Windows build/install/runtime checks later. The accepted lifecycle decision is [ADR-004](../decisions/ADR-004-phase-4-usb-owned-display.md).

## Changes

- Added portable C++ mode enumeration and exact rational refresh matching for the two approved modes. Tests exercise the same enumeration helper used by the WDK callback draft.
- Added a single-owner monitor lifecycle with a five-second lease, explicit stop, failed departure retry and adapter/power transitions. Added strict 16-byte local control decoding.
- Added Windows-only UMDF/IddCx source drafts for adapter/monitor lifecycle, mode callbacks and D3D swap-chain consumption. These files have not been compiled against WDK and are not an installable driver.
- Added the C# device-interface transport and display lease. The host requests a monitor only when its virtual-display option is enabled and USB handshake succeeds. Tablet pongs drive renewal; disconnect, failure and close dispose the lease. USB-only testing remains available with the option off.
- Made ADB session disposal idempotent because disconnect/watch/close can converge. Added a heartbeat observer after valid pong validation. Existing uncommitted Phase 3 host work was retained and extended, not discarded or committed as validated Windows behavior.
- Added an offline .NET console test project with package sources cleared. No new runtime dependency, downloaded test framework, service, driver package, signing material or permission was added.

## Checks and limits

| Command / check | Result | Scope |
| --- | --- | --- |
| `python3 tools/test-driver-core.py --sanitize` | PASS | Apple Clang 16, C++17, warnings as errors; AddressSanitizer + UndefinedBehaviorSanitizer; two native executables |
| `dotnet run --project tests/HostDriverControl/HostDriverControl.csproj` | PASS | .NET 10; compiles actual control/lease/ADB source; fake driver only, no native Windows calls |
| `dotnet run --no-restore --project tests/HostDriverControl/HostDriverControl.csproj` | PASS | Cached SDK-only rerun; no external service or device |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -p 'test_*.py'` | PASS | Existing eight static Android/Windows contract checks |
| `python3 tools/validate_scaffold.py` | PASS | Existing structural, permission baseline and secret-pattern checks; not a full native vulnerability analysis |
| `git diff --check` | PASS | Whitespace |
| Windows WinUI / WDK compile and WDK static analysis | UNAVAILABLE | macOS has no Windows runtime/WDK test machine |
| Windows SetupAPI/overlapped IOCTL/ACL/runtime | NOT RUN | C# compilation does not test Windows ABI behavior |
| Driver install/remove, reboot, GPU/display matrix | NOT RUN | Requires an approved Windows test environment |
| Samsung / Huawei physical USB checks | NOT RUN | Deferred Phase 3 checks remain pending |

Native coverage includes zero/short/null mode queries, no partial writes, unsupported resolutions and rational refresh values; invalid/short/oversized commands and all command-byte values; owner mismatch, duplicate start, expiry boundary, stale renewal, clock reversal/wrap, arrival/removal failure, power transitions, 1,000 reconnect cycles and 200,000 deterministic randomized state transitions. This is a fake monitor backend, not 200,000 Windows/device tests.

The .NET harness checks fixed protocol bytes, Start/Renew/Stop ordering, failure and cancellation cleanup, repeated/concurrent disposal and disposal while renewal is in flight. No real ADB process, network peer or tablet is used by these new tests. WinUI event handling and the actual ADB heartbeat callback remain Windows/integration checks.

## Security review and Windows gates

Portable command handling accepts only fixed sizes and known operations; it cannot allocate caller-selected frames or expose arbitrary memory. Ownership is issued by the driver, not supplied in request bytes. Failed monitor departure retains ownership and rejects another arrival. The proof does not map/log/store/encode desktop pixels, introduce a network listener or claim DRM/protected-output support.

The future Windows INF must restrict access to SYSTEM, administrators and the selected local test user. ACL enforcement is not present as a tested package; installation is blocked until its Windows review and owner approval. Review WDF callback lifetime, cleanup ordering and concurrent device removal with real tooling. Overlapped I/O retains buffers until cancellation completion, and frame workers join rather than detaching live WDF references; actual cancellation/GPU stalls require Windows fault tests. These are outstanding validation gates, not passed security checks or silently accepted findings.

No confirmed Critical/High/Medium/Low finding was produced by the available checks; this is not a claim of a completed security audit. Any findings from Windows/static analysis must follow the project's severity gates.

## Delivery state

Windows behavior source changes, their portable core and new test harnesses remain uncommitted pending the required Windows checks. Only documentation may be committed from this session. No push or release is authorized. The ignored `AGENTS.md` and `plan.md` record the owner's updated decisions and must accompany the working tree. See the [WDK handoff](../../src/windows/driver/WDK-HANDOFF.md) for project creation, access policy, review points and recovery details.

# Phase 5 macOS implementation and validation

Date: 2026-09-30. **Phase 5 remains IN PROGRESS**: macOS-feasible implementation is prepared, Windows/device runtime checks are deferred by the owner and combined with outstanding Phases 2–4 checks. No successful physical video stream is claimed.

## Delivered scope

- Bounded authenticated secondary ADB video channel, with the existing control channel retained. Versioned TVID framing, pre-allocation length checks, per-packet deadlines, sequence/timestamp checks, one compressed frame in flight and explicit rejection behavior.
- Android MediaCodec/SurfaceView receiver with codec capability checks, bounded progressive Baseline SPS/reference/dimension validation, format-change rejection, aspect fitting, crop validation and lifecycle cleanup. No GMS or new manifest permissions.
- Windows source draft: Media Foundation inbox H.264 helper generates only NV12 test patterns; C# host supervises the helper and stream. No real desktop capture or IddCx frame connection. Windows-only helper and WinUI behavior are not compiled/run on this Mac.
- User-selected stream FPS limit: 30 default, lower limits for low-motion use, and 120/144 or other higher requests if actual capabilities permit. Panel refresh is separate/read-only. Parser ceiling 1000 bounds input; it is not a support claim. Detailed quality/power presets stay in Phase 7.
- SDK-only Kotlin/C# protocol tests, portable C++ pattern tests and a Windows helper build script. No new third-party codec/test dependency or distributed SDK binary.

## Exact checks and outcomes

Android environment: JDK 22.0.1, existing Android SDK API 37 / Build Tools 36.0.0, pinned Gradle 9.7.1 / AGP 9.4.1 / Kotlin 2.4.20. Set `JAVA_HOME` and `ANDROID_HOME` to those installations before the commands below. Gradle used its existing cache with `--offline`; no device was contacted. The normal sandbox first denied the existing Gradle cache lock; the authorized escalated build succeeded without changing device/boot/security settings.

| Command | Outcome / scope |
| --- | --- |
| `./src/android/gradlew --offline --no-daemon -p src/android :app:assembleDebug :app:lintDebug :app:videoProtocolTest` | PASS; APK built, zero lint findings, executable Kotlin protocol suite passed |
| `./src/android/gradlew --offline --no-daemon -p src/android :app:videoProtocolTest` | Protocol commit gate; real Kotlin parser with synthetic input |
| `dotnet run --project tests/VideoProtocol/VideoProtocol.csproj` | PASS; C# wire golden bytes, fragmentation/truncation, allocation bounds, metadata rejection and cancellation |
| `dotnet run --no-restore --project tests/HostDriverControl/HostDriverControl.csproj` | PASS; compiles current C# ADB/video/control sources and exercises the fake driver lease tests; not Windows P/Invoke or WinUI runtime |
| `python3 tools/test-driver-core.py --sanitize` | PASS; three native executables, Clang 16/C++17 with warnings as errors and ASan/UBSan; includes generated NV12 pattern guards plus prior driver logic |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -p 'test_*.py'` | PASS, 8/8 existing source-contract tests |
| `python3 tools/validate_scaffold.py` | PASS, structural/XML/version/permission baseline and configured secret-pattern scan |
| `git diff --check` | PASS |
| `aapt dump permissions src/android/app/build/outputs/apk/debug/app-debug.apk` plus `aapt dump xmltree ... AndroidManifest.xml` | PASS; only AndroidX's package-scoped self-permission with signature protection (`0x2`); no INTERNET permission |

Kotlin tests cover matching golden header bytes, fragmented/truncated/oversized input, full-packet deadline, FPS values 1/15/30/60/120/144/240, malformed and oversized SPS dimensions, crop bounds, IDR validation, rate limiting and 10,000 deterministic malformed headers. C++ tests verify NV12 dimensions/plane filling, guard bytes, deterministic moving pattern and low/high FPS input acceptance. These do not prove that an OS encoder/decoder accepts or sustains any requested rate.

An initial Android compile found nullable video-capability access; it was corrected and the full build/lint/test command passed afterward. No test suppression or permission expansion was used. Debug APK SHA-256: `f292e68233e056ac2fab0ea12ace4192ddf085791b8be4199c8e884f315f3091` (source as tested before any later Android edit; re-hash if rebuilt with changes).

## Pending platform gates and security limits

Windows helper compilation/linking and native Media Foundation encoding: **UNAVAILABLE on this host**. WinUI build/run, the helper's output Annex-B/config/IDR behavior, high-FPS throughput, x64/ARM64 compatibility and cancellation/child-process cleanup: **NOT RUN**. Android native codec rendering/capability accuracy, vendor codec hangs, surface orientation/lifecycle, sustained memory/power behavior and Samsung/Huawei USB tests: **NOT RUN**.

Application-buffer limits exclude native codec pools, OS transport buffers and managed heap retention; measure process memory on actual devices. An ACK counts submission to the decoder, not display presentation. The first Windows helper uses the inbox encoder and does not claim vendor hardware acceleration. A 120/144-Hz panel alone establishes neither encoder nor decode throughput. Phase 4's 60-Hz proof monitor has not been expanded by the independent pattern test.

Source review found no intentional pixel/token/serial logging or unbounded application frame queue. The available validator is not a native codec vulnerability audit; no complete dependency/WDK/security audit is claimed. OS codecs still parse compressed data from the authorized host, and native cancellation/vendor failure behavior needs platform fault tests. New findings must follow the project's severity/owner-decision gates.

## Commit/delivery policy

Portable protocol and Android units may be committed after their available executable/Android build, lint, permission and static gates; Windows/device acceptance remains deferred. Windows host/driver/encoder behavior drafts and helper build script remain uncommitted until their relevant Windows gates. Keep local-only AGENTS.md/plan.md and the complete working tree with any Windows handoff. No push, publication, installation or future phase was authorized.

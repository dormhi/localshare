# ADR-005: Bounded OS-codec video proof and selectable FPS limit

- Status: accepted direction; Windows/device validation pending
- Date: 2026-09-30
- Scope: Phase 5, generated-pattern transport/decoder only

## Owner intent and decision

The owner requested Phase 5 while retaining macOS development and combining unavailable platform tests with Phases 2–4. Use the recommended Windows/Android OS H.264 codec route and separately authenticated ADB video channel. The owner's FPS clarification supersedes a fixed 30-FPS proof interpretation: 30 is a default, never the product maximum. Higher-capability devices may request 120/144 FPS; low-motion/control-panel uses may choose lower limits to reduce work/power. Do not conflate stream FPS with physical refresh. Full quality/power presets remain Phase 7; no Stream Deck integration or touch feature is added.

The initial pattern is 1280×800 with a user-selectable integer FPS limit. Negotiated decoder capacity, host encoder acceptance and observed throughput determine feasibility. OS codec availability does not prove sustained performance. Failed/unsupported requests give an error rather than silently forcing 30 or 60 FPS. UI controls are proof controls, not a claim of final device support.

## Implementation and scope

A native Windows user-mode helper uses Media Foundation's inbox H.264 encoder with NV12 test-pattern input, Baseline profile and low-latency mode. It does not use the display driver, capture a desktop or ship an encoder library. This first adapter uses the inbox encoder; selecting vendor asynchronous hardware MFTs is not implemented or claimed. Keep the requested FPS variable; hardware acceleration/performance can be evaluated with the Windows evidence rather than promised from a tablet's panel specification.

Android detects AVC decoder capabilities, configures MediaCodec onto a SurfaceView, validates protocol/SPS dimensions, and preserves aspect ratio within density-independent Compose bounds. An actor owns codec operations and only one pending compressed input is allowed. The secondary channel preserves responsive control heartbeats and authenticates to the same single-tablet session.

See [protocol/VIDEO.md](../../protocol/VIDEO.md) for the exact header, memory/work limits and rejection behavior. Maximum compressed frame is 1 MiB and the application never queues more than two frames. A 1000-FPS parser ceiling bounds requests but is not an advertised hardware capability. The selected cap, observed accepted-frame rate and reported panel Hz remain separate values.

Phase 4 retains its approved 60-Hz virtual proof modes. A high-FPS generated pattern is independent of that driver and does not prove a 120/144-FPS extended desktop. Later real-desktop operation must negotiate the virtual mode and the entire encoder/transport/decoder/display path; no stage may claim frames the source never produced.

## Dependencies and licensing record

No third-party runtime/test library was added. The Android implementation uses the already pinned Android API 37/MediaCodec and Kotlin/Compose stack. The JVM test task uses the existing Kotlin/AGP tooling without JUnit or another test framework. The C# tests use .NET 10 SDK libraries with NuGet package sources cleared.

The Windows helper uses Windows SDK Media Foundation/COM and the standard C++ library through the already planned Visual Studio toolchain; no WDK is needed for this helper. Exact Windows SDK/MSVC versions and x64/ARM64 build results must be recorded when a Windows environment is available. Tools/frameworks remain under their existing vendor terms; no SDK or codec binary is copied into the repository. Using an OS H.264 codec is not a legal conclusion about redistribution/patents; public distribution review remains Phase 9–10. Offline dependency caches suffice for the local Android checks; no new lockfile or vendored codec is introduced.

## Primary facts checked 2026-09-30

- [Microsoft H.264 encoder](https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder): accepts NV12, requires output type before input type, supports Baseline and recommends letting the encoder derive AVC level. Baseline avoids B-frame reordering. We use these facts for the source draft; runtime remains unverified.
- [Codec low-latency mode](https://learn.microsoft.com/en-us/windows/win32/medfound/codecapi-avlowlatencymode): codec latency/quality tradeoffs exist; requesting low latency does not prove real-world latency or throughput.
- [Sequence header](https://learn.microsoft.com/en-us/windows/win32/medfound/mf-mt-mpeg-sequence-header-attribute): H.264 codec configuration contains concatenated Annex-B NAL units. Validate SPS/PPS before configuring the Android decoder.
- [Android MediaCodec](https://developer.android.com/reference/android/media/MediaCodec): decoder setup can use a Surface; asynchronous callbacks deliver input/output availability. Format support is device-specific, so the implementation checks it and surfaces configuration failure.
- [Video capabilities](https://developer.android.com/reference/android/media/MediaCodecInfo.VideoCapabilities) and [Display.Mode](https://developer.android.com/reference/android/view/Display.Mode): decoding size/rate support and physical display refresh are separate capability sources. Advertised ranges are not measured sustained FPS.

## Validation

Android build/lint and executable Kotlin/C#/portable C++ tests can run on macOS. Windows helper/host builds, native encoder output, actual decoder rendering, throughput/memory/power and USB cleanup tests remain in the [combined checklist](../testing/phase-2-5-windows-checklist.md). Deferred results are not passes. This phase does not connect driver desktop frames to the encoder; that remains Phase 6.

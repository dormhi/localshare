# Phase 5 generated-video proof protocol

Status: implementation draft, 2026-09-30. Windows encoder, actual MediaCodec rendering and USB behavior remain unverified. This sends an internally generated pattern only, not IddCx/desktop pixels. [ADR-005](../docs/decisions/ADR-005-bounded-video-and-fps.md) records scope and FPS decisions.

## Channels and ownership

Keep the Phase 3 control endpoint/protocol intact. After a valid control handshake, Android starts `localabstract:com.dormhi.localshare.video`. The host creates a second `adb forward tcp:localhost:0` mapping and connects to `127.0.0.1`; video authenticates with the same session's random 32-byte token, never placed in arguments/logs. Stop control, control timeout, activity destruction or surface loss closes the video endpoint. Host video failure stops the combined session. Remove each exact forwarding port, never all ADB forwards. There is still one tablet session, with two channels rather than simultaneous tablet support.

This remains a debug APK experiment using the existing `run-as` bootstrap. No `INTERNET` permission, Wi-Fi debugging, new service or Google Play dependency. The same authorized-host/root/app-UID trust limitations as the control protocol apply. An unauthenticated local client gets a bounded handshake and cannot configure a decoder.

## Header and messages

Header is 24 bytes; integers are unsigned big-endian. Reject wrong magic/version, unknown types/flags, invalid lengths and timestamps before allocating payload storage.

| Bytes | Field |
| --- | --- |
| 0–3 | ASCII `TVID` |
| 4 | Video protocol version 1 (distinct from `TDSH`) |
| 5 | Type |
| 6–7 | Flags: bit 0 marks an IDR frame; all other bits zero |
| 8–11 | Payload byte count |
| 12–15 | Sequence number |
| 16–23 | Presentation time in microseconds; frame only, otherwise zero; ≤ signed 64-bit maximum |

| Type | Direction | Payload / limit |
| --- | --- | --- |
| 1 HELLO | Host → tablet | Exactly 32 token bytes, sequence 0 |
| 2 CAPS | Tablet → host | Five u32s: pattern width, height, advertised decoder maximum FPS, current panel millihertz, maximum panel millihertz; sequence 0 |
| 3 FORMAT | Host → tablet | Four u32s: width, height, FPS limit, bitrate; sequence 1 |
| 4 CONFIG | Host → tablet | Annex-B SPS then PPS, 1–65,536 bytes; sequence 2 |
| 5 FRAME | Host → tablet | One Annex-B AVC access unit, 1–1,048,576 bytes; sequence starts at 3 |
| 6 ACK | Tablet → host | No payload; echoes the accepted FORMAT/CONFIG/FRAME sequence |
| 7 STOP | Host → tablet | No payload; next sequence; closing the channel also stops video |
| 8 ERROR | Tablet → host | One byte, currently 1: generic format/data/decoder failure; no private/raw codec diagnostics |

The first frame must contain an IDR. Frame timestamps strictly increase; sequence numbers cannot wrap. Reserved fields are rejected. The host waits for each ACK before forwarding another compressed packet: one frame in flight, below the approved two-frame application-buffer ceiling. ACK means queued to MediaCodec, **not rendered or measured panel refresh**. Native codec pools, raw input, OS pipe/socket buffers and managed heap retention are separate from this application queue bound and require device memory measurements.

## Format, limits and FPS

The initial pattern is 1280×800 H.264 Baseline, no B-frame reordering. This is a test-image resolution, not a hardcoded tablet panel size. 30 FPS and about 3 Mbit/s are defaults only. User limits can be lower or higher, including 120/144 FPS when the path supports them. Bitrate scales with the requested FPS in this proof (100 kbit/s–50 Mbit/s); detailed quality presets remain Phase 7.

Protocol work ceiling is 1–1000 FPS to bound inputs, **not a support claim**. Android reports decoder capability for the exact proof dimensions and rechecks the chosen rate/profile. Windows Media Foundation must accept the format and selects an AVC level; Android verifies profile/level during configuration. Unsupported requests fail visibly; there is no silent 30/60 FPS clamp. Actual throughput can be lower than the selected maximum and is reported separately. The helper prefers a hardware encoder (see "Encoder selection"), but no hardware acceleration, 120/144 FPS performance or tablet compatibility is claimed before physical validation. Panel Hz is read-only information; no panel refresh API is changed.

An entire received video packet has a five-second deadline, including partial transfers. Codec input submission has a two-second deadline. Android checks per-packet sizes, strict state/order, progressive Baseline SPS dimensions/reference bounds, unchanged parameter sets and decoded crop dimensions. Unknown frame NAL types or renegotiation fail the session. Decoder output is aspect-fitted to available Compose layout bounds and adapts to tablet orientation/density. Surface destruction requires explicit reconnect; automatic resume is later lifecycle work.

A token-bucket policy allows at most two frame arrivals in a burst, refills at the negotiated FPS, and bounds compressed bytes to bitrate plus a bounded keyframe allowance. The sender applies backpressure and the native generator does not catch up in bursts after stalls. On a stalled/malformed/unsupported stream, stop and show an error; do not drop arbitrary interdependent H.264 frames and continue corrupt decoding.

## Memory and helper boundary

The Windows helper generates NV12 directly into a fixed-size Media Foundation input buffer. It permits at most a 4 MiB codec output scratch buffer and rejects compressed output above the 1 MiB wire limit. It writes only protocol bytes to a redirected stdout pipe; no video files or desktop capture. Its arguments contain FPS/bitrate and an optional encoder mode (`auto` default, `hardware`, `software`). After CONFIG and each FRAME, it waits for a single `A` byte on its private stdin pipe; the host grants that credit only after the matching tablet ACK. This prevents the OS pipe from accumulating a burst of pre-encoded frames while the tablet configures or stalls. The host kills its own helper on cancellation/error and retains only one compressed packet while awaiting ACK. Android accepts only one decoder actor; if a vendor codec hangs during close, the slot remains occupied rather than spawning unlimited codecs. Native termination and vendor codec stalls remain Windows/device fault-test gates.

### Encoder selection (2026-10-15)

Low host impact is a product requirement, so `auto` tries hardware H.264 encoders first (Media Foundation `MFTEnumEx` with `MFT_ENUM_FLAG_HARDWARE`, sorted by the OS; typically NVIDIA, Intel or AMD vendor MFTs). Each candidate must accept the same Baseline/NV12 format; a candidate that rejects it is shut down and the next one is tried. Only when none works does `auto` fall back to the Microsoft software encoder. `hardware` fails instead of falling back; `software` skips hardware. The choice is never switched silently mid-stream: a later encoder failure ends the session. The helper writes one diagnostic line to stderr, `LocalShare encoder: hardware|software (<name>)`, so the host can show a software fallback; stdout stays protocol-only.

Asynchronous hardware MFTs are driven by their `METransformNeedInput`/`METransformHaveOutput` events: input is generated only when requested and each output still waits for the tablet credit, so the only extra buffering is the vendor encoder's fixed internal pipeline. Rate-control, GOP, low-latency and zero-B-frame settings are applied best-effort on hardware (vendors expose different properties) and are required on the software encoder.

Hardware encoders often publish no sequence header before streaming and add AUD/SEI/filler NAL units. The helper therefore normalizes every access unit to 4-byte start codes and drops NAL types 6, 9 and 12; all other types are forwarded so the tablet's validator still rejects surprises. When no sequence header was published, CONFIG is built from the SPS and PPS of the first output, which must also contain an IDR; later inline SPS/PPS must stay byte-identical, as the Android validator already requires. Input is still system-memory NV12 in this proof; the zero-copy D3D11 texture path from IddCx is Phase 6 work. No hardware vendor, throughput or host-impact result is claimed until it is measured on Windows.

## Offline tests

- `:app:videoProtocolTest`: real Kotlin parser and SPS/budget/crop helpers with synthetic data; golden header, partial/truncated reads, pre-allocation bounds, deadline, low/high FPS and malformed-header tests.
- `dotnet run --project tests/VideoProtocol/VideoProtocol.csproj`: matching C# golden bytes, fragmented/truncated input, length/metadata rejection and cancellation.
- `python3 tools/test-driver-core.py --sanitize`: includes deterministic generated NV12 pattern/buffer tests alongside existing portable driver tests. It does not run Media Foundation or WDK.

Golden HELLO header (hex): `545649440101000000000020000000000000000000000000`. Codec payload test vectors are synthetic parser fixtures, not proof that a vendor decoder rendered video.

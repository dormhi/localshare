# ADR-004: USB-owned virtual monitor proof

- Status: accepted architecture; Windows implementation/validation pending
- Date: 2026-09-30
- Scope: Phase 4 only

## Owner decisions

Complete all macOS-feasible Phase 4 implementation now; defer Windows-only build, installation and runtime checks until a Windows machine is available. This supersedes the earlier preparation-only interpretation. Retain the existing Windows validation gate before committing driver behavior; no driver installation or machine-security change is authorized.

The virtual monitor exposes 1280×800 and 1920×1080 at 60 Hz. The owner selected a monitor tied to the USB session rather than a standalone manually managed proof: create after the handshake, remove on disconnect/cable loss/host close. One tablet and one monitor only. This does not authorize video transport or a display-topology rewrite.

## Implementation decision

Use the documented IddCx companion-app device-control callback through a Windows device interface. No new TCP server, named-pipe service or background account service. The host holds one device file; the driver assigns its ownership identity. Start, Renew and Stop are fixed-size requests. Renewal occurs only after an accepted USB heartbeat, not from an independent host timer.

Use a five-second lease and a one-second watchdog to cover host hangs or lost cleanup; file cleanup and explicit Stop also remove the monitor. Keep monitor ownership if departure fails, reject another arrival and retry removal. This is a development fail-closed policy, not measured Windows timing. The host offers an explicit virtual-display checkbox, off by default, so Phase 3 USB-only tests can run without a driver. When enabled, driver failures are visible and terminate the combined session.

## Local control ABI v1

Interface GUID: `7e0a104a-6999-4f54-9262-58a25b3d878b` (project-generated identity). IOCTL `0x0022e000` is FILE_DEVICE_UNKNOWN/function 0x800/METHOD_BUFFERED with read+write access. Input is exactly 16 bytes; output length must be zero:

| Bytes | Meaning |
| --- | --- |
| 0–3 | ASCII `LSD4` |
| 4–5 | Version `01 00` |
| 6 | Start=1, Renew=2, Stop=3 |
| 7–15 | Reserved, all zero |

Reject all other sizes, versions, commands and reserved bits. Windows request completion carries status; no raw structures or addresses cross this boundary. This is distinct from the Android `TDSH` protocol. Device access must be limited to SYSTEM, administrators and the explicitly chosen local test user in the Windows-generated package; validate denied-user behavior before installation acceptance. No machine/user SID is committed.

## Windows-dependent work

Real WDK template/project and INF, SDK/WDK target, ACL enforcement, installation/signing/recovery choices, UMDF lifetime/concurrency and D3D/cancellation behavior remain unverified. See [WDK handoff](../../src/windows/driver/WDK-HANDOFF.md) and [combined checklist](../testing/phase-3-4-windows-checklist.md). The source draft and tests are not a driver package, and portable successes cannot close Phase 4.

## Primary-source facts checked 2026-09-30

- [IddCx client configuration](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/ns-iddcx-idd_cx_client_config): the device-control callback supports companion-app communication. We use it only for monitor lifecycle.
- [IddCx objects](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/iddcx-objects): adapter initialization is asynchronous; monitor creation/arrival and departure are separate operations. Departure destroys the monitor, so it must not be deleted again.
- [Monitor modes](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/ns-iddcx-iddcx_monitor_mode) and [target modes](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/ns-iddcx-iddcx_target_mode): monitor timing divider must be zero, target divider must be nonzero. Draft target divider 1 preserves the approved 60 Hz.
- [Swap-chain assignment](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/nc-iddcx-evt_idd_cx_monitor_assign_swapchain): successful assignment transfers chain deletion responsibility to the driver; error-return handling is sensitive and must be validated. [FinishedProcessingFrame](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/iddcx/nf-iddcx-iddcxswapchainfinishedprocessingframe) is required before the next release/acquire call.
- [WDF timers](https://learn.microsoft.com/en-us/windows-hardware/drivers/wdf/using-timers): UMDF timers use user-mode thread-pool workers; a one-shot timer can rearm itself. Stop waits occur outside the control lock.
- [DeviceIoControl](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-deviceiocontrol) and [CancelIoEx](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex): overlapped requests require live event/buffer state; requesting cancellation is not completed cancellation. Host buffers are retained until completion.

The exact WDK/SDK versions remain unselected; this review did not upgrade any toolchain or copy third-party implementation code.

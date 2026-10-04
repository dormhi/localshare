# Phase 3 USB handshake protocol

Phase 5 adds a separately authenticated [generated-video channel](VIDEO.md); the control channel below remains unchanged.

Phase 3 carries control bytes only. It does not send desktop frames or user content.

## Transport and trust boundary

- Windows runs Android Platform-Tools from `PATH`, requires exactly one authorized ADB device, and creates `adb forward tcp:localhost:0 localabstract:com.dormhi.localshare.session`. Naming `localhost` explicitly keeps the temporary host port on loopback even if the ADB server was started with `-a`.
- The host connects only to `127.0.0.1` at the ephemeral forwarded port. Android listens with `LocalServerSocket` in the abstract namespace; the manifest requests no network permission and no general TCP listener is created.
- A random 32-byte session token is passed on `adb shell run-as` standard input into app-private storage, then read and deleted by the app. It is not placed in process arguments or an exported activity intent. This bootstrap requires a debuggable development APK; production credential provisioning is outside Phase 3.
- The token rejects accidental or stale local clients. It does not defend against a compromised ADB-authorized host, a rooted tablet, or other code running under the app's UID.
- Only one authorized tablet may be connected. The app accepts one socket at a time; incomplete clients time out after five seconds. Host connect retries for at most 15 seconds.

## Messages

All integers are unsigned, network byte order. The fixed header is 8 bytes: ASCII `TDSH` (4), protocol version (1 byte, currently `1`), message type (1 byte), payload length (2 bytes).

| Direction | Type | Payload | Meaning |
| --- | ---: | --- | --- |
| Host → Android | 1 | 32-byte session token | Initial hello; header length must be 32 |
| Android → Host | 2 | empty | Accepted hello |
| Host → Android | 3 | empty | Graceful stop |
| Host → Android | 4 | empty | Liveness ping, every 2 seconds |
| Android → Host | 5 | empty | Liveness pong |

The receiver rejects wrong magic/version/type/length, truncated data, unexpected control messages, and token mismatch. After type 2, all messages have zero payload. Incomplete handshakes are closed after five seconds. A missed heartbeat response times out at the host after five seconds, then the host closes the client and removes only its own `tcp:<port>` forwarding rule. No frame buffering is introduced in this phase.

## Device authorization and cleanup

The user enables Android USB debugging, unlocks the tablet, and approves the host's RSA prompt. ADB authorization allows that computer to issue privileged debugging commands; the app never enables `adb tcpip` or Wi-Fi debugging. Disconnect removes the temporary port forward and closes the session. To revoke trust, use Android Developer options → Revoke USB debugging authorizations, then disable USB debugging when it is no longer needed.

ADB keys remain in the user's Platform-Tools profile and are never copied into the repository or app. Device serials, tokens, and frame data are not logged.

## ADB source note

Checked 2026-09-25 against the Android Open Source Project's [`socket_spec.cpp`](https://android.googlesource.com/platform/packages/modules/adb/+/refs/heads/main/socket_spec.cpp): ADB's TCP listener uses its loopback bind path when the host is `localhost`; an omitted host can use the all-interfaces path when the ADB server's `-a` flag is active. Therefore the host uses `tcp:localhost:0`, not the ambiguous `tcp:0` form.

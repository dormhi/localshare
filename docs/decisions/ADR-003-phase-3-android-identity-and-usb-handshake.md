# ADR-003: Android identity and ADB USB handshake

- Status: Accepted
- Date: 2026-09-25
- Scope: v0.2 Phase 3 only

## Context

Phase 3 must prove a small, bounded host-to-tablet handshake over an authorized USB ADB tunnel. It must not send desktop frames, install a display driver, enable wireless debugging, or request an Android network permission.

## Decisions

- Permanent Android application ID: `com.dormhi.localshare`, selected by the project owner. Changing it after distribution creates a separate application identity.
- Android endpoint: ADB `forward` to an abstract `LocalServerSocket` named `com.dormhi.localshare.session`. It avoids declaring `INTERNET`; the host endpoint binds only through ADB's ephemeral TCP port on `127.0.0.1`.
- Authenticate each session with 32 random bytes. The Windows host provisions the token to the app's private files using `adb shell run-as`, then starts the exported launcher activity without embedding the token in its intent. `run-as` makes this a debug-build-only feasibility proof; production token provisioning must be designed before release.
- Permit one authorized ADB device and one active socket. Apply bounded handshake/control messages, client read timeout, connection timeout, heartbeat, and forwarding cleanup. Never enable `adb tcpip`.
- Protocol details and the current trust boundary are documented in [`protocol/README.md`](../../protocol/README.md).

## Security and limitations

ADB authorization is host-wide debugging trust and must be explicitly approved on the tablet. The ephemeral token prevents accidental/stale clients, not a compromised authorized host, rooted device, or code with the app UID. Platform-Tools must be installed separately in this phase; bundling and redistribution are deferred to Phase 9 license review.

## Validation required

Build the Android APK and inspect its merged permissions. On Windows 11, run the host and authorize USB debugging on each named tablet in separate sessions. Verify handshake, unauthorized state, malformed/idle client rejection, heartbeat failure, cable disconnect, reconnect, and forwarding removal. Do not install a driver or change boot/security settings.

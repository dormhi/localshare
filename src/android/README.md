# Android client

Kotlin/Jetpack Compose viewer shell. The Phase 1 screen is static; it does not connect to USB/ADB, use the network, or request Android permissions. The provisional application ID is `org.example.tabletdisplay`; replace it before the first release/install build.

Pinned foundation toolchain and offline build instructions: [`docs/development.md`](../../docs/development.md). Do not assume Google Play services, a particular panel size, refresh rate, or raw USB role. Request only necessary permissions and document USB debugging authorization and cleanup when transport is added.

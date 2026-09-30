# Android client

Kotlin/Jetpack Compose viewer. The permanent application ID is `com.dormhi.localshare`. Android's exported launcher activity is the only exported component; the local ADB endpoint does not require an Android runtime permission.

Pinned toolchain and offline build instructions: [`docs/development.md`](../../docs/development.md). Do not assume Google Play services, a particular panel size, refresh rate, or raw USB role. USB debugging requires the user to authorize the host and can be revoked in Android developer settings. Phase 3 token provisioning uses `run-as` and therefore only works with the debuggable development APK.

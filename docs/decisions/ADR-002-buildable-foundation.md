# ADR-002: Build and run the foundation shells

- Status: accepted; Android validated, Windows validation pending
- Date: 2026-09-23
- Scope/version/phase: Foundation, Phase 2

## Context

At Phase 2 start, Phase 1's WinUI 3 and Android Compose shells had not been compiled or launched. The authoring machine is macOS and has no Windows/WDK environment or connected ADB device. The user selected build validation as the next phase.

## Decision

- Pin the Android Gradle Wrapper to Gradle 9.7.1. AGP 9.4 requires Gradle 9.6.0 or later; 9.7.1 is the stable release selected for this phase.
- Lint's repository-metadata check reported newer Android/Kotlin patches; use AGP 9.4.1 and Kotlin/Compose compiler plugin 2.4.20, then verify by compiling and linting.
- Check in the official wrapper JAR and launchers. Verify the wrapper JAR and downloaded Gradle distribution against their official SHA-256 values.
- Provide Windows PowerShell scripts to restore/build/run the WinUI shell and build (but not install) the Android debug APK.
- Phase 2 does not add ADB transport, install drivers, install the APK, alter system settings, or start the virtual display implementation.
- Phase completion requires Android build/lint success and a successful host restore/build/launch on Windows 11. Tests that cannot run on macOS are not treated as passed.

## Consequences

- First Gradle builds require the pinned distribution and Android build dependencies to be available from the network or local caches.
- Windows host execution requires the Windows App Runtime matching the selected Windows App SDK version.
- The Android application ID remains a build-only placeholder and must be selected before installing/publishing a release build.

## Validation result (2026-09-23)

- Gradle 9.7.1 wrapper checksum and distribution verification passed.
- Android `:app:assembleDebug` and `:app:lintDebug` passed on macOS with JDK 22.0.1 and SDK Platform 37 / Build Tools 36.0.0; lint reported zero findings. Final debug APK SHA-256: `f58b502674a4d0f738b9e3762c038005a042a1f1619dba5003a574bc225e9724`.
- WinUI NuGet restore passed from macOS with Windows targeting enabled. Cross-building stopped at the Windows-only XAML compiler and cannot substitute for a Windows build/run.
- Windows 11 host restore/build/launch remains the outstanding Phase 2 check. The Android APK was not installed on a tablet.

## Sources

- [AGP 9.4 compatibility](https://developer.android.com/build/releases/agp-9-4-0-release-notes) and [Gradle compatibility matrix](https://developer.android.com/build/releases/about-agp).
- [Gradle 9.7.1 release](https://gradle.org/releases/) and [official release checksums](https://gradle.org/release-checksums/).

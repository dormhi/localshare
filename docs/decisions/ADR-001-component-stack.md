# ADR-001: Foundation component stack

- Status: accepted
- Date: 2026-09-23
- Scope/version/phase: Foundation, Phase 1

## Context

The product needs a Windows 11 host UI, an Android tablet viewer, and later a Windows indirect display driver. The repository is a blank scaffold and the authoring host is macOS, so Windows driver compilation and Android device tests are not available here.

## Decision

- Windows host UI: C# with WinUI 3 using the Windows App SDK.
- Display driver: separate C++ IddCx/UMDF component. Phase 1 reserves the directory and documents the environment; it does not invent an unvalidated WDK project.
- Android client: Kotlin with Jetpack Compose, independent of Google Play services.
- Phase 1 includes empty/status UI shells only. It does not implement USB/ADB transport, a display driver, video, touch, or packaging.
- Phase 1 validation uses offline structural/static checks on macOS. Windows build and physical Android validation remain outstanding and must be reported as unavailable, not passed.

## Toolchain baseline

Checked 2026-09-23 from primary vendor documentation:

- .NET 10 LTS; Windows App SDK 2.5.1 stable.
- Android Gradle Plugin 9.4.0; Gradle 9.6.0; JDK 17; Kotlin/Compose compiler plugin 2.4.10; Compose BOM 2026.09.00; compile/target API 37; provisional min API 26.
- AGP 9's built-in Kotlin is enabled; the matching Kotlin Gradle Plugin 2.4.10 is added to the root buildscript classpath and `org.jetbrains.kotlin.android` is not applied.
- Android app ID `org.example.tabletdisplay` is temporary and must change to the user's chosen permanent ID before release.

## Consequences

- The UI and driver have a clear language/runtime boundary; no shared process crosses into the driver.
- The project cannot claim a successful Windows/Android build from the current Mac environment. A later Windows phase must generate/validate the WDK project and a Gradle wrapper.
- No external runtime permissions or dependencies are added for Phase 1 beyond the standard AndroidX/Compose stack.

## Sources

- [Microsoft Windows development path](https://learn.microsoft.com/en-us/windows/apps/get-started/) and [Windows App SDK downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads).
- [Microsoft .NET support](https://learn.microsoft.com/dotnet/core/releases-and-support).
- [Android AGP 9.4 release notes](https://developer.android.com/build/releases/agp-9-4-0-release-notes), [AGP compatibility](https://developer.android.com/build/releases/about-agp), and [Compose setup](https://developer.android.com/develop/ui/compose/setup-compose-dependencies-and-compiler).
- [Android built-in Kotlin migration](https://developer.android.com/build/migrate-to-built-in-kotlin) and [AGP 9 release notes](https://developer.android.com/build/releases/agp-9-0-0-release-notes).
- [Android developer verification FAQ](https://developer.android.com/developer-verification/guides/faq).

# Development setup

This document is the tracked, offline-friendly build and test guide for the public repository. The current phase, test results, pending checks, and agent reading map are in [`docs/project-status.md`](project-status.md). Phase 3 is scoped to an ADB USB handshake and has not passed Windows or physical-device validation. It does not include desktop capture, video, or an IddCx driver.

## Approved component stack

| Component | Foundation choice | Current pinned version |
| --- | --- | --- |
| Windows host UI | C# / WinUI 3, unpackaged framework-dependent shell | .NET 10; Windows App SDK 2.5.1 stable |
| Windows display driver | C++ / IddCx (UMDF) | Driver project deferred until Windows 11 + Visual Studio + WDK environment is available |
| Android viewer | Kotlin / Jetpack Compose | AGP 9.4.1; Gradle 9.7.1; JDK 17 baseline; Kotlin/Compose compiler plugin 2.4.20; Compose BOM 2026.09.00 |
| Android API range | Tablet viewer | `minSdk 26` provisional; `compileSdk` and `targetSdk` 37 |

The Android package ID is `com.dormhi.localshare`, selected by the project owner on 2026-09-25. Package ID and signing key determine update identity; changing either after distribution creates a separate install identity.

## Phase 4 macOS source draft

The owner authorized all macOS-feasible Phase 4 implementation and deferred the outstanding Phase 3 checks into the [combined Windows validation session](testing/phase-3-4-windows-checklist.md). The working tree now includes driver source drafts and portable C++/C# tests; this is not a Windows test pass or permission to install a driver. See the [driver design preparation](decisions/phase-4-driver-design.md); the actual WDK project must be created on Windows. Run `python3 tools/test-driver-core.py --sanitize` and `dotnet run --project tests/HostDriverControl/HostDriverControl.csproj` for the new offline tests. See the [implementation report](testing/phase-4-macos-report.md) and [WDK handoff](../src/windows/driver/WDK-HANDOFF.md).

## Windows host

Build on Windows 11 using Visual Studio 2026 (or a compatible Visual Studio installation with .NET desktop and Windows App SDK tooling) and the .NET 10 SDK:

```powershell
dotnet --info
dotnet restore .\src\windows\TabletDisplay.Host\TabletDisplay.Host.csproj
dotnet build .\src\windows\TabletDisplay.Host\TabletDisplay.Host.csproj -c Debug -p:Platform=x64
```

The shell is an unpackaged WinUI 3 app (`WindowsPackageType=None`) that references the Windows App SDK NuGet package. Run/build validation must happen on Windows. The current Mac checkout cannot validate WinUI runtime, Windows packaging, IddCx, or driver installation.

The display driver is intentionally only documented in `src/windows/driver/README.md`. Do not manufacture a WDK project or install/sign a driver from the Mac environment. Create the actual C++/IddCx UMDF project in a Windows phase with a supported WDK and validate it on the agreed Windows 11 test machine.

## Android viewer

Install JDK 17 or newer, Android SDK Platform 37, and Android SDK Build Tools 36.0.0. Set `ANDROID_HOME` or `ANDROID_SDK_ROOT` to the SDK path. The repository wrapper downloads and pins Gradle; do not install a separate Gradle copy. From the repository root:

```sh
./src/android/gradlew --no-daemon -p src/android :app:assembleDebug
```

The checked-in Gradle wrapper pins Gradle 9.7.1 and validates the distribution SHA-256 before use. AGP 9.4 requires Gradle 9.6.0 or newer; 9.7.1 is the current stable choice verified on 2026-09-23. Phase 2 lint metadata reported newer stable Android/Kotlin patches, so the project is pinned to AGP 9.4.1 and Kotlin/Compose compiler 2.4.20. The wrapper JAR and distribution hashes are pinned in the repository and checked by `tools/validate_scaffold.py`. The first build needs the Android Gradle/Google Maven artifacts available from network or a populated Gradle cache; coding agents do not need browser access.

The source manifest declares no permissions. Phase 3 uses Android `LocalServerSocket` in the abstract namespace rather than an Android TCP listener; the ADB host endpoint is loopback-only and needs no `INTERNET` permission. AndroidX may add a package-scoped `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` protected at `signature` level to the merged debug manifest; this is internal, not user-granted or network access. The Android verification script rejects other requested permissions. Backup and Android 12+ device-transfer rules exclude all app-data domains: [Android backup behavior](https://developer.android.com/about/versions/12/behavior-changes-12), [Auto Backup rules](https://developer.android.com/identity/data/autobackup).

## Phase 3 ADB handshake prototype

Once the Phase 3 Windows changes pass Windows validation, the host will require Android Platform-Tools (`adb`) on `PATH`. Connect exactly one tablet by USB, unlock the tablet, enable USB debugging in Developer options, and approve the host computer's RSA authorization prompt. Start the host shell and press **Connect tablet**. The host is designed to create an ephemeral loopback ADB forward (`tcp:localhost:0`) to `localabstract:com.dormhi.localshare.session`, provision a random session token through `adb shell run-as` standard input, launch the Android app, then verify a versioned handshake and periodic heartbeat. Disconnect should send a stop control message and remove only that session's forwarding rule. This prototype does not send desktop frames.

`run-as` token provisioning is limited to the debuggable development APK and is not a release credential mechanism. Never enable `adb tcpip` or wireless debugging for this flow. USB debugging gives this computer broad debugging access; revoke it under Developer options → Revoke USB debugging authorizations, then disable USB debugging when desired. ADB private keys, device identifiers, and tokens must not enter the repo or logs.

See [ADR-003](decisions/ADR-003-phase-3-android-identity-and-usb-handshake.md) and [`protocol/README.md`](../protocol/README.md) for message bytes, timeout behavior, and trust boundary. Follow the ordered [Windows/device checklist](testing/phase-3-windows-checklist.md): run `tools/verify-windows.ps1` first to record the deferred Phase 2 baseline, then perform Phase 3 host/device tests one by one. Build Android with `:app:assembleDebug :app:lintDebug` and inspect merged APK permissions. Physical testing on each target tablet is required before calling the handshake phase complete.

The launcher icon uses the unqualified `mipmap-anydpi` folder because this app's provisional `minSdk` is 26; its adaptive XML includes foreground, background, and monochrome layers.

## Foundation checks

These checks use Python 3.11+ standard library only and need no network access or external packages:

```sh
python3 tools/validate_scaffold.py
git diff --check
```

Before every commit, run the checks relevant to the changed files and a fast secret/unsafe-permission scan. For the current skeleton, the validator parses project XML/XAML, Android manifest and version catalog, checks required source files and pinned tool versions, and ensures the Android manifest has no permissions. It does not replace Windows or Android compilation.

At phase completion, run the Windows `dotnet build` on Windows 11 and Android `assembleDebug` with the pinned toolchain when those components change. Report any unavailable device/Windows tests as unavailable; do not mark them passed.

Phase 2 platform checks are automated as follows, from the repository root in Windows PowerShell:

```powershell
.\tools\verify-windows.ps1 -Architecture x64
.\tools\verify-android.ps1
python .\tools\validate_scaffold.py
git diff --check
```

Use `-Architecture ARM64` for a Windows on ARM machine. The Windows script restores/builds the host, launches its placeholder window, and waits for it to close; it does not install a driver or change system settings. Install the matching Windows App Runtime 2.5.1 before running if the framework-dependent WinUI app reports that runtime missing. The Android script builds the debug APK, verifies merged permission requests with `aapt`, and reports its SHA-256; it does not install it on a tablet. It requires Android SDK Platform 37 and JDK 17 or newer.

## Security and distribution notes

- Phase 3 is limited to the ADB handshake; desktop frames, IddCx, video, touch, release packaging, and production token provisioning remain out of scope.
- Keep APK signing keys, Windows signing keys/certificates, and device identifiers out of Git.
- Android developer-verification rules are changing. As of 2026-09-23, Android's official documentation says ADB installs remain unchanged; a phased verification rollout begins 2026-09-30 in Brazil, Indonesia, Singapore, and Thailand for participating stores, with wider rollout planned in 2027. Re-check the official policy before public APK distribution. Sources: [Android developer verification](https://developer.android.com/developer-verification/guides) and [FAQ](https://developer.android.com/developer-verification/guides/faq).

## Version facts and primary sources

Versions above were checked on 2026-09-23. Update this table and its compatibility details together when dependencies change; ordinary implementation should not require live web research.

- .NET 10 is an LTS release supported through November 2028: [Microsoft .NET releases and support](https://learn.microsoft.com/dotnet/core/releases-and-support).
- Windows App SDK 2.5.1 is the stable release dated 2026-09-16; WinUI 3 is Microsoft's recommended native framework for new Windows desktop apps: [downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads), [release notes](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0), [Windows development path](https://learn.microsoft.com/en-us/windows/apps/get-started/).
- Unpackaged WinUI apps use the Windows App SDK package and `WindowsPackageType=None` to initialize the framework: [Microsoft unpackaged app sample](https://learn.microsoft.com/en-us/samples/microsoft/windowsappsdk-samples/unpackaged/).
- AGP 9.4 supports API level 37 and requires Gradle 9.6.0 or newer, Build Tools 36.0.0, and JDK 17: [AGP 9.4 release notes](https://developer.android.com/build/releases/agp-9-4-0-release-notes), [AGP/Gradle compatibility](https://developer.android.com/build/releases/about-agp).
- Gradle 9.7.1 is the stable distribution used by the wrapper. The wrapper verifies the official binary distribution SHA-256 `acd53f1edaf02f1a8ff99879f8a34b302661a057d9b063ae9e35b552f804d20a`; the official wrapper JAR SHA-256 is `7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d`: [Gradle releases](https://gradle.org/releases/), [checksum reference](https://gradle.org/release-checksums/).
- AGP 9 enables built-in Kotlin. This project uses KGP 2.4.20 on the buildscript classpath to match the Compose compiler plugin, and does not apply the now-unneeded `org.jetbrains.kotlin.android` plugin: [built-in Kotlin migration](https://developer.android.com/build/migrate-to-built-in-kotlin), [AGP 9 upgrade notes](https://developer.android.com/build/releases/agp-9-0-0-release-notes).
- Jetpack Compose's official setup uses a Kotlin-matched Compose compiler plugin and the Compose BOM: [Compose setup](https://developer.android.com/develop/ui/compose/setup-compose-dependencies-and-compiler).
- Android says ADB installs are unaffected by the current developer-verification rollout; check the official [FAQ](https://developer.android.com/developer-verification/guides/faq) again before distribution.

## Phase 2 validation record (2026-09-23)

- PASS: Gradle wrapper launched Gradle 9.7.1 and verified the official wrapper-JAR and distribution SHA-256 values.
- PASS: Android Gradle configuration, `:app:assembleDebug`, and `:app:lintDebug` on macOS with Java 22.0.1 and Android SDK Platform 37 / Build Tools 36.0.0. AGP 9.4.1 and Kotlin/Compose compiler 2.4.20 are pinned; lint reported zero findings.
- Final debug APK SHA-256: `f58b502674a4d0f738b9e3762c038005a042a1f1619dba5003a574bc225e9724`.
- PASS: `aapt dump permissions` found only AndroidX's package-scoped `DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION`, protected at `signature` level. The APK was not installed because no ADB devices are connected and its application ID is a build-only placeholder.
- PASS: .NET 10 cross-target NuGet restore with `EnableWindowsTargeting=true` on macOS.
- NOT VALIDATED ON THIS HOST: WinUI `dotnet build` failed at Microsoft's `XamlCompiler.exe`, a Windows executable that cannot run on macOS. This does not establish a Windows build failure. Run `tools/verify-windows.ps1` on Windows 11 before Phase 2 is closed.

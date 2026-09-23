#!/usr/bin/env python3
"""Offline structural validation for the Phase 1 app shells."""

from pathlib import Path
import hashlib
import re
import sys
import tomllib
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"FAIL: {message}")


def read(relative: str) -> str:
    path = ROOT / relative
    require(path.is_file(), f"missing file: {relative}")
    return path.read_text(encoding="utf-8")


def main() -> None:
    require(sys.version_info >= (3, 11), "Python 3.11+ is required for TOML parsing")

    host_project = ET.fromstring(read("src/windows/TabletDisplay.Host/TabletDisplay.Host.csproj"))
    # SDK-style project files have no default namespace.
    properties = {
        node.tag: (node.text or "").strip()
        for group in host_project.findall("PropertyGroup")
        for node in group
    }
    require(properties.get("UseWinUI") == "true", "Windows host must use WinUI 3")
    require(properties.get("WindowsPackageType") == "None", "host shell should remain unpackaged in this phase")
    require(properties.get("TargetFramework", "").startswith("net10.0-windows"), "host target must use .NET 10 for Windows")
    packages = [p.attrib for group in host_project.findall("ItemGroup") for p in group.findall("PackageReference")]
    require(any(p.get("Include") == "Microsoft.WindowsAppSDK" and p.get("Version") == "2.5.1" for p in packages), "Windows App SDK version must be pinned to 2.5.1")

    for relative in (
        "src/windows/TabletDisplay.Host/App.xaml",
        "src/windows/TabletDisplay.Host/MainWindow.xaml",
        "src/android/app/src/main/AndroidManifest.xml",
        "src/android/app/src/main/res/xml/data_extraction_rules.xml",
        "src/android/app/src/main/res/xml/backup_rules.xml",
        "src/android/app/src/main/res/values/colors.xml",
        "src/android/app/src/main/res/drawable/ic_launcher_foreground.xml",
        "src/android/app/src/main/res/mipmap-anydpi/ic_launcher.xml",
    ):
        ET.fromstring(read(relative))

    manifest = ET.fromstring(read("src/android/app/src/main/AndroidManifest.xml"))
    android_ns = "{http://schemas.android.com/apk/res/android}"
    permissions = [node.tag for node in manifest if node.tag.endswith("uses-permission")]
    require(not permissions, f"foundation Android manifest must request no permissions: {permissions}")
    app_id = "org.example.tabletdisplay"
    android_build = read("src/android/app/build.gradle.kts")
    require(f'namespace = "{app_id}"' in android_build and f'applicationId = "{app_id}"' in android_build, "temporary Android app ID must match namespace")
    require(f'{android_ns}label' in list(manifest.find("application").attrib), "Android app label is required")
    app_attrs = manifest.find("application").attrib
    require(app_attrs.get(f"{android_ns}icon") == "@mipmap/ic_launcher", "Android launcher icon must be declared")
    require(app_attrs.get(f"{android_ns}dataExtractionRules") == "@xml/data_extraction_rules", "Android 12+ backup/transfer rules must be declared")
    require(app_attrs.get(f"{android_ns}fullBackupContent") == "@xml/backup_rules", "legacy Android backup rules must be declared")
    backup_domains = {"root", "file", "database", "sharedpref", "external", "device_root", "device_file", "device_database", "device_sharedpref"}
    legacy_rules = ET.fromstring(read("src/android/app/src/main/res/xml/backup_rules.xml"))
    legacy_exclusions = {node.attrib.get("domain") for node in legacy_rules if node.tag == "exclude"}
    require(backup_domains.issubset(legacy_exclusions), "legacy backup rules must exclude all app-data domains")
    extraction_rules = ET.fromstring(read("src/android/app/src/main/res/xml/data_extraction_rules.xml"))
    for transfer_mode in ("cloud-backup", "device-transfer"):
        mode = extraction_rules.find(transfer_mode)
        require(mode is not None, f"Android 12+ backup rules must include {transfer_mode}")
        exclusions = {node.attrib.get("domain") for node in mode if node.tag == "exclude"}
        require(backup_domains.issubset(exclusions), f"Android 12+ {transfer_mode} must exclude all app-data domains")

    versions = tomllib.loads(read("src/android/gradle/libs.versions.toml"))
    expected = {
        "agp": "9.4.1",
        "kotlin": "2.4.20",
        "composeBom": "2026.09.00",
        "activityCompose": "1.13.0",
    }
    require(versions.get("versions") == expected, "Android dependency versions differ from the approved toolchain baseline")
    root_gradle = read("src/android/build.gradle.kts")
    require('classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.4.20")' in root_gradle, "KGP runtime must match the Compose compiler plugin version")
    wrapper_properties = read("src/android/gradle/wrapper/gradle-wrapper.properties")
    require("gradle-9.7.1-bin.zip" in wrapper_properties, "Gradle wrapper must pin version 9.7.1")
    require("distributionSha256Sum=acd53f1edaf02f1a8ff99879f8a34b302661a057d9b063ae9e35b552f804d20a" in wrapper_properties, "Gradle distribution checksum is missing or incorrect")
    wrapper_jar = ROOT / "src/android/gradle/wrapper/gradle-wrapper.jar"
    require(wrapper_jar.is_file(), "official Gradle wrapper JAR is missing")
    wrapper_hash = hashlib.sha256(wrapper_jar.read_bytes()).hexdigest()
    expected_wrapper_hash = "7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d"
    require(wrapper_hash == expected_wrapper_hash, "Gradle wrapper JAR checksum does not match the official 9.7.1 checksum")
    require(expected_wrapper_hash in read("src/android/gradle/wrapper/gradle-wrapper.jar.sha256"), "Gradle wrapper checksum record is missing")
    for relative in ("src/android/gradlew", "src/android/gradlew.bat"):
        require((ROOT / relative).is_file(), f"missing repeatable platform verification script: {relative}")
    windows_verification = read("tools/verify-windows.ps1")
    require("dotnet build" in windows_verification and "Start-Process" in windows_verification, "Windows verification must build and launch the shell")
    android_verification = read("tools/verify-android.ps1")
    require(":app:assembleDebug" in android_verification and "aapt dump permissions" in android_verification and "aapt dump xmltree" in android_verification, "Android verification must build and inspect APK permissions/protection levels")
    require("does not install it on a tablet" in android_verification, "Android verification must not install the APK")
    require("compileSdk = 37" in android_build and "targetSdk = 37" in android_build, "Android compile/target SDK must be 37")
    require("minSdk = 26" in android_build, "Android minSdk baseline must remain 26")

    for relative, needle in (
        ("src/windows/TabletDisplay.Host/App.xaml.cs", "OnLaunched"),
        ("src/windows/TabletDisplay.Host/MainWindow.xaml.cs", "InitializeComponent"),
        ("src/android/app/src/main/java/org/example/tabletdisplay/MainActivity.kt", "setContent"),
        ("src/windows/driver/README.md", "C++ UMDF indirect display driver"),
    ):
        require(needle in read(relative), f"expected scaffold marker missing in {relative}")

    secret_patterns = (
        re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
        re.compile(r"AKIA[0-9A-Z]{16}"),
        re.compile(r"(?i)(?:api[_-]?key|client[_-]?secret|password)\s*[:=]\s*['\"]?[^\s'\"]{8,}"),
    )
    candidate_files = [
        path for path in ROOT.rglob("*")
        if path.is_file() and ".git" not in path.parts and path.suffix.lower() not in {".png", ".jpg", ".jar", ".apk"}
    ]
    for path in candidate_files:
        try:
            content = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        require(not any(pattern.search(content) for pattern in secret_patterns), f"secret-like value detected in {path.relative_to(ROOT)}")

    print("PASS: project structure, pinned versions, XML, zero-permission baseline, and secret-pattern scan")


if __name__ == "__main__":
    main()

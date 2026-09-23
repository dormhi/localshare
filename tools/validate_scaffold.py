#!/usr/bin/env python3
"""Offline structural validation for the Phase 1 app shells."""

from pathlib import Path
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

    versions = tomllib.loads(read("src/android/gradle/libs.versions.toml"))
    expected = {
        "agp": "9.4.0",
        "kotlin": "2.4.10",
        "composeBom": "2026.09.00",
        "activityCompose": "1.13.0",
    }
    require(versions.get("versions") == expected, "Android dependency versions differ from the approved toolchain baseline")
    root_gradle = read("src/android/build.gradle.kts")
    require('classpath("org.jetbrains.kotlin:kotlin-gradle-plugin:2.4.10")' in root_gradle, "KGP runtime must match the Compose compiler plugin version")
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

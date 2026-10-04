[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$androidRoot = Join-Path $repositoryRoot "src\android"
$gradleWrapper = Join-Path $androidRoot "gradlew.bat"

$java = Get-Command java -ErrorAction Stop
# Windows PowerShell 5.1 turns native stderr into terminating errors under "Stop"; java -version writes to stderr.
$ErrorActionPreference = "Continue"
$javaVersionOutput = (& $java.Source -version 2>&1 | ForEach-Object { "$_" } | Out-String)
$javaExitCode = $LASTEXITCODE
$ErrorActionPreference = "Stop"
$javaVersionMatch = [regex]::Match($javaVersionOutput, 'version "(?:1\.)?(\d+)')
if ($javaExitCode -ne 0 -or -not $javaVersionMatch.Success -or [int]$javaVersionMatch.Groups[1].Value -lt 17) {
    throw "JDK 17 or newer is required to run Gradle. Detected: $javaVersionOutput"
}

$androidSdk = $env:ANDROID_HOME
if (-not $androidSdk) { $androidSdk = $env:ANDROID_SDK_ROOT }
if (-not $androidSdk -and $env:LOCALAPPDATA) { $androidSdk = Join-Path $env:LOCALAPPDATA "Android\Sdk" }
# Newer SDK managers install API 37 as "android-37.0"; older ones use "android-37".
if (-not $androidSdk -or -not ((Test-Path (Join-Path $androidSdk "platforms\android-37\android.jar")) -or
        (Test-Path (Join-Path $androidSdk "platforms\android-37.0\android.jar")))) {
    throw "Android SDK Platform 37 is required. Install it with Android Studio SDK Manager and set ANDROID_HOME or ANDROID_SDK_ROOT."
}

if (-not (Test-Path $gradleWrapper)) { throw "Gradle wrapper is missing: $gradleWrapper" }

Push-Location $androidRoot
try {
    Write-Host "Building the Android debug APK with the checked-in Gradle wrapper..."
    & $gradleWrapper --no-daemon :app:assembleDebug
    if ($LASTEXITCODE -ne 0) { throw "Gradle assembleDebug failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

$apk = Join-Path $androidRoot "app\build\outputs\apk\debug\app-debug.apk"
if (-not (Test-Path $apk)) { throw "Build succeeded but the expected APK was not produced: $apk" }

$aapt = Join-Path $androidSdk "build-tools\36.0.0\aapt.exe"
if (-not (Test-Path $aapt)) { throw "Android SDK Build Tools 36.0.0 must be installed; expected aapt.exe at '$aapt'." }
$permissionDump = (& $aapt dump permissions $apk | Out-String)
if ($LASTEXITCODE -ne 0) { throw "Could not inspect permissions in the generated APK." }
$requestedPermissions = @([regex]::Matches($permissionDump, "uses-permission: name='([^']+)'") | ForEach-Object { $_.Groups[1].Value })
$androidBuildFile = Get-Content (Join-Path $androidRoot "app\build.gradle.kts") -Raw
$appIdMatch = [regex]::Match($androidBuildFile, 'applicationId\s*=\s*"([^"]+)"')
if (-not $appIdMatch.Success) { throw "Could not determine Android applicationId from app/build.gradle.kts." }
$allowedInternalPermission = "$($appIdMatch.Groups[1].Value).DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
$unexpectedPermissions = @($requestedPermissions | Where-Object { $_ -ne $allowedInternalPermission })
if ($unexpectedPermissions.Count -gt 0) {
    throw "Unexpected Android permissions in the APK: $($unexpectedPermissions -join ', ')"
}
if ($requestedPermissions -contains $allowedInternalPermission) {
    $manifestTree = (& $aapt dump xmltree $apk AndroidManifest.xml | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect merged manifest permission protection levels." }
    $signatureOnlyPermission = '(?s)E: permission .*?android:name\([^)]+\)="' + [regex]::Escape($allowedInternalPermission) + '".*?android:protectionLevel\([^)]+\)=\(type 0x11\)0x2'
    if ($manifestTree -notmatch $signatureOnlyPermission) {
        throw "The AndroidX self-permission is not signature-protected in the merged APK manifest."
    }
}

$hash = (Get-FileHash -Path $apk -Algorithm SHA256).Hash
Write-Host "PASS: Android debug APK built."
Write-Host "No user-granted or network permissions are expected; only AndroidX's package-scoped signature permission is allowed."
Write-Host "APK: $apk"
Write-Host "SHA-256: $hash"
Write-Host "This script does not install it on a tablet."

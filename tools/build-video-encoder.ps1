[CmdletBinding()]
param([ValidateSet("x64", "ARM64")][string]$Architecture = "x64")
$ErrorActionPreference = "Stop"
if ([Environment]::OSVersion.Platform -ne "Win32NT" -or [Environment]::OSVersion.Version.Build -lt 22000) {
    throw "Build this Windows-only helper on Windows 11."
}
if ($env:VSCMD_ARG_TGT_ARCH -ine $Architecture) {
    $locator = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
    if (-not (Test-Path -LiteralPath $locator)) { throw "Install Visual Studio C++ development tools first." }
    $installation = (& $locator -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
    if (-not $installation) { throw "A complete Visual Studio C++ toolchain was not found." }
    & (Join-Path $installation "Common7/Tools/Launch-VsDevShell.ps1") -Arch $Architecture.ToLowerInvariant() -HostArch amd64 -SkipAutomaticLocation
}
$compiler = (Get-Command cl.exe -ErrorAction Stop).Source
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "src\windows\VideoPatternEncoder"
$output = Join-Path $source "out/$Architecture"
New-Item -ItemType Directory -Force -Path $output | Out-Null
Push-Location $output
try {
    & $compiler /nologo /std:c++17 /EHsc /W4 /WX /O2 (Join-Path $source "main.cpp") /Fe:LocalShare.PatternEncoder.exe /link mfplat.lib mf.lib mfuuid.lib wmcodecdspuuid.lib strmiids.lib ole32.lib oleaut32.lib
    if ($LASTEXITCODE -ne 0) { throw "Pattern encoder build failed." }
} finally { Pop-Location }
Write-Host "Build completed; runtime is NOT verified. Rebuild the host to copy the helper alongside it."
Write-Host "Compiler: $compiler; Windows SDK: $env:WindowsSDKVersion; architecture: $Architecture"
Write-Host "No driver, device installation, boot setting or signing operation was performed."

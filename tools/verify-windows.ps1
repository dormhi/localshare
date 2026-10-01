[CmdletBinding()]
param(
    [ValidateSet("x64", "ARM64")]
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repositoryRoot "src\windows\TabletDisplay.Host\TabletDisplay.Host.csproj"
$projectDirectory = Split-Path -Parent $projectFile

if (-not $env:OS -or $env:OS -ne "Windows_NT") {
    throw "Run this check on Windows 11."
}

$buildNumber = [int](Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion").CurrentBuildNumber
if ($buildNumber -lt 22000) {
    throw "Windows 11 (build 22000 or later) is required. Detected build $buildNumber."
}

$dotnetVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $dotnetVersion -notmatch '^10\.') {
    throw ".NET 10 SDK is required. Detected '$dotnetVersion'."
}

Write-Host "Restoring WinUI host with .NET SDK $dotnetVersion..."
& dotnet restore $projectFile -p:Platform=$Architecture
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

Write-Host "Building $Architecture configuration..."
& dotnet build $projectFile --no-restore -c Debug -p:Platform=$Architecture
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$outputDirectory = Join-Path $projectDirectory "bin\$Architecture\Debug"
$executables = @(Get-ChildItem -Path $outputDirectory -Filter "TabletDisplay.Host.exe" -File -Recurse)
if ($executables.Count -ne 1) {
    throw "Expected one TabletDisplay.Host.exe under '$outputDirectory'; found $($executables.Count)."
}

Write-Host "Launching the placeholder host window. Close it to continue."
Write-Host "The matching Windows App Runtime must be installed; this script does not install software or change system settings."
$process = Start-Process -FilePath $executables[0].FullName -WorkingDirectory $projectDirectory -PassThru -Wait
if ($process.ExitCode -ne 0) {
    throw "The host shell exited with code $($process.ExitCode). Check the Windows App Runtime installation and app logs."
}

Write-Host "PASS: Windows 11 host restored, built, launched, and closed with exit code 0."

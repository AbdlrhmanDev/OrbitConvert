<#
.SYNOPSIS
    Builds OrbitConvert release artifacts: self-contained publish, MSI installer, portable ZIP and SHA-256 checksums.

.DESCRIPTION
    Output goes to artifacts\release:
      OrbitConvert-<version>-win-x64.msi        per-user installer (no admin rights)
      OrbitConvert-<version>-win-x64-portable.zip
      SHA256SUMS.txt
    Binaries are NOT code-signed unless you sign them yourself afterwards.

.EXAMPLE
    pwsh scripts/build-release.ps1
    pwsh scripts/build-release.ps1 -Version 0.3.0 -Runtime win-arm64 -SkipTests
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$AppendArtifacts,
    [switch]$SkipTests,
    [switch]$NoRestore,
    [string]$DotNetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish\OrbitConvert-$Runtime"
$release = Join-Path $artifacts "release"
$restoreArg = if ($NoRestore) { "--no-restore" } else { $null }

function Invoke-Step([string]$Title, [scriptblock]$Action) {
    Write-Host "==> $Title" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Title failed with exit code $LASTEXITCODE" }
}

if (-not $Version) {
    $Version = (& $DotNetPath msbuild "$root\src\OrbitConvert.Core\OrbitConvert.Core.csproj" -getProperty:Version).Trim()
}
Write-Host "OrbitConvert $Version" -ForegroundColor Green

Remove-Item -Recurse -Force $publish -ErrorAction SilentlyContinue
if (-not $AppendArtifacts) { Remove-Item -Recurse -Force $release -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force $release | Out-Null

if (-not $SkipTests) {
    Invoke-Step "Test" { & $DotNetPath test "$root\OrbitConvert.sln" -c $Configuration --nologo -p:UseSharedCompilation=false }
}

# Self-contained: users do not need to install .NET.
Invoke-Step "Publish desktop app" {
    & $DotNetPath publish "$root\src\OrbitConvert.App\OrbitConvert.App.csproj" -c $Configuration -r $Runtime --self-contained $restoreArg `
        -p:Version=$Version -p:DebugType=none -p:UseSharedCompilation=false -m:1 -nodeReuse:false -o $publish --nologo
}
Invoke-Step "Publish command-line tool" {
    & $DotNetPath publish "$root\src\OrbitConvert.Cli\OrbitConvert.Cli.csproj" -c $Configuration -r $Runtime --self-contained $restoreArg `
        -p:Version=$Version -p:DebugType=none -p:UseSharedCompilation=false -m:1 -nodeReuse:false -o $publish --nologo
}

# Portable ZIP: the marker file makes OrbitConvert keep settings in .\data next to the executable.
$portableName = "OrbitConvert-$Version-$Runtime-portable"
$staging = Join-Path $artifacts "staging\$portableName"
Remove-Item -Recurse -Force (Join-Path $artifacts "staging") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item -Recurse "$publish\*" $staging
Set-Content -Path (Join-Path $staging "portable.txt") -Encoding utf8 -Value @"
This file enables portable mode: settings, history and downloaded tools are stored in the 'data' folder next to OrbitConvert.exe.
Delete this file to store data in %LOCALAPPDATA%\OrbitConvert instead.
"@
$zip = Join-Path $release "$portableName.zip"
Invoke-Step "Create portable ZIP" { Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal; $global:LASTEXITCODE = 0 }

Invoke-Step "Build MSI installer" {
    $installerPlatform = if ($Runtime -eq "win-arm64") { "arm64" } else { "x64" }
    & $DotNetPath build "$root\installer\OrbitConvert.Installer.wixproj" -c $Configuration $restoreArg -p:Version=$Version -p:Runtime=$Runtime `
        -p:InstallerPlatform=$installerPlatform -p:PublishDir=$publish -p:UseSharedCompilation=false -m:1 -nodeReuse:false -o $release --nologo
}
Get-ChildItem $release -Filter *.wixpdb | Remove-Item -Force

$sums = Get-ChildItem $release -File | Where-Object Name -ne "SHA256SUMS.txt" | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
Set-Content -Path (Join-Path $release "SHA256SUMS.txt") -Value $sums -Encoding ascii

Write-Host ""
Write-Host "Release artifacts:" -ForegroundColor Green
Get-ChildItem $release | ForEach-Object { "{0,10:N1} MB  {1}" -f ($_.Length / 1MB), $_.Name }

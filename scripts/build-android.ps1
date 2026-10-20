<#
.SYNOPSIS
  Builds the signed Android App Bundle (.aab) for Google Play.

.DESCRIPTION
  Reads signing data from %USERPROFILE%\.snakevs-keys\snakevs-upload.properties (see create-keystore.ps1)
  or from the environment variables SNAKEVS_KEYSTORE, SNAKEVS_KEY_ALIAS, SNAKEVS_KEYSTORE_PASS, SNAKEVS_KEY_PASS.
  Runs the unit tests first. Output: artifacts\android\com.devolib.snakevs-Signed.aab

.PARAMETER VersionCode
  Optional versionCode override (must grow on every Play upload).

.PARAMETER VersionName
  Optional versionName override (e.g. 1.0.1).
#>
param(
    [int]$VersionCode = 0,
    [string]$VersionName = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $env:USERPROFILE ".snakevs-keys\snakevs-upload.properties"

if (-not $env:SNAKEVS_KEYSTORE -and (Test-Path $props)) {
    $map = @{}
    Get-Content $props | ForEach-Object {
        $i = $_.IndexOf("=")
        if ($i -gt 0) { $map[$_.Substring(0, $i)] = $_.Substring($i + 1) }
    }
    $env:SNAKEVS_KEYSTORE = $map["keystore"]
    $env:SNAKEVS_KEY_ALIAS = $map["alias"]
    $env:SNAKEVS_KEYSTORE_PASS = $map["storePassword"]
    $env:SNAKEVS_KEY_PASS = $map["keyPassword"]
}

if (-not $env:SNAKEVS_KEYSTORE) {
    throw "No signing key. Run scripts\create-keystore.ps1 first (or set the SNAKEVS_* environment variables)."
}

if (-not $SkipTests) {
    dotnet test (Join-Path $root "tests\SnakeVs.Core.Tests") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

$out = Join-Path $root "artifacts\android"
$extra = @()
if ($VersionCode -gt 0) { $extra += "-p:ApplicationVersion=$VersionCode" }
if ($VersionName) { $extra += "-p:ApplicationDisplayVersion=$VersionName" }

dotnet publish (Join-Path $root "src\SnakeVs.Android\SnakeVs.Android.csproj") -c Release -o $out --nologo @extra
if ($LASTEXITCODE -ne 0) { throw "Android publish failed." }

Get-ChildItem $out -Filter *.aab | ForEach-Object { Write-Host "Bundle: $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" }

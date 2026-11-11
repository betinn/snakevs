<#
.SYNOPSIS
  Builds the web version (static site) of Snake VS.

.DESCRIPTION
  Runs the unit tests first, then publishes src/SnakeVs.Web in Release to artifacts\web.
    artifacts\web\wwwroot  the site: upload its CONTENTS (Apache/LiteSpeed, nginx, static hosts)
    artifacts\web           IIS / Azure App Service: upload the whole folder (web.config points at wwwroot)
  See docs/WEB.md.
#>
param(
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\web"

if (-not $SkipTests) {
    dotnet test (Join-Path $root "tests\SnakeVs.Core.Tests") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish (Join-Path $root "src\SnakeVs.Web\SnakeVs.Web.csproj") -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

Write-Host "Site ready: $(Join-Path $out 'wwwroot')  (IIS: $out)"

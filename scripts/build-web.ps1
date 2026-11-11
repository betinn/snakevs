<#
.SYNOPSIS
  Builds the web version of Snake VS (static site).

.DESCRIPTION
  Runs the unit tests first, then publishes the game to artifacts\web\wwwroot. Upload the CONTENTS of that folder
  to the site (nginx root on the VM). The online ranking is a separate tiny server (src/SnakeVs.Api, binaries built
  by the "ranking-api" GitHub workflow); nginx sends /api/ to it. See docs/WEB.md.

.PARAMETER ApiUrl
  Ranking API hosted on another site (that server must list this site in SNAKEVS_ALLOWED_ORIGINS).
  Default: same site (/api/ on the host serving the game).

.PARAMETER NoRanking
  Turns the online ranking off (no buttons in the game). For a host without the ranking server.
#>
param(
    [string]$ApiUrl = "",
    [switch]$NoRanking,
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

$site = Join-Path $out "wwwroot"
$api = if ($NoRanking) { "off" } else { $ApiUrl }
if ($api) {
    # Point the game at the ranking API (or turn it off). The precompressed copies of index.html are stale after
    # the edit, so they are removed: index.html is tiny and is then sent uncompressed.
    $index = Join-Path $site "index.html"
    $html = [IO.File]::ReadAllText($index)
    $html = $html.Replace('<meta name="snakevs-api" content="" />', "<meta name=`"snakevs-api`" content=`"$api`" />")
    [IO.File]::WriteAllText($index, $html, (New-Object Text.UTF8Encoding $false))
    Remove-Item (Join-Path $site "index.html.br"), (Join-Path $site "index.html.gz") -ErrorAction SilentlyContinue
}

$ranking = if ($NoRanking) { "off" } elseif ($ApiUrl) { $ApiUrl } else { "same site (/api/)" }
Write-Host "Site ready: $site  (ranking: $ranking)"

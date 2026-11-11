<#
.SYNOPSIS
  Builds the web version of Snake VS.

.DESCRIPTION
  Runs the unit tests first, then publishes to artifacts\web.

  Default: SnakeVs.Server (game + online ranking with SQLite). Upload the whole artifacts\web folder to a host that
  runs .NET 10 (Linux with systemd + nginx, IIS, Azure App Service...). See docs/WEB.md.

  -StaticOnly: only the game, as a static site without ranking (Apache/LiteSpeed shared hosting, Cloudflare Pages...).
  Upload the CONTENTS of artifacts\web\wwwroot.

.PARAMETER StaticOnly
  Publish the game alone, with the online ranking turned off.

.PARAMETER ApiUrl
  With -StaticOnly: URL of a SnakeVs.Server hosted elsewhere, to keep the ranking (that server must list this
  site in Leaderboard:AllowedOrigins).
#>
param(
    [switch]$StaticOnly,
    [string]$ApiUrl = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\web"

if (-not $SkipTests) {
    dotnet test (Join-Path $root "tests\SnakeVs.Core.Tests") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Core tests failed." }
    dotnet test (Join-Path $root "tests\SnakeVs.Server.Tests") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Server tests failed." }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

if (-not $StaticOnly) {
    dotnet publish (Join-Path $root "src\SnakeVs.Server\SnakeVs.Server.csproj") -c Release -o $out
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
    Write-Host "Site + ranking ready: $out  (run: dotnet SnakeVs.Server.dll)"
    return
}

dotnet publish (Join-Path $root "src\SnakeVs.Web\SnakeVs.Web.csproj") -c Release -o $out
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Point the game at the ranking API (or turn it off). The precompressed copies of index.html are stale after the
# edit, so they are removed: index.html is tiny and servers compress it on the fly.
$site = Join-Path $out "wwwroot"
$index = Join-Path $site "index.html"
$api = if ($ApiUrl) { $ApiUrl } else { "off" }
$html = [IO.File]::ReadAllText($index)
$html = $html.Replace('<meta name="snakevs-api" content="" />', "<meta name=`"snakevs-api`" content=`"$api`" />")
[IO.File]::WriteAllText($index, $html, (New-Object Text.UTF8Encoding $false))
Remove-Item (Join-Path $site "index.html.br"), (Join-Path $site "index.html.gz") -ErrorAction SilentlyContinue

Write-Host "Static site ready: $site  (ranking: $api)"

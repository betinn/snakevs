<#
.SYNOPSIS
  Creates the Play Store *upload* key for Snake VS (run once).

.DESCRIPTION
  Generates a random password, creates an RSA 4096 keystore and stores both OUTSIDE the repository:
    %USERPROFILE%\.snakevs-keys\snakevs-upload.jks
    %USERPROFILE%\.snakevs-keys\snakevs-upload.properties
  Back up that folder (password manager / safe storage). With Play App Signing a lost upload key can be
  reset through the Play Console, but it takes days.
#>
param(
    [string]$KeyDir = (Join-Path $env:USERPROFILE ".snakevs-keys"),
    [string]$Alias = "snakevs-upload",
    [string]$DName = "CN=Snake VS, OU=Games, O=Devolib, C=BR"
)

$ErrorActionPreference = "Stop"
$keystore = Join-Path $KeyDir "snakevs-upload.jks"
$props = Join-Path $KeyDir "snakevs-upload.properties"

if (Test-Path $keystore) {
    Write-Host "Keystore already exists: $keystore (nothing to do)"
    exit 0
}

$keytool = Get-Command keytool -ErrorAction SilentlyContinue
if (-not $keytool) {
    $candidate = Get-ChildItem "C:\Program Files\Android\openjdk", "C:\Program Files\Microsoft", "$env:JAVA_HOME" -Recurse -Filter keytool.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $candidate) { throw "keytool.exe not found. Install a JDK (17+) or set JAVA_HOME." }
    $keytool = $candidate.FullName
} else {
    $keytool = $keytool.Source
}

New-Item -ItemType Directory -Force $KeyDir | Out-Null
$bytes = New-Object byte[] 24
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = [Convert]::ToBase64String($bytes).Replace("+", "A").Replace("/", "B").Replace("=", "")

& $keytool -genkeypair -v -keystore $keystore -alias $Alias -keyalg RSA -keysize 4096 -validity 10000 `
    -storepass $password -keypass $password -dname $DName | Out-Null
if ($LASTEXITCODE -ne 0) { throw "keytool failed." }

@"
keystore=$keystore
alias=$Alias
storePassword=$password
keyPassword=$password
"@ | Set-Content -Path $props -Encoding ascii

Write-Host "Upload keystore created in $KeyDir"
Write-Host "BACK UP THIS FOLDER. It is not in git on purpose."

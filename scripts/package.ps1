# package.ps1 - Builds the executable and assembles a distributable ZIP for a GitHub Release.
# The ZIP is what a non-developer downloads, extracts, and installs with scripts\install.ps1.

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root  = Split-Path -Parent $PSScriptRoot
$dist  = Join-Path $root "dist"
$stage = Join-Path $dist "PLC-WebControl-$Version"

# 1) Build the executable against the local Siemens API DLL.
& (Join-Path $PSScriptRoot "build.ps1")

# 2) Stage the runnable layout.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item (Join-Path $root "PlcWebControl.exe")        $stage
Copy-Item (Join-Path $root "appconfig.example.txt")    $stage
Copy-Item (Join-Path $root "README.md")                $stage -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "LICENSE")                  $stage -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "CHANGELOG.md")             $stage -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "wwwroot")  (Join-Path $stage "wwwroot")  -Recurse
Copy-Item (Join-Path $root "scripts")  (Join-Path $stage "scripts")  -Recurse
Copy-Item (Join-Path $root "docs")     (Join-Path $stage "docs")     -Recurse

# 3) Zip it.
$zip = Join-Path $dist "PLC-WebControl-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force

Write-Host ""
Write-Host "Release package: $zip" -ForegroundColor Green
Write-Host "Attach this ZIP to your GitHub Release (tag v$Version)."

# install.ps1 - Guided installer for PLC-WebControl.
#
# RUN THIS IN AN ELEVATED POWERSHELL (Run as administrator).
#
# It registers a Scheduled Task that starts the web service whenever the install account logs on,
# so the service is always available. PLCSIM Advanced REQUIRES an interactive user session, so the
# task runs as your user at logon (not as SYSTEM). For fully unattended boot, also configure
# auto-logon (see scripts\setup-autologon.ps1).
#
# Usage:
#   .\install.ps1                 # interactive, localhost-only (recommended)
#   .\install.ps1 -Port 8090
#   .\install.ps1 -Lan            # also expose the UI to the LAN (urlacl + firewall)

param(
    [int]$Port = 8090,
    [switch]$Lan,
    [string]$TaskName = "PLCSIM WebControl"
)

$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $PSScriptRoot
$exe     = Join-Path $root "PlcWebControl.exe"
$cfg     = Join-Path $root "appconfig.txt"
$example = Join-Path $root "appconfig.example.txt"

function Assert-Admin {
    $id = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $id.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Please run this script in an ELEVATED PowerShell (Run as administrator)."
    }
}
Assert-Admin

Write-Host "==== PLC-WebControl installer ====" -ForegroundColor Cyan

# 1) Build the executable if it is missing.
if (-not (Test-Path $exe)) {
    Write-Host "PlcWebControl.exe not found - building it now..."
    & (Join-Path $PSScriptRoot "build.ps1")
}

# 2) Check the Siemens PLCSIM Advanced API DLL is present (auto-detected at runtime).
$api = Get-ChildItem -Path @("$env:ProgramFiles\Siemens\Automation","${env:ProgramFiles(x86)}\Siemens\Automation") `
        -Recurse -Filter "Siemens.Simatic.Simulation.Runtime.Api.x64.dll" -ErrorAction SilentlyContinue |
       Select-Object -First 1 -ExpandProperty FullName
if ($api) { Write-Host "Found PLCSIM Advanced API DLL: $api" -ForegroundColor Green }
else { Write-Warning "PLCSIM Advanced API DLL not found. Install S7-PLCSIM Advanced before starting the service, or set api_dll_path in appconfig.txt." }

# 3) Ask about network exposure (unless -Lan was passed).
if (-not $Lan) {
    Write-Host ""
    Write-Host "Network exposure:" -ForegroundColor Cyan
    Write-Host "  [1] localhost only   (recommended - the UI has NO authentication)"
    Write-Host "  [2] expose to the LAN (any machine on your network can control the PLCs)"
    $choice = Read-Host "Choose 1 or 2 [1]"
    if ($choice -eq "2") { $Lan = $true }
}
if ($Lan) {
    $bindHost = "+"
    Write-Warning "Exposing to the LAN. The UI has NO authentication - only do this on a trusted network."
} else {
    $bindHost = "localhost"
}
$prefix = "http://${bindHost}:$Port/"

# 4) Create appconfig.txt from the template (first install only) and set http_prefix.
if (-not (Test-Path $cfg)) {
    if (Test-Path $example) { Copy-Item $example $cfg }
    else { New-Item -ItemType File -Path $cfg | Out-Null }
    Write-Host "Created appconfig.txt from template."
}
$lines = Get-Content $cfg
if ($lines -match '^\s*http_prefix\s*=') {
    $lines = $lines | ForEach-Object { if ($_ -match '^\s*http_prefix\s*=') { "http_prefix = $prefix" } else { $_ } }
} else {
    $lines += "http_prefix = $prefix"
}
Set-Content -Path $cfg -Value $lines -Encoding UTF8
Write-Host "appconfig.txt -> http_prefix = $prefix"

# 5) LAN: reserve the URL for the install account + open the firewall.
if ($Lan) {
    $aclUser = "$env:USERDOMAIN\$env:USERNAME"
    Write-Host "Reserving URL $prefix for $aclUser ..."
    cmd /c "netsh http add urlacl url=$prefix user=`"$aclUser`"" | Out-Null
    if (-not (Get-NetFirewallRule -DisplayName "$TaskName $Port" -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName "$TaskName $Port" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Any | Out-Null
        Write-Host "Firewall rule created (TCP $Port)."
    }
}

# 6) Register the always-on Scheduled Task (runs at logon, in the interactive session, elevated).
$curUser = "$env:USERDOMAIN\$env:USERNAME"
$action  = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $root
$set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
            -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $curUser
$prin    = New-ScheduledTaskPrincipal -UserId $curUser -LogonType Interactive -RunLevel Highest
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $set -Principal $prin -Force | Out-Null

Write-Host ""
Write-Host "Installed scheduled task '$TaskName' (runs as $curUser at logon)." -ForegroundColor Green
Write-Host "Start now:  Start-ScheduledTask -TaskName '$TaskName'"
Write-Host "Open UI:    http://localhost:$Port"
if ($Lan) {
    $ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1).IPAddress
    if ($ip) { Write-Host "LAN URL:    http://${ip}:$Port" -ForegroundColor Cyan }
}
Write-Host "Logs:       $root\webcontrol.log"
Write-Host ""
Write-Host "For unattended boot (auto-power a PLC after a server restart), also set up auto-logon:" -ForegroundColor Yellow
Write-Host "  scripts\setup-autologon.ps1   (optional; see docs\INSTALL.md)"

$startNow = Read-Host "Start the service now? [Y/n]"
if ($startNow -ne "n") {
    Start-ScheduledTask -TaskName $TaskName
    Start-Sleep 3
    Write-Host "Started. Recent log:"
    if (Test-Path "$root\webcontrol.log") { Get-Content "$root\webcontrol.log" -Tail 8 }
}

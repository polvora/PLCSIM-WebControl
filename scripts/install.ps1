# install.ps1 - Guided installer for PLCSIM-AutoStart.
#
# RUN THIS IN AN ELEVATED POWERSHELL (Run as administrator).
#
# By default it installs a Windows Service you can Start/Stop from services.msc / Task Manager.
# PLCSIM Advanced cannot run from the hidden SYSTEM/session-0 context, so the service runs as
# LocalSystem only as a LAUNCHER: it starts the web app inside the logged-in user's interactive
# session (where PLCSIM works). For unattended boot, also configure auto-logon (offered below).
# Use -AsTask to install a Scheduled Task instead of a service.
#
# Usage:
#   .\install.ps1                 # interactive; Windows Service, LAN-open by default
#   .\install.ps1 -Port 8091
#   .\install.ps1 -LocalOnly      # bind to localhost only
#   .\install.ps1 -AsTask         # install a Scheduled Task instead of a Windows Service

param(
    [int]$Port = 8090,
    [switch]$LocalOnly,
    [switch]$AsTask,
    [string]$TaskName = "PLCSIM AutoStart"
)

$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $PSScriptRoot
$exe     = Join-Path $root "PlcsimAutoStart.exe"
$cfg     = Join-Path $root "appconfig.txt"
$example = Join-Path $root "appconfig.example.txt"

function Assert-Admin {
    $id = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $id.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Please run this script in an ELEVATED PowerShell (Run as administrator)."
    }
}
Assert-Admin

Write-Host "==== PLCSIM-AutoStart installer ====" -ForegroundColor Cyan
if ($AsTask) { Write-Host "Install method: Scheduled Task (-AsTask)." }
else { Write-Host "Install method: Windows Service - manage it later from services.msc / Task Manager > Services." }

# 1) Build the executable if it is missing.
if (-not (Test-Path $exe)) {
    Write-Host "PlcsimAutoStart.exe not found - building it now..."
    & (Join-Path $PSScriptRoot "build.ps1")
}

# 2) Check the Siemens PLCSIM Advanced API DLL is present (auto-detected at runtime).
$api = Get-ChildItem -Path @("$env:ProgramFiles\Siemens\Automation","${env:ProgramFiles(x86)}\Siemens\Automation") `
        -Recurse -Filter "Siemens.Simatic.Simulation.Runtime.Api.x64.dll" -ErrorAction SilentlyContinue |
       Select-Object -First 1 -ExpandProperty FullName
if ($api) { Write-Host "Found PLCSIM Advanced API DLL: $api" -ForegroundColor Green }
else { Write-Warning "PLCSIM Advanced API DLL not found. Install S7-PLCSIM Advanced before starting the service, or set api_dll_path in appconfig.txt." }

# 3) Network exposure. Default = LAN (remote control is the main feature). -LocalOnly forces localhost.
$Lan = $true
if ($LocalOnly) {
    $Lan = $false
} else {
    Write-Host ""
    Write-Host "Network exposure:" -ForegroundColor Cyan
    Write-Host "  [1] LAN - reachable from other machines on your network (default; the main feature)"
    Write-Host "  [2] localhost only - this machine's browser only"
    $choice = Read-Host "Choose 1 or 2 [1]"
    if ($choice -eq "2") { $Lan = $false }
}
if ($Lan) {
    $bindHost = "+"
    Write-Host "Binding to all interfaces (LAN). Note: the UI has no authentication - intended for trusted / closed networks."
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

# 6) Install as a Windows Service (default) or a Scheduled Task (-AsTask).
$curUser = "$env:USERDOMAIN\$env:USERNAME"

# Always clear any previous install of either kind with this name, to avoid duplicates.
$svcExisting = Get-Service -Name $TaskName -ErrorAction SilentlyContinue
if ($svcExisting) {
    if ($svcExisting.Status -ne 'Stopped') { Stop-Service -Name $TaskName -Force -ErrorAction SilentlyContinue }
    & sc.exe delete "$TaskName" | Out-Null; Start-Sleep 1
}
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Get-Process PlcsimAutoStart -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Legacy cleanup: remove the pre-rename "PLCSIM WebControl" service/task + old binary, if upgrading.
$legacy = "PLCSIM WebControl"
$lsvc = Get-Service -Name $legacy -ErrorAction SilentlyContinue
if ($lsvc) { Stop-Service -Name $legacy -Force -ErrorAction SilentlyContinue; & sc.exe delete "$legacy" | Out-Null }
Unregister-ScheduledTask -TaskName $legacy -Confirm:$false -ErrorAction SilentlyContinue
Get-Process PlcsimWebControl -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-NetFirewallRule -DisplayName "$legacy $Port" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

if (-not $AsTask) {
    # Windows Service (launcher): shows in services.msc / Task Manager; runs as LocalSystem and starts
    # the web app in the interactive session so PLCSIM works.
    New-Service -Name $TaskName -BinaryPathName "`"$exe`" --service" -DisplayName $TaskName `
        -Description "PLCSIM-AutoStart: launches the web app in the interactive session." `
        -StartupType Automatic | Out-Null
    & sc.exe failure "$TaskName" reset= 0 actions= restart/60000 | Out-Null
    $installedAs = "service"
    Write-Host ""
    Write-Host "Installed Windows Service '$TaskName' (Automatic, LocalSystem)." -ForegroundColor Green
    Write-Host "Manage in services.msc / Task Manager > Services, or:  Start-Service / Stop-Service '$TaskName'"
} else {
    # Scheduled Task (fallback): runs the web app directly at the install user's logon.
    $action  = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $root
    $set     = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
                -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1)
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $curUser
    $prin    = New-ScheduledTaskPrincipal -UserId $curUser -LogonType Interactive -RunLevel Highest
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $set -Principal $prin -Force | Out-Null
    $installedAs = "task"
    Write-Host ""
    Write-Host "Installed scheduled task '$TaskName' (runs as $curUser at logon)." -ForegroundColor Green
}

Write-Host "Open UI:    http://localhost:$Port"
if ($Lan) {
    $ip = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1).IPAddress
    if ($ip) { Write-Host "LAN URL:    http://${ip}:$Port" -ForegroundColor Cyan }
}
if (-not $AsTask) { Write-Host "Logs:       $root\autostart.log   (service log: $root\service.log)" }
else { Write-Host "Logs:       $root\autostart.log" }

# 7) Auto-logon for unattended boot (a core feature: PLCSIM only runs while a user is logged in).
Write-Host ""
Write-Host "Auto-logon - unattended boot:" -ForegroundColor Cyan
Write-Host "  PLCSIM only runs while a user is signed in. Auto-logon makes Windows sign '$curUser' in"
Write-Host "  automatically after a reboot, so the app (and your PLCs) come back with nobody present."
Write-Host "  It stores that account's password (encrypted if Sysinternals Autologon is available)."
$al = Read-Host "Set up auto-logon for $curUser now? [y/N]"
if ($al -eq "y") {
    try { & (Join-Path $PSScriptRoot "setup-autologon.ps1") -User $env:USERNAME -Domain $env:USERDOMAIN -NoPrompt }
    catch { Write-Warning "Auto-logon setup failed: $($_.Exception.Message). Retry later with scripts\setup-autologon.ps1" }
} else {
    Write-Host "  Skipped. You can enable it later by re-running the installer or scripts\setup-autologon.ps1."
}

Write-Host ""
$startNow = Read-Host "Start it now? [Y/n]"
if ($startNow -ne "n") {
    if ($installedAs -eq "service") { Start-Service -Name $TaskName } else { Start-ScheduledTask -TaskName $TaskName }
    Write-Host "Starting... (the web app takes a few seconds to come up)"
    Start-Sleep 6
    if (Test-Path "$root\autostart.log") { Write-Host "Recent app log:"; Get-Content "$root\autostart.log" -Tail 8 }
}

# uninstall.ps1 - Removes the PLCSIM-AutoStart Windows Service / scheduled task and its LAN bindings.
#
# RUN THIS IN AN ELEVATED POWERSHELL (Run as administrator).
# It does NOT delete the program files, your appconfig.txt, logs, or PLCSIM workspaces.

param(
    [int]$Port = 8090,
    [string]$TaskName = "PLCSIM AutoStart"
)

$ErrorActionPreference = "SilentlyContinue"
$id = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $id.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Please run this script in an ELEVATED PowerShell (Run as administrator)."
    return
}

Write-Host "Stopping and removing '$TaskName' (service and/or task)..."
# Windows Service (if installed that way)
$svc = Get-Service -Name $TaskName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $TaskName -Force -ErrorAction SilentlyContinue }
    & sc.exe delete "$TaskName" | Out-Null
}
# Scheduled Task (if installed that way)
Stop-ScheduledTask -TaskName $TaskName
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
# The web app process (launched in the interactive session)
Get-Process PlcsimAutoStart | Stop-Process -Force

# Legacy: also remove the pre-rename "PLCSIM WebControl" service/task/process, if present.
$legacy = "PLCSIM WebControl"
$lsvc = Get-Service -Name $legacy -ErrorAction SilentlyContinue
if ($lsvc) { Stop-Service -Name $legacy -Force -ErrorAction SilentlyContinue; & sc.exe delete "$legacy" | Out-Null }
Unregister-ScheduledTask -TaskName $legacy -Confirm:$false
Get-Process PlcsimWebControl | Stop-Process -Force
Get-NetFirewallRule -DisplayName "$legacy $Port" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host "Removing LAN URL reservation and firewall rule (if any)..."
cmd /c "netsh http delete urlacl url=http://+:$Port/" | Out-Null
Get-NetFirewallRule -DisplayName "$TaskName $Port" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host "Done. Program files and configuration were left in place." -ForegroundColor Green
Write-Host "If you also configured auto-logon, undo it with sysinternals Autologon or by clearing"
Write-Host "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon."

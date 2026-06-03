# uninstall.ps1 - Removes the PLC-WebControl scheduled task and (optionally) its LAN bindings.
#
# RUN THIS IN AN ELEVATED POWERSHELL (Run as administrator).
# It does NOT delete the program files, your appconfig.txt, logs, or PLCSIM workspaces.

param(
    [int]$Port = 8090,
    [string]$TaskName = "PLCSIM WebControl"
)

$ErrorActionPreference = "SilentlyContinue"
$id = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $id.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Please run this script in an ELEVATED PowerShell (Run as administrator)."
    return
}

Write-Host "Stopping and removing scheduled task '$TaskName'..."
Stop-ScheduledTask -TaskName $TaskName
Get-Process PlcWebControl | Stop-Process -Force
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false

Write-Host "Removing LAN URL reservation and firewall rule (if any)..."
cmd /c "netsh http delete urlacl url=http://+:$Port/" | Out-Null
Get-NetFirewallRule -DisplayName "$TaskName $Port" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

Write-Host "Done. Program files and configuration were left in place." -ForegroundColor Green
Write-Host "If you also configured auto-logon, undo it with sysinternals Autologon or by clearing"
Write-Host "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\AutoAdminLogon."

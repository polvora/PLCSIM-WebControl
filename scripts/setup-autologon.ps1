# setup-autologon.ps1 - OPTIONAL helper to enable Windows auto-logon, so the PLCSIM-AutoStart
# service starts unattended after a reboot (PLCSIM Advanced needs a logged-in Windows session).
#
# RUN THIS IN AN ELEVATED POWERSHELL (Run as administrator).
#
# ============================ SECURITY WARNING ============================
# Auto-logon means the machine boots straight into a user session WITHOUT asking
# for a password. Anyone with physical/console access gets that session.
#
# The registry method below stores the password in PLAINTEXT under
#   HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\DefaultPassword
# Prefer Sysinternals "Autologon" (https://learn.microsoft.com/sysinternals/downloads/autologon)
# which stores the password as an ENCRYPTED LSA secret instead. This script will use it
# automatically if autologon.exe / autologon64.exe is on the PATH or next to this script.
#
# Only enable auto-logon on a physically secured machine (e.g. a locked server / VM).
# =========================================================================

param(
    [string]$User   = $env:USERNAME,
    [string]$Domain = $env:USERDOMAIN,
    [switch]$NoPrompt   # skip the y/N confirmation (used when called from install.ps1)
)

$ErrorActionPreference = "Stop"
$id = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $id.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script in an ELEVATED PowerShell (Run as administrator)."
}

Write-Host "Configure auto-logon for: $Domain\$User" -ForegroundColor Cyan
if (-not $NoPrompt) {
    Write-Warning "Read the security warning at the top of this script before continuing."
    $go = Read-Host "Continue? [y/N]"
    if ($go -ne "y") { Write-Host "Aborted."; return }
}

$sec = Read-Host "Password for $Domain\$User" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

# Prefer Sysinternals Autologon (encrypted LSA secret) if available.
$auto = $null
foreach ($name in @("autologon64.exe","autologon.exe")) {
    $cmd = Get-Command $name -ErrorAction SilentlyContinue
    if ($cmd) { $auto = $cmd.Source; break }
    $local = Join-Path $PSScriptRoot $name
    if (Test-Path $local) { $auto = $local; break }
}

if ($auto) {
    Write-Host "Using Sysinternals Autologon (encrypted): $auto"
    & $auto /accepteula $User $Domain $plain | Out-Null
    Write-Host "Auto-logon configured via Sysinternals Autologon (password stored encrypted)." -ForegroundColor Green
} else {
    Write-Warning "Sysinternals Autologon not found. Falling back to the registry method (PLAINTEXT password)."
    $win = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
    Set-ItemProperty $win "AutoAdminLogon" "1"
    Set-ItemProperty $win "DefaultUserName" $User
    Set-ItemProperty $win "DefaultDomainName" $Domain
    Set-ItemProperty $win "DefaultPassword" $plain
    Write-Host "Auto-logon configured via the registry." -ForegroundColor Green
    Write-Host "To remove later: set AutoAdminLogon=0 and delete DefaultPassword under $win"
}

$plain = $null
Write-Host ""
Write-Host "Reboot to verify the machine logs on automatically and the PLCSIM AutoStart task starts."

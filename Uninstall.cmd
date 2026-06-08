@echo off
title PLCSIM-AutoStart uninstaller
rem Double-click this file to uninstall. It requests administrator rights automatically (UAC),
rem then runs scripts\uninstall.ps1. Your config, logs and PLCSIM workspaces are left untouched.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator permission...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\uninstall.ps1"
echo.
pause

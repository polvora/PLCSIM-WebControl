@echo off
title PLCSIM-AutoStart installer
rem Double-click this file to install. It requests administrator rights automatically (UAC),
rem then runs scripts\install.ps1. No need to open PowerShell or navigate anywhere.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator permission...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1"
echo.
pause

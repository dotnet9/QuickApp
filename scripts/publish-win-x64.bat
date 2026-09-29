@echo off
rem Publish QuickApp for win-x64 (NativeAOT).
rem
rem Usage:
rem   publish-win-x64.bat                 project default version
rem   publish-win-x64.bat 0.2.2           override the version

setlocal EnableExtensions
set "SCRIPT_ROOT=%~dp0"
set "VERSION=%~1"

powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_ROOT%publish_quickapp.ps1" -RuntimeIdentifier win-x64 -Version "%VERSION%"
exit /b %errorlevel%

@echo off
rem Publish QuickApp for one or more runtime identifiers.
rem Windows x64 uses NativeAOT; Linux/macOS use self-contained single-file publish.
rem
rem Usage:
rem   publish.bat                         publish all supported platforms
rem   publish.bat win-x64                 publish one platform
rem   publish.bat "win-x64 linux-x64"     publish several platforms
rem   publish.bat win-x64 0.2.2           override the version

setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

set "SCRIPT_ROOT=%~dp0"
set "ALL_PLATFORMS=win-x64 win-x86 linux-x64 linux-arm64 osx-x64 osx-arm64"
set "PLATFORMS=%~1"
set "VERSION=%~2"

if "%PLATFORMS%"=="" set "PLATFORMS=%ALL_PLATFORMS%"
if /I "%PLATFORMS%"=="all" set "PLATFORMS=%ALL_PLATFORMS%"

set /a FAILED=0
set /a DONE=0

echo ========================================
echo Publish QuickApp: %PLATFORMS%
echo Version: %VERSION% (empty = project default)
echo ========================================

for %%P in (%PLATFORMS%) do (
    echo.
    echo ----------------------------------------
    echo [%%P]
    echo ----------------------------------------

    powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_ROOT%publish_quickapp.ps1" -RuntimeIdentifier %%P -Version "%VERSION%"
    if errorlevel 1 (
        echo [FAIL] %%P
        set /a FAILED+=1
    ) else (
        echo [OK] %%P
        set /a DONE+=1
    )
)

echo.
echo ========================================
echo Done: !DONE! ok, !FAILED! failed
echo Output: %SCRIPT_ROOT%..\artifacts\publish
echo ========================================

if !FAILED! GTR 0 exit /b 1
exit /b 0

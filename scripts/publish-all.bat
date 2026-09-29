@echo off
rem Publish QuickApp for all supported platforms:
rem   win-x64 (NativeAOT) + win-x86 / linux-x64 / linux-arm64 / osx-x64 / osx-arm64 (self-contained single-file)
rem
rem Usage:
rem   publish-all.bat                     project default version
rem   publish-all.bat 0.2.2               override the version

setlocal EnableExtensions EnableDelayedExpansion
set "SCRIPT_ROOT=%~dp0"
set "VERSION=%~1"
set "ALL_PLATFORMS=win-x64 win-x86 linux-x64 linux-arm64 osx-x64 osx-arm64"
set /a FAILED=0
set /a DONE=0

echo ========================================
echo Publish QuickApp: %ALL_PLATFORMS%
echo Version: %VERSION% (empty = project default)
echo ========================================

for %%P in (%ALL_PLATFORMS%) do (
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

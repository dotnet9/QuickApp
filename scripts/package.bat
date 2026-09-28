@echo off
rem Legacy portable ZIP helper for local builds. GitHub Release uses native
rem installers from the platform-specific package scripts instead.
rem
rem Usage:
rem   package.bat                             all platforms
rem   package.bat win-x64                     one platform
rem   package.bat "win-x64 win-x86"           several platforms
rem   package.bat win-x64 0.2.0               override the version
rem   package.bat win-x64 0.2.0 --force       overwrite existing artifacts
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

set "SCRIPT_ROOT=%~dp0"
for %%I in ("%SCRIPT_ROOT%..") do set "REPO_ROOT=%%~fI"

set "PLATFORMS=%~1"
if "%PLATFORMS%"=="" set "PLATFORMS=win-x64 win-x86 linux-x64 linux-arm64"
if /I "%PLATFORMS%"=="all" set "PLATFORMS=win-x64 win-x86 linux-x64 linux-arm64"

set "VERSION=%~2"
set "FORCE="
if /I "%~3"=="--force" set "FORCE=-Force"

call "%SCRIPT_ROOT%publish.bat" "%PLATFORMS%" "%VERSION%"
if errorlevel 1 echo [WARN] Some platforms failed to publish; packaging the rest.

echo.
echo ========================================
echo Packaging
echo ========================================

set /a PACKED=0
for %%P in (%PLATFORMS%) do (
    set "STAGE="
    for /d %%D in ("%REPO_ROOT%\artifacts\publish\%%P\*") do (
        set "STAGE=%%~fD"
        for /d %%A in ("%%~fD\*") do set "STAGE=%%~fA"
    )

    if not defined STAGE (
        echo [SKIP] No publish output for %%P
    ) else (
        powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_ROOT%package_quickapp.ps1" ^
            -SourceDirectory "!STAGE!" ^
            -RuntimeIdentifier "%%P" ^
            -Version "%VERSION%" %FORCE%
        if errorlevel 1 (
            echo [FAIL] package %%P
        ) else (
            set /a PACKED+=1
        )
    )
)

echo.
echo ========================================
echo Packaged !PACKED! platform^(s^)
echo Output: %REPO_ROOT%\artifacts\release
echo ========================================

@echo off
rem Publish QuickApp (NativeAOT).
rem
rem Usage:
rem   publish.bat                       publish all platforms (win-x64 win-x86 linux-x64 linux-arm64)
rem   publish.bat win-x64               publish one platform
rem   publish.bat "win-x64 win-x86"     publish several platforms
rem   publish.bat win-x64 0.2.0         override the version
rem
rem Notes:
rem   * Windows targets (AOT) need MSVC build tools (link.exe) and the Windows SDK.
rem   * Linux targets cannot be linked from Windows: NativeAOT does not cross-link between OSes.
rem     Publish them on a Linux machine or on a GitHub Actions ubuntu runner.
rem   * The app project currently targets net10.0-windows, so Linux targets also need a
rem     multi-targeting + platform-abstraction pass first.
setlocal EnableExtensions EnableDelayedExpansion
chcp 65001 >nul

set "SCRIPT_ROOT=%~dp0"
for %%I in ("%SCRIPT_ROOT%..") do set "REPO_ROOT=%%~fI"

set "ALL_PLATFORMS=win-x64 win-x86 linux-x64 linux-arm64"
set "PLATFORMS=%~1"
set "VERSION=%~2"

if "%PLATFORMS%"=="" set "PLATFORMS=%ALL_PLATFORMS%"
if /I "%PLATFORMS%"=="all" set "PLATFORMS=%ALL_PLATFORMS%"

set "PROJECT=%REPO_ROOT%\src\QuickApp\QuickApp.csproj"
set "PROFILE_DIR=%REPO_ROOT%\src\QuickApp\Properties\PublishProfiles"

if not exist "%PROJECT%" (
    echo [ERROR] Project not found: %PROJECT%
    exit /b 1
)

set "VERSION_ARG="
if not "%VERSION%"=="" set "VERSION_ARG=-p:Version=%VERSION%"

set /a FAILED=0
set /a DONE=0

echo ========================================
echo Publish QuickApp: %PLATFORMS%
echo Version: %VERSION% (empty = Directory.Build.props default)
echo ========================================

for %%P in (%PLATFORMS%) do (
    set "PROFILE=FolderProfile_%%P"
    set "PUBXML=!PROFILE_DIR!\!PROFILE!.pubxml"

    echo.
    echo ----------------------------------------
    echo [%%P]
    echo ----------------------------------------

    if not exist "!PUBXML!" (
        echo [SKIP] Missing publish profile: !PUBXML!
        set /a FAILED+=1
    ) else (
        dotnet publish "%PROJECT%" -c Release -p:PublishProfile=!PROFILE! %VERSION_ARG% --nologo
        if errorlevel 1 (
            echo [FAIL] %%P
            set /a FAILED+=1
        ) else (
            del /s /q "%REPO_ROOT%\artifacts\publish\%%P\*.pdb" >nul 2>nul
            echo [OK] %%P
            set /a DONE+=1
        )
    )
)

echo.
echo ========================================
echo Done: !DONE! ok, !FAILED! failed
echo Output: %REPO_ROOT%\artifacts\publish
echo ========================================

if !FAILED! GTR 0 exit /b 1
exit /b 0

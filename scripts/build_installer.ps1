[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $SourceDirectory = "",
    [string] $OutputDirectory = "",
    [string] $InnoSetupCompiler = "",
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$scriptRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$issPath = Join-Path $scriptRoot "QuickApp.iss"

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $repositoryRoot "artifacts\publish\win-x64\net10.0-windows\QuickApp"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\release"
}

$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) {
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $InnoSetupCompiler = $command.Source
    }
    else {
        $knownPaths = @()
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            $knownPaths += Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $knownPaths += Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"
        }
        $InnoSetupCompiler = $knownPaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw "Inno Setup compiler not found. Install Inno Setup 6 or pass -InnoSetupCompiler."
}

$cleanVersion = $Version.Trim().TrimStart('v', 'V').Split('+')[0]
if ($cleanVersion -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Version must be numeric (for example 0.2.3): $Version"
}

$installerPath = Join-Path $outputPath "QuickApp-v$cleanVersion-win-x64-setup.exe"
if ((Test-Path -LiteralPath $installerPath) -and -not $Force) {
    throw "Artifact already exists: $installerPath (use -Force to overwrite)."
}
if (Test-Path -LiteralPath $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}

Write-Host "Building QuickApp installer from $sourcePath"
$arguments = @(
    $issPath,
    "/DAppVersion=$cleanVersion",
    "/DSourceDir=$sourcePath",
    "/DOutputDir=$outputPath"
)
& $InnoSetupCompiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer was not produced: $installerPath"
}

$shaPath = "$installerPath.sha256"
$sha = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$sha  $(Split-Path -Leaf $installerPath)" | Set-Content -LiteralPath $shaPath -Encoding ASCII

Write-Host "Installer: $installerPath"
Write-Host "Checksum: $shaPath"

[CmdletBinding()]
param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$TargetFramework = "",
    [string]$AssemblyName = "QuickApp",
    [string]$Version = "",
    [string]$SourceDirectory = "",
    [string]$PublishRoot = "",
    [string]$ReleaseRoot = "",
    [switch]$Force
)

# ASCII-only on purpose: Windows PowerShell 5.1 reads .ps1 as ANSI unless the file has a BOM,
# so non-ASCII text here would be mangled. Keep build scripts ASCII (same as xskj-component).

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Join-Path (Join-Path $repoRoot "artifacts") "publish"
}

if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path (Join-Path $repoRoot "artifacts") "release"
}

# Locate the folder to package: use the explicit one, otherwise probe rid/[tfm/]AssemblyName.
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $ridRoot = Join-Path $PublishRoot $RuntimeIdentifier
    if (-not (Test-Path -LiteralPath $ridRoot -PathType Container)) {
        throw "Publish folder not found: $ridRoot. Run publish-all.bat first."
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($TargetFramework)) {
        $candidates += (Join-Path (Join-Path $ridRoot $TargetFramework) $AssemblyName)
    }

    $candidates += (Join-Path $ridRoot $AssemblyName)

    foreach ($sub in Get-ChildItem -LiteralPath $ridRoot -Directory -ErrorAction SilentlyContinue) {
        $candidates += (Join-Path $sub.FullName $AssemblyName)
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $SourceDirectory = $candidate
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($SourceDirectory) -or -not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Publish output folder not found (rid=$RuntimeIdentifier, tfm=$TargetFramework)."
}

$sourceDir = (Resolve-Path -LiteralPath $SourceDirectory).Path
Write-Host "Source: $sourceDir"

if ([string]::IsNullOrWhiteSpace($Version)) {
    foreach ($name in @("$AssemblyName.exe", $AssemblyName)) {
        $binary = Join-Path $sourceDir $name
        if (Test-Path -LiteralPath $binary) {
            $Version = (Get-Item -LiteralPath $binary).VersionInfo.ProductVersion
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "0.0.0"
    }
}

# ProductVersion carries a "+<commit>" suffix from SourceLink; strip it so the zip name stays clean.
$Version = $Version.Split('+')[0].Trim()

$releaseVersion = if ($Version -match "^[vV]") { $Version } else { "v$Version" }
$zipName = "$AssemblyName-$releaseVersion-$RuntimeIdentifier.zip"
$zipPath = Join-Path $ReleaseRoot $zipName

if (-not (Test-Path -LiteralPath $ReleaseRoot -PathType Container)) {
    New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null
}

if ((Test-Path -LiteralPath $zipPath) -and -not $Force) {
    throw "Artifact already exists: $zipPath (use -Force to overwrite)."
}

# Skip pdb files: keeps the archive far smaller.
$files = Get-ChildItem -LiteralPath $sourceDir -Recurse -File |
    Where-Object { $_.Extension -ine ".pdb" }

if ($files.Count -eq 0) {
    throw "Nothing to package in $sourceDir"
}

Write-Host "Packing $($files.Count) files -> $zipPath"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$resolvedSource = $sourceDir.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($resolvedSource.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace([IO.Path]::DirectorySeparatorChar, '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

# Ship a sha256 next to the zip so downloads can be verified.
$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$shaPath = "$zipPath.sha256"
"$sha  $zipName" | Set-Content -LiteralPath $shaPath -Encoding ASCII

$sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
Write-Host "Done: $zipPath ($sizeMb MB)"
Write-Host "Checksum: $shaPath"

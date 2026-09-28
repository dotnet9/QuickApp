[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "linux-arm64")]
    [string] $RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $SourceDirectory = "",
    [string] $OutputDirectory = "",
    [switch] $Force
)

$ErrorActionPreference = "Stop"
$scriptRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $repositoryRoot "artifacts/publish/$RuntimeIdentifier/net10.0/QuickApp"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "artifacts/release"
}

$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$cleanVersion = $Version.Trim().TrimStart('v', 'V').Split('+')[0]
if ($cleanVersion -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Version must be numeric (for example 0.2.3): $Version"
}

$architecture = if ($RuntimeIdentifier -eq "linux-arm64") { "arm64" } else { "amd64" }
$packageName = "QuickApp-v$cleanVersion-$RuntimeIdentifier.deb"
$packagePath = Join-Path $outputPath $packageName
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("QuickApp-deb-" + [Guid]::NewGuid().ToString("N"))
$appPath = Join-Path $stageRoot "usr/lib/quickapp"
$binPath = Join-Path $stageRoot "usr/bin"
$desktopPath = Join-Path $stageRoot "usr/share/applications"
$controlPath = Join-Path $stageRoot "DEBIAN"

if ((Test-Path -LiteralPath $packagePath) -and -not $Force) {
    throw "Artifact already exists: $packagePath (use -Force to overwrite)."
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
New-Item -ItemType Directory -Path $appPath, $binPath, $desktopPath, $controlPath -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $sourcePath -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $appPath -Recurse -Force
    }

    $executable = Join-Path $appPath "QuickApp"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Published executable was not produced: $executable"
    }
    & chmod +x $executable

    @(
        "#!/bin/sh",
        'exec /usr/lib/quickapp/QuickApp "$@"'
    ) | Set-Content -LiteralPath (Join-Path $binPath "quickapp") -Encoding ascii
    & chmod +x (Join-Path $binPath "quickapp")

    @(
        "[Desktop Entry]",
        "Type=Application",
        "Name=QuickApp",
        "Comment=Quick application launcher",
        "Exec=quickapp",
        "Terminal=false",
        "Categories=Utility;"
    ) | Set-Content -LiteralPath (Join-Path $desktopPath "quickapp.desktop") -Encoding ascii

    @(
        "Package: quickapp",
        "Version: $cleanVersion",
        "Section: utils",
        "Priority: optional",
        "Architecture: $architecture",
        "Maintainer: Dotnet9 <1012434131@qq.com>",
        "Depends: libc6, libx11-6, libxrandr2, libxrender1, libxi6, libfontconfig1, libfreetype6, libglib2.0-0",
        "Description: QuickApp quick application launcher",
        " A cross-platform dock for launching applications, URLs and commands."
    ) | Set-Content -LiteralPath (Join-Path $controlPath "control") -Encoding ascii

    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    & dpkg-deb --build --root-owner-group $stageRoot $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "dpkg-deb failed with exit code $LASTEXITCODE."
    }

    $sha = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha  $packageName" | Set-Content -LiteralPath "$packagePath.sha256" -Encoding ascii
    Write-Host "Package: $packagePath"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

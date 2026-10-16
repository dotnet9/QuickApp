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
    Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($sourcePath, $_.FullName)
        if ($_.Extension -in '.pdb', '.dbg' -or $relative -match '(^|[\\/])[^\\/]+\.dSYM([\\/]|$)') { return }
        $destination = Join-Path $appPath $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }

    $executable = Join-Path $appPath "QuickApp"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Published executable was not produced: $executable"
    }
    if ($IsLinux) {
        & chmod 755 $executable
        if ($LASTEXITCODE -ne 0) { throw '无法设置程序启动权限。' }
    }

    [IO.File]::WriteAllText((Join-Path $binPath 'quickapp'), "#!/bin/sh`n" + 'exec /usr/lib/quickapp/QuickApp "$@"' + "`n")
    if ($IsLinux) {
        & chmod 755 (Join-Path $binPath 'quickapp')
        if ($LASTEXITCODE -ne 0) { throw '无法设置启动入口权限。' }
    }

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
    # 补齐独立安装包的图标、品牌和软件管理器元数据。
    & (Join-Path $scriptRoot 'installer/linux/Write-LinuxPackageMetadata.ps1') -StagingDirectory $stageRoot -RepositoryRoot $repositoryRoot -Version $cleanVersion
    & dpkg-deb --build --root-owner-group $stageRoot $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "dpkg-deb failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw "安装包未生成：$packagePath" }
    $sha = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha  $packageName" | Set-Content -LiteralPath "$packagePath.sha256" -Encoding ascii
    Write-Host "Package: $packagePath"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("osx-x64", "osx-arm64")]
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

$dmgName = "QuickApp-v$cleanVersion-$RuntimeIdentifier.dmg"
$dmgPath = Join-Path $outputPath $dmgName
$pkgName = "QuickApp-v$cleanVersion-$RuntimeIdentifier.pkg"
$pkgPath = Join-Path $outputPath $pkgName
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("QuickApp-dmg-" + [Guid]::NewGuid().ToString("N"))
$appRoot = Join-Path $stageRoot "QuickApp.app"
$contentsPath = Join-Path $appRoot "Contents"
$macosPath = Join-Path $contentsPath "MacOS"

if (((Test-Path -LiteralPath $dmgPath) -or (Test-Path -LiteralPath $pkgPath)) -and -not $Force) {
    throw "Artifact already exists for $RuntimeIdentifier (use -Force to overwrite)."
}
New-Item -ItemType Directory -Path $outputPath, $macosPath -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $sourcePath -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $macosPath -Recurse -Force
    }

    $executable = Join-Path $macosPath "QuickApp"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Published executable was not produced: $executable"
    }
    & chmod +x $executable

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key><string>QuickApp</string>
  <key>CFBundleExecutable</key><string>QuickApp</string>
  <key>CFBundleIdentifier</key><string>com.dotnet9.quickapp</string>
  <key>CFBundleName</key><string>QuickApp</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$cleanVersion</string>
  <key>CFBundleVersion</key><string>$cleanVersion</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
"@
    $plist | Set-Content -LiteralPath (Join-Path $contentsPath "Info.plist") -Encoding utf8

    if (Test-Path -LiteralPath $pkgPath) {
        Remove-Item -LiteralPath $pkgPath -Force
    }
    & pkgbuild --component $appRoot --install-location "/Applications" --identifier "com.dotnet9.quickapp" --version $cleanVersion $pkgPath
    if ($LASTEXITCODE -ne 0) {
        throw "pkgbuild failed with exit code $LASTEXITCODE."
    }

    $pkgSha = (Get-FileHash -LiteralPath $pkgPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$pkgSha  $pkgName" | Set-Content -LiteralPath "$pkgPath.sha256" -Encoding ascii

    & ln -s "/Applications" (Join-Path $stageRoot "Applications")

    if (Test-Path -LiteralPath $dmgPath) {
        Remove-Item -LiteralPath $dmgPath -Force
    }
    & hdiutil create -volname "QuickApp" -srcfolder $stageRoot -ov -format UDZO $dmgPath
    if ($LASTEXITCODE -ne 0) {
        throw "hdiutil failed with exit code $LASTEXITCODE."
    }

    $sha = (Get-FileHash -LiteralPath $dmgPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha  $dmgName" | Set-Content -LiteralPath "$dmgPath.sha256" -Encoding ascii
    Write-Host "Package: $dmgPath"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

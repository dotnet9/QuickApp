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

<#
.SYNOPSIS
    由 PNG 生成 macOS 的 .icns 图标并放进 Contents/Resources。
.DESCRIPTION
    iconutil 要求一套固定尺寸的 PNG（16~1024 的 1x/2x 组合），缺一个就报错，
    所以先用 sips 逐个缩放。iconutil 不可用时（旧系统或精简环境）退化为直接拷源 PNG，
    至少让启动台有个图标而不是通用占位图。注意：函数必须定义在主流程之前，
    PowerShell 脚本自上而下执行，定义在文件末尾会导致首次调用报「无法识别」。
#>
function BuildIcns {
    param(
        [Parameter(Mandatory = $true)][string] $SourcePng,
        [Parameter(Mandatory = $true)][string] $ResourcesPath
    )

    $icnsPath = Join-Path $ResourcesPath "QuickApp.icns"
    $iconSet = Join-Path ([System.IO.Path]::GetTempPath()) ("QuickApp-icon-" + [Guid]::NewGuid().ToString("N") + ".iconset")
    New-Item -ItemType Directory -Path $iconSet -Force | Out-Null

    try {
        # iconset 要求的文件名：<逻辑尺寸>_[_@2x].png
        $entries = @(
            @{ Name = "icon_16x16.png"; Size = 16 },
            @{ Name = "icon_16x16@2x.png"; Size = 32 },
            @{ Name = "icon_32x32.png"; Size = 32 },
            @{ Name = "icon_32x32@2x.png"; Size = 64 },
            @{ Name = "icon_128x128.png"; Size = 128 },
            @{ Name = "icon_128x128@2x.png"; Size = 256 },
            @{ Name = "icon_256x256.png"; Size = 256 },
            @{ Name = "icon_256x256@2x.png"; Size = 512 },
            @{ Name = "icon_512x512.png"; Size = 512 },
            @{ Name = "icon_512x512@2x.png"; Size = 1024 }
        )

        foreach ($entry in $entries) {
            $target = Join-Path $iconSet $entry.Name
            & sips -s format png -z $entry.Size $entry.Size $SourcePng --out $target | Out-Null
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $target)) {
                throw "sips failed to render $($entry.Name)."
            }
        }

        & iconutil -c icns $iconSet -o $icnsPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $icnsPath)) {
            Remove-Item -LiteralPath $icnsPath -Force -ErrorAction SilentlyContinue
            Write-Warning "iconutil unavailable; falling back to a plain PNG icon."
            Copy-Item -LiteralPath $SourcePng -Destination (Join-Path $ResourcesPath "QuickApp.png") -Force
            return (Join-Path $ResourcesPath "QuickApp.png")
        }

        return $icnsPath
    }
    finally {
        if (Test-Path -LiteralPath $iconSet) {
            Remove-Item -LiteralPath $iconSet -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

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
$resourcesPath = Join-Path $contentsPath "Resources"
$iconSource = Join-Path $repositoryRoot "logo.png"

if (((Test-Path -LiteralPath $dmgPath) -or (Test-Path -LiteralPath $pkgPath)) -and -not $Force) {
    throw "Artifact already exists for $RuntimeIdentifier (use -Force to overwrite)."
}
New-Item -ItemType Directory -Path $outputPath, $macosPath, $resourcesPath -Force | Out-Null

try {
    Get-ChildItem -LiteralPath $sourcePath -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $macosPath -Recurse -Force
    }

    $executable = Join-Path $macosPath "QuickApp"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Published executable was not produced: $executable"
    }
    & chmod +x $executable

    # 图标：logo.png -> iconset -> .icns。没有 CFBundleIconFile 时启动台会显示通用占位图标，
    # 用户会以为装坏了，所以这一步不能省。
    if (-not (Test-Path -LiteralPath $iconSource -PathType Leaf)) {
        throw "Icon source not found: $iconSource"
    }
    $iconPath = BuildIcns -SourcePng $iconSource -ResourcesPath $resourcesPath
    Write-Host "Icon: $iconPath"

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key><string>QuickApp</string>
  <key>CFBundleExecutable</key><string>QuickApp</string>
  <key>CFBundleIconFile</key><string>QuickApp</string>
  <key>CFBundleIdentifier</key><string>com.dotnet9.quickapp</string>
  <key>CFBundleName</key><string>QuickApp</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$cleanVersion</string>
  <key>CFBundleVersion</key><string>$cleanVersion</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSSupportsAutomaticGraphicsSwitching</key><true/>
  <key>NSHumanReadableCopyright</key><string>QuickApp</string>
</dict>
</plist>
"@
    $plist | Set-Content -LiteralPath (Join-Path $contentsPath "Info.plist") -Encoding utf8

    # PkgInfo：老式 bundle 标识，缺了部分系统工具识别不了这是应用包
    "APPL????" | Set-Content -LiteralPath (Join-Path $contentsPath "PkgInfo") -Encoding ascii

    # ad-hoc 签名：未签名的 app 在新系统上首次打开会被 Gatekeeper 拦，
    # ad-hoc 至少让系统认它是个完整应用包（后续换 Developer ID 只需替换这一步）。
    & codesign --force --sign - --timestamp=none $appRoot
    if ($LASTEXITCODE -ne 0) {
        throw "codesign failed with exit code $LASTEXITCODE."
    }

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
    # DMF 根目录只放 .app 与 Applications 软链；图标已烤进 .app/Contents/Resources
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


param([string]$TemporaryDirectory = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$product = 'QuickApp'
$version = '1.2.3'
$verifier = Join-Path (Split-Path -Parent $PSScriptRoot) 'verify_release_installers.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetFullPath($TemporaryDirectory)) ('quickapp-installers-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$names = @("$product-v$version-win-x64-setup.exe", "$product-v$version-linux-x64.deb", "$product-v$version-linux-arm64.deb", "$product-v$version-osx-x64.dmg", "$product-v$version-osx-arm64.dmg")
$passed = 0
function Write-Checksum([string]$Path) {
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($Path))" | Set-Content -LiteralPath ($Path + '.sha256') -Encoding ascii
}
function Rename-Installer([string]$Folder, [string]$OldName, [string]$NewName) {
    $path = Join-Path $Folder $OldName
    Move-Item -LiteralPath $path -Destination (Join-Path $Folder $NewName)
    Remove-Item -LiteralPath ($path + '.sha256')
    Write-Checksum (Join-Path $Folder $NewName)
}
function Check-Case([string]$Name, [bool]$Accepted, [scriptblock]$Change, [string]$RequestedVersion = $version) {
    $folder = Join-Path $fixtureRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder | Out-Null
    foreach ($installerName in $names) {
        $path = Join-Path $folder $installerName
        [IO.File]::WriteAllText($path, 'isolated installer fixture')
        Write-Checksum $path
    }
    & $Change $folder
    $actual = $true
    try { & $verifier -Version $RequestedVersion -PackageDirectory $folder *> $null } catch { $actual = $false }
    if ($actual -ne $Accepted) { throw "安装包校验场景失败：$Name，预期接受=$Accepted，实际=$actual" }
    $script:passed++
    Write-Output "PASS $Name"
}
try {
    Check-Case '五平台正确名称与真实哈希' $true {}
    Check-Case '带 v 的精确版本参数' $true {} 'v1.2.3'
    Check-Case '拒绝其他产品' $false { param($folder) Rename-Installer $folder $names[0] "Other-v$version-win-x64-setup.exe" }
    Check-Case '拒绝版本追加第四段' $false { param($folder) Rename-Installer $folder $names[0] "$product-v$version.4-win-x64-setup.exe" }
    Check-Case '拒绝版本增加前缀段' $false { param($folder) Rename-Installer $folder $names[0] "$product-v0.$version-win-x64-setup.exe" }
    Check-Case '拒绝预发布后缀' $false { param($folder) Rename-Installer $folder $names[0] "$product-v$version-preview-win-x64-setup.exe" }
    Check-Case '拒绝错误架构' $false { param($folder) Rename-Installer $folder $names[0] "$product-v$version-win-arm64-setup.exe" }
    Check-Case '拒绝缺失校验文件' $false { param($folder) Remove-Item -LiteralPath (Join-Path $folder ($names[0] + '.sha256')) }
    Check-Case '拒绝不匹配哈希' $false { param($folder) [IO.File]::AppendAllText((Join-Path $folder $names[0]), 'changed') }
    Check-Case '拒绝校验目标名称错误' $false { param($folder) $path = Join-Path $folder ($names[0] + '.sha256'); (('0' * 64) + '  other.exe') | Set-Content -LiteralPath $path -Encoding ascii }
    Check-Case '拒绝空安装包' $false { param($folder) $path = Join-Path $folder $names[0]; [IO.File]::WriteAllBytes($path, [byte[]]@()); Write-Checksum $path }
    Check-Case '拒绝额外压缩包' $false { param($folder) [IO.File]::WriteAllText((Join-Path $folder 'portable.zip'), 'extra') }
    Check-Case '拒绝重复 v 参数' $false {} 'vv1.2.3'
    Write-Output "安装包门禁通过：$passed 个场景。"
} finally {
    # 只删除本测试在指定临时目录下创建的随机隔离目录。
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

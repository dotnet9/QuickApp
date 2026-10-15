param([switch] $Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$design = Join-Path $root 'design'
$versionMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props')), '<VersionPrefix\b[^>]*>(?<version>\d+\.\d+\.\d+)</VersionPrefix>')
if (-not $versionMatch.Success) { throw '未找到产品版本号。' }
$version = $versionMatch.Groups['version'].Value
$parsedVersion = [version]$version
$updateVersion = '{0}.{1}.{2}' -f $parsedVersion.Major, $parsedVersion.Minor, ($parsedVersion.Build + 1)
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'prototype-source/index.template.html'))
$source = [regex]::Replace($source, '<link rel="stylesheet" href="([^"]+)">', {
    param($match)
    '<style>' + [IO.File]::ReadAllText((Join-Path $design $match.Groups[1].Value)) + '</style>'
})
$source = [regex]::Replace($source, '<script src="([^"]+)"></script>', {
    param($match)
    '<script>' + [IO.File]::ReadAllText((Join-Path $design $match.Groups[1].Value)).Replace('</script', '<\/script') + '</script>'
})
foreach ($asset in '../logo.svg','assets/vex-logo.svg') {
    $assetPath = [IO.Path]::GetFullPath((Join-Path $design $asset))
    $svg = [IO.File]::ReadAllText($assetPath).ReplaceLineEndings("`n")
    $uri = 'data:image/svg+xml;base64,' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($svg))
    $source = $source.Replace($asset, $uri)
}
$source = $source.Replace('__APP_VERSION__', $version).Replace('__UPDATE_VERSION__', $updateVersion)
$target = Join-Path $design 'index.html'
if ($Check) {
    if ([IO.File]::ReadAllText($target).ReplaceLineEndings("`n") -cne $source.ReplaceLineEndings("`n")) { throw '原型入口需要重新生成。' }
} else { [IO.File]::WriteAllText($target,$source,[Text.UTF8Encoding]::new($false)) }
Write-Host 'QuickApp 单文件原型已验证。'

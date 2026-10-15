param([switch] $Check)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$design = Join-Path $root 'design'
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
    $uri = 'data:image/svg+xml;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($assetPath))
    $source = $source.Replace($asset, $uri)
}
$target = Join-Path $design 'index.html'
if ($Check) {
    if ([IO.File]::ReadAllText($target).ReplaceLineEndings("`n") -cne $source.ReplaceLineEndings("`n")) { throw '原型入口需要重新生成。' }
} else { [IO.File]::WriteAllText($target,$source,[Text.UTF8Encoding]::new($false)) }
Write-Host 'QuickApp 单文件原型已验证。'

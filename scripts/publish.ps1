# 统一发布入口：scripts/publish.ps1
# 用法：pwsh scripts/publish.ps1 [-RuntimeIdentifier win-x64] [-Version 0.0.0]
# 输出：artifacts/publish/<rid>/<AppName>/
[CmdletBinding()]
param(
    [string] $RuntimeIdentifier = "win-x64",
    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = Join-Path $repositoryRoot "Directory.Build.props"
    if (Test-Path -LiteralPath $props) {
        [xml] $properties = Get-Content -LiteralPath $props -Raw
        $versionNode = $properties.SelectSingleNode('/Project/PropertyGroup/Version | /Project/PropertyGroup/VersionPrefix')
        if ($versionNode) { $Version = $versionNode.InnerText.Trim() }
    }
}
Write-Host "发布 $RuntimeIdentifier (Version=$Version)"

& (Join-Path $PSScriptRoot "publish_quickapp.ps1") -RuntimeIdentifier $RuntimeIdentifier -Version $Version
exit $LASTEXITCODE

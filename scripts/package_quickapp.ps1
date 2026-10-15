[CmdletBinding()]
param(
    [ValidateSet("win-x64","linux-x64","linux-arm64","osx-x64","osx-arm64")]
    [string] $RuntimeIdentifier = "win-x64",
    [string] $TargetFramework = "",
    [string] $AssemblyName = "QuickApp",
    [string] $Version = "",
    [string] $SourceDirectory = "",
    [string] $PublishRoot = "",
    [string] $ReleaseRoot = "",
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $props = Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props')
    $Version = $props.Project.PropertyGroup.VersionPrefix | Where-Object { $_ } | Select-Object -First 1
}
$arguments = @{ Version=$Version; Force=$Force }
if ($SourceDirectory) { $arguments.SourceDirectory=$SourceDirectory }
if ($ReleaseRoot) { $arguments.OutputDirectory=$ReleaseRoot }
switch -Wildcard ($RuntimeIdentifier) {
    'win-*' { & (Join-Path $PSScriptRoot 'build_installer.ps1') @arguments }
    'linux-*' { & (Join-Path $PSScriptRoot 'package_linux_deb.ps1') -RuntimeIdentifier $RuntimeIdentifier @arguments }
    'osx-*' { & (Join-Path $PSScriptRoot 'package_macos_dmg.ps1') -RuntimeIdentifier $RuntimeIdentifier @arguments }
}

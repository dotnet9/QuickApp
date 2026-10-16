$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('quickapp-publish-entry-' + [Guid]::NewGuid().ToString('N'))
$encoding = [Text.UTF8Encoding]::new($false)
$shell = (Get-Process -Id $PID).Path
try {
    $scripts = Join-Path $fixture 'scripts'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts/publish.ps1') -Destination $scripts
    # 只记录真实入口传来的参数，不启动 dotnet，不修改实际发布目录。
    [IO.File]::WriteAllText((Join-Path $scripts 'publish_quickapp.ps1'), @'
param([string] $RuntimeIdentifier, [string] $Version)
@{ runtime = $RuntimeIdentifier; version = $Version } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '../invocation.json')
exit 23
'@, $encoding)
    $cases = @(
        @{ name = '条件 VersionPrefix'; property = '<VersionPrefix Condition="condition">2.3.4</VersionPrefix>'; version = '2.3.4'; runtime = 'win-x64' },
        @{ name = 'Version'; property = '<Version>3.4.5</Version>'; version = '3.4.5'; runtime = 'win-x64' },
        @{ name = '版本空白与 RID'; property = '<VersionPrefix Condition="condition"> 4.5.6 </VersionPrefix>'; version = '4.5.6'; runtime = 'linux-arm64' },
        @{ name = '显式版本覆盖'; property = 'not XML'; version = '7.8.9'; runtime = 'osx-arm64'; explicit = $true }
    )
    foreach ($case in $cases) {
        $props = if ($case.explicit) { $case.property } else { '<Project><PropertyGroup>' + $case.property + '</PropertyGroup></Project>' }
        [IO.File]::WriteAllText((Join-Path $fixture 'Directory.Build.props'), $props, $encoding)
        $arguments = @('-NoProfile', '-File', (Join-Path $scripts 'publish.ps1'), '-RuntimeIdentifier', $case.runtime)
        if ($case.explicit) { $arguments += @('-Version', $case.version) }
        & $shell @arguments
        if ($LASTEXITCODE -ne 23) { throw "未传递发布脚本退出码：$($case.name)" }
        $actual = Get-Content -LiteralPath (Join-Path $fixture 'invocation.json') -Raw | ConvertFrom-Json
        if ($actual.version -cne $case.version -or $actual.runtime -cne $case.runtime) {
            throw "发布参数错误：$($case.name)；实际为 $($actual | ConvertTo-Json -Compress)"
        }
        Write-Host "PASS $($case.name)"
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    $temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temporary, [StringComparison]::OrdinalIgnoreCase)) { throw '拒绝清理临时目录之外的文件。' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

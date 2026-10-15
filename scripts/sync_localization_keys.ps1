param([switch] $Check)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'T4 tool restore failed.' }
    $template = Join-Path $repositoryRoot 'src/QuickApp.Core/I18n/Language.tt'
    $output = Join-Path $repositoryRoot 'src/QuickApp.Core/I18n/Language.cs'
    $generatedOutput = Join-Path ([IO.Path]::GetTempPath()) ('quickapp-t4-' + [Guid]::NewGuid().ToString('N') + '.cs')
    try {
        dotnet tool run t4 -- $template -o $generatedOutput
        if ($LASTEXITCODE -ne 0) { throw 'T4 transformation failed.' }
        $generated = [IO.File]::ReadAllText($generatedOutput).ReplaceLineEndings("`n")
        if ($Check) {
            if ([IO.File]::ReadAllText($output).ReplaceLineEndings("`n") -cne $generated) { throw 'Stale T4 keys. Run scripts/sync_localization_keys.ps1.' }
        } else { [IO.File]::WriteAllText($output, $generated, [Text.UTF8Encoding]::new($false)) }
    } finally { if (Test-Path -LiteralPath $generatedOutput) { Remove-Item -LiteralPath $generatedOutput -Force } }

    $translations = [ordered]@{}
    foreach ($culture in 'zh-CN','en-US') {
        $translations[$culture] = [IO.File]::ReadAllText((Join-Path $repositoryRoot "src/QuickApp.Core/I18n/$culture.json")) | ConvertFrom-Json -AsHashtable
    }
    $resourceScript = '// 根据应用 JSON 生成；运行 scripts/sync_localization_keys.ps1 更新。' + "`nwindow.QUICKAPP_RESOURCES = " + ($translations | ConvertTo-Json -Depth 20 -Compress) + ";`n"
    $resourceOutput = Join-Path $repositoryRoot 'design/assets/resources.js'
    if ($Check) {
        if (-not (Test-Path -LiteralPath $resourceOutput) -or [IO.File]::ReadAllText($resourceOutput).ReplaceLineEndings("`n") -cne $resourceScript) { throw 'Stale prototype resources.' }
    } else { [IO.File]::WriteAllText($resourceOutput, $resourceScript, [Text.UTF8Encoding]::new($false)) }
    Write-Host 'QuickApp T4 and prototype resources verified.'
} finally { Pop-Location }

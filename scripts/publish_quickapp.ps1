[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-x64", "win-x86", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string] $RuntimeIdentifier,

    [string] $Version = ""
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path (Join-Path $repositoryRoot "src") "QuickApp/QuickApp.csproj"
$publishRoot = Join-Path (Join-Path $repositoryRoot "artifacts") "publish"
$targetFramework = if ($RuntimeIdentifier.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
    "net10.0-windows"
}
else {
    "net10.0"
}

# 全平台 NativeAOT：完整反射元数据保全（源生成 JSON / 运行时 JSON 配置），单线程 ILC 更稳
$useAot = $true
$outputPath = Join-Path (Join-Path (Join-Path $publishRoot $RuntimeIdentifier) $targetFramework) "QuickApp"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "0.0.0"
}

if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}

$publishArguments = @(
    "publish", $projectPath,
    "-c", "Release",
    "-f", $targetFramework,
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-o", $outputPath,
    "-p:Version=$Version",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:PublishTrimmed=true",
    "-p:PublishSingleFile=false",
    "-p:PublishAot=true"
)

if ($useAot) {
    # 交叉编译（linux-arm64）与 Apple ld_classic 不支持 -gz=zlib（压缩调试段）：这些平台保留符号
    $stripSymbols = $RuntimeIdentifier -notin @("linux-arm64", "osx-x64", "osx-arm64")
    $publishArguments += @(
        "-p:StripSymbols=$($stripSymbols.ToString().ToLowerInvariant())",
        "-p:IlcSingleThreaded=true",
        "-p:IlcGenerateCompleteTypeMetadata=true",
        "-p:IlcTrimMetadata=false",
        "-p:TreatWarningsAsErrors=false",
        "-p:ILLinkTreatWarningsAsErrors=false"
    )
}

Write-Host "Publishing QuickApp for $RuntimeIdentifier with $targetFramework..."
& dotnet @publishArguments
$code = $LASTEXITCODE
if ($code -ne 0) {
    throw "QuickApp publish failed for $RuntimeIdentifier with exit code $code."
}

Get-ChildItem -LiteralPath $outputPath -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in ".pdb", ".dbg" } |
    Remove-Item -Force

$executableName = if ($RuntimeIdentifier.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
    "QuickApp.exe"
}
else {
    "QuickApp"
}
$executablePath = Join-Path $outputPath $executableName
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable was not produced: $executablePath"
}

Write-Host "Published QuickApp to $outputPath"

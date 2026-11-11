param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

dotnet publish .\GitCount.csproj `
    -c $Configuration `
    -p:PublishProfile=win-x64 `
    -p:NuGetAudit=false

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exePath = Join-Path $projectRoot "dist\win-x64\GitCount.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "发布失败：未找到 $exePath"
}

Write-Host ""
Write-Host "发布完成：$exePath" -ForegroundColor Green
Write-Host "运行前请确保已安装 Git for Windows 和 .NET 10 Desktop Runtime。"

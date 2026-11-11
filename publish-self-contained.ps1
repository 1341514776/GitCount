param(
    [string]$Configuration = "Release",
    [string]$NuGetSource = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $projectRoot

dotnet publish .\GitCount.csproj `
    -c $Configuration `
    -p:PublishProfile=win-x64-self-contained `
    -p:NuGetAudit=false `
    -p:RestoreSources=$NuGetSource

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exePath = Join-Path $projectRoot "dist\win-x64-self-contained\GitCount.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Publish failed: $exePath was not found."
}

Write-Host ""
Write-Host "Self-contained publish complete: $exePath" -ForegroundColor Green
Write-Host "This exe includes the .NET runtime. Target PCs only need Git for Windows."

[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Run,
    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"
$winformRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseRoot = Join-Path $winformRoot "Release"
$outputDirectory = Join-Path $releaseRoot "local"
$desktopProject = Join-Path $winformRoot "CSI.OpenBase.Desktop\CSI.OpenBase.Desktop.csproj"
$desktopExecutable = Join-Path $outputDirectory "CSI.OpenBase.Desktop.exe"

if ($PlanOnly) {
    [ordered]@{
        mode = "local-testing"
        configuration = $Configuration
        outputDirectory = $outputDirectory
        executable = $desktopExecutable
        freezesBackend = $false
        bundlesChromium = $false
        bumpsVersion = $false
    } | ConvertTo-Json
    return
}

$resolvedReleaseRoot = [System.IO.Path]::GetFullPath($releaseRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($outputDirectory)
$expectedOutput = [System.IO.Path]::GetFullPath(
    (Join-Path $resolvedReleaseRoot "local")
)
if (-not $resolvedOutput.Equals(
    $expectedOutput,
    [System.StringComparison]::OrdinalIgnoreCase
)) {
    throw "Refusing to replace an unexpected local output directory: $resolvedOutput"
}

if (Test-Path -LiteralPath $resolvedOutput) {
    $item = Get-Item -LiteralPath $resolvedOutput -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to replace a local output reparse point: $resolvedOutput"
    }
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

& dotnet publish $desktopProject `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -o $resolvedOutput
if ($LASTEXITCODE -ne 0) {
    throw "Local WinForms publish failed"
}
if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
    throw "Local publish did not create the desktop executable: $desktopExecutable"
}

Write-Host "Local test build ready: $desktopExecutable"
Write-Host "Backend: adjacent Python source or CSI_OPENBASE_BACKEND"
Write-Host "Playwright Chromium was not downloaded or bundled"

if ($Run) {
    Start-Process -FilePath $desktopExecutable -WorkingDirectory $outputDirectory
}

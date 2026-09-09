[CmdletBinding(DefaultParameterSetName = "Source")]
param(
    [Parameter(ParameterSetName = "Source")]
    [string]$PythonSource,

    [Parameter(Mandatory = $true, ParameterSetName = "Wheel")]
    [string]$PythonWheel,

    [string]$BootstrapPython = "python",
    [string]$Configuration = "Release",
    [switch]$SkipInstaller,
    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"
$runtime = "win-x64"
$winformRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$versionFile = Join-Path $winformRoot "VERSION"
if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "Missing release version file: $versionFile"
}
$versionText = [System.IO.File]::ReadAllText($versionFile)
$versionPattern = '\A(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.[1-9][0-9])(?:\r?\n)?\z'
if ($versionText -notmatch $versionPattern) {
    throw "VERSION must contain exactly x.x.xx with a patch from 10 through 99"
}
$releaseVersion = $Matches["version"]
$releasesRoot = Join-Path $winformRoot "Releases"
$releaseDirectory = Join-Path $releasesRoot "winform.$releaseVersion"
$outputRoot = Join-Path $releaseDirectory "portable"
$installerOutput = Join-Path $releaseDirectory "installer"
$portableArchive = Join-Path $releaseDirectory "CSI-OpenBase-$releaseVersion-win-x64-portable.zip"
$portableChecksum = "$portableArchive.sha256"
$backendDist = Join-Path $winformRoot "build\pyinstaller-dist"
$backendWork = Join-Path $winformRoot "build\pyinstaller-work"
$pythonEnvironment = Join-Path $winformRoot "build\python-env"
$playwrightCache = Join-Path $winformRoot "build\playwright-browsers"
$playwrightBundle = Join-Path $winformRoot "build\playwright-bundle"
$webViewDirectory = Join-Path $winformRoot "build\webview2"
$webViewBootstrapper = Join-Path $webViewDirectory "MicrosoftEdgeWebView2Setup.exe"
$webViewBootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
$desktopProject = Join-Path $winformRoot "CSI.OpenBase.Desktop\CSI.OpenBase.Desktop.csproj"
$specPath = Join-Path $winformRoot "packaging\openbase_backend.spec"
$buildRequirements = Join-Path $winformRoot "packaging\requirements-build.txt"
$installerScript = Join-Path $winformRoot "packaging\CSI.OpenBase.iss"

if ($PlanOnly) {
    [ordered]@{
        version = $releaseVersion
        releaseDirectory = $releaseDirectory
        portableDirectory = $outputRoot
        installerDirectory = $installerOutput
        portableArchive = $portableArchive
        portableChecksum = $portableChecksum
    } | ConvertTo-Json
    return
}

function Assert-SafeProjectPath {
    param([Parameter(Mandatory = $true)][string]$Candidate)

    $fullPath = [System.IO.Path]::GetFullPath($Candidate)
    $rootPrefix = $winformRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the WinForms project: $fullPath"
    }

    $relativePath = $fullPath.Substring($rootPrefix.Length)
    $currentPath = $winformRoot
    foreach ($part in ($relativePath -split "[\\/]")) {
        if (-not $part) { continue }
        $currentPath = Join-Path $currentPath $part
        if (-not (Test-Path -LiteralPath $currentPath)) { continue }
        $item = Get-Item -LiteralPath $currentPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to clean through a reparse point: $currentPath"
        }
    }
    return $fullPath
}

$installerCompiler = $null
if (-not $SkipInstaller) {
    $installerCompiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $installerCompiler) {
        throw "ISCC.exe was not found. Install Inno Setup or pass -SkipInstaller for a portable-only build."
    }
}

if ($PSCmdlet.ParameterSetName -eq "Wheel") {
    $pythonInput = (Resolve-Path -LiteralPath $PythonWheel -ErrorAction Stop).Path
    if ([System.IO.Path]::GetExtension($pythonInput) -ne ".whl") {
        throw "PythonWheel must point to a .whl file: $pythonInput"
    }
    $pythonInputDescription = "wheel $pythonInput"
} else {
    if ([string]::IsNullOrWhiteSpace($PythonSource)) {
        $PythonSource = Join-Path $winformRoot "..\python"
    }
    $pythonInput = (Resolve-Path -LiteralPath $PythonSource -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath (Join-Path $pythonInput "pyproject.toml") -PathType Leaf)) {
        throw "PythonSource must contain pyproject.toml: $pythonInput"
    }
    $pythonInputDescription = "source $pythonInput"
}

$bootstrapCommand = Get-Command $BootstrapPython -ErrorAction Stop | Select-Object -First 1
$bootstrapExecutable = $bootstrapCommand.Source
if (-not $bootstrapExecutable) {
    $bootstrapExecutable = $bootstrapCommand.Path
}
if (-not $bootstrapExecutable) {
    throw "Could not resolve the bootstrap Python executable: $BootstrapPython"
}

$cleanPaths = @(
    $releaseDirectory,
    $backendDist,
    $backendWork,
    $pythonEnvironment,
    $playwrightBundle
)
foreach ($path in $cleanPaths) {
    $fullPath = Assert-SafeProjectPath -Candidate $path
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $playwrightCache -Force | Out-Null
New-Item -ItemType Directory -Path $playwrightBundle -Force | Out-Null
New-Item -ItemType Directory -Path $webViewDirectory -Force | Out-Null

Write-Host "Creating an isolated Python build environment from $pythonInputDescription..."
& $bootstrapExecutable -m venv $pythonEnvironment
if ($LASTEXITCODE -ne 0) { throw "Could not create the Python build environment" }
$pythonExecutable = Join-Path $pythonEnvironment "Scripts\python.exe"
if (-not (Test-Path -LiteralPath $pythonExecutable -PathType Leaf)) {
    throw "The Python build environment did not create $pythonExecutable"
}
$pythonPointerWidth = (& $pythonExecutable -c "import struct; print(struct.calcsize('P') * 8)" |
    Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $pythonPointerWidth -ne "64") {
    throw "The win-x64 build requires a 64-bit Python interpreter"
}

& $pythonExecutable -m pip install --disable-pip-version-check --no-input -r $buildRequirements
if ($LASTEXITCODE -ne 0) { throw "Could not install Windows build dependencies" }
& $pythonExecutable -m pip install --disable-pip-version-check --no-input $pythonInput
if ($LASTEXITCODE -ne 0) { throw "Could not install CSI OpenBase from $pythonInput" }

$pythonPackageVersion = & $pythonExecutable -c `
    "import importlib.metadata; print(importlib.metadata.version('csi-openbase'))"
if ($LASTEXITCODE -ne 0 -or -not $pythonPackageVersion) {
    throw "The supplied Python project did not install the csi-openbase distribution"
}
Write-Host "Bundling csi-openbase $($pythonPackageVersion.Trim())"

if (-not (Test-Path -LiteralPath $webViewBootstrapper)) {
    Write-Host "Downloading the Microsoft Edge WebView2 Evergreen bootstrapper..."
    Invoke-WebRequest -UseBasicParsing -Uri $webViewBootstrapperUrl -OutFile $webViewBootstrapper
}
$webViewSignature = Get-AuthenticodeSignature -LiteralPath $webViewBootstrapper
if (
    $webViewSignature.Status -ne "Valid" -or
    $null -eq $webViewSignature.SignerCertificate -or
    $webViewSignature.SignerCertificate.Subject -notmatch "O=Microsoft Corporation"
) {
    throw "WebView2 bootstrapper signature validation failed: $($webViewSignature.Status)"
}

$env:PLAYWRIGHT_BROWSERS_PATH = $playwrightCache
& $pythonExecutable -m playwright install chromium --no-shell
if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed" }

$playwrightDescriptor = (& $pythonExecutable -c `
    "import pathlib, playwright; print(pathlib.Path(playwright.__file__).parent / 'driver' / 'package' / 'browsers.json')" |
    Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or -not $playwrightDescriptor) {
    throw "Could not locate Playwright's browser descriptor"
}
$browserDefinitions = (Get-Content -LiteralPath $playwrightDescriptor -Raw | ConvertFrom-Json).browsers
foreach ($browserName in @("chromium", "ffmpeg")) {
    $definition = $browserDefinitions | Where-Object { $_.name -eq $browserName } | Select-Object -First 1
    if ($null -eq $definition) {
        throw "Playwright descriptor does not define $browserName"
    }
    $directoryName = "$($definition.name)-$($definition.revision)"
    $source = Join-Path $playwrightCache $directoryName
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Playwright did not install required browser resource: $source"
    }
    Copy-Item -LiteralPath $source -Destination $playwrightBundle -Recurse -Force
}
$winLdd = $browserDefinitions | Where-Object { $_.name -eq "winldd" } | Select-Object -First 1
if ($null -ne $winLdd) {
    $winLddSource = Join-Path $playwrightCache "$($winLdd.name)-$($winLdd.revision)"
    if (Test-Path -LiteralPath $winLddSource) {
        Copy-Item -LiteralPath $winLddSource -Destination $playwrightBundle -Recurse -Force
    }
}
$env:CSI_OPENBASE_PLAYWRIGHT_BROWSERS = $playwrightBundle

& $pythonExecutable -m PyInstaller `
    --noconfirm `
    --clean `
    --distpath $backendDist `
    --workpath $backendWork `
    $specPath
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed" }

& dotnet publish $desktopProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputRoot
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$desktopExecutable = Join-Path $outputRoot "CSI.OpenBase.Desktop.exe"
if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
    throw "dotnet publish did not create the desktop executable: $desktopExecutable"
}
$desktopFileVersion = (Get-Item -LiteralPath $desktopExecutable).VersionInfo.FileVersion
if ($desktopFileVersion -ne "$releaseVersion.0") {
    throw "Desktop file version $desktopFileVersion does not match VERSION $releaseVersion"
}
$desktopProductVersion = (Get-Item -LiteralPath $desktopExecutable).VersionInfo.ProductVersion
if ($desktopProductVersion -ne $releaseVersion) {
    throw "Desktop product version $desktopProductVersion does not match VERSION $releaseVersion"
}

$backendOutput = Join-Path $backendDist "CSI.OpenBase.Backend"
if (-not (Test-Path -LiteralPath $backendOutput -PathType Container)) {
    throw "PyInstaller did not create the expected backend directory: $backendOutput"
}
$backendDestination = Join-Path $outputRoot "backend"
New-Item -ItemType Directory -Path $backendDestination -Force | Out-Null
Get-ChildItem -LiteralPath $backendOutput -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $backendDestination -Recurse -Force
}
Copy-Item -LiteralPath $webViewBootstrapper -Destination $outputRoot

foreach ($legalFileName in @("LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md")) {
    $legalFile = Join-Path $winformRoot $legalFileName
    if (-not (Test-Path -LiteralPath $legalFile -PathType Leaf)) {
        throw "Missing WinForms distribution notice: $legalFile"
    }
    Copy-Item -LiteralPath $legalFile -Destination $outputRoot
}
Copy-Item -LiteralPath $versionFile -Destination $outputRoot

$distributionLicenses = Join-Path $outputRoot "licenses"
New-Item -ItemType Directory -Path $distributionLicenses -Force | Out-Null
$projectLicenseDirectory = Join-Path $winformRoot "licenses"
if (Test-Path -LiteralPath $projectLicenseDirectory -PathType Container) {
    Get-ChildItem -LiteralPath $projectLicenseDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $distributionLicenses -Recurse -Force
    }
}

$webViewVersionNode = Select-Xml `
    -Path $desktopProject `
    -XPath "//*[local-name()='WebView2PackageVersion']" | Select-Object -First 1
if ($null -eq $webViewVersionNode) {
    throw "Could not read WebView2PackageVersion from the desktop project"
}
$webViewVersion = $webViewVersionNode.Node.InnerText.Trim()
$nugetRoot = $env:NUGET_PACKAGES
if (-not $nugetRoot) {
    $nugetRoot = Join-Path ([Environment]::GetFolderPath("UserProfile")) ".nuget\packages"
}
$webViewPackage = Join-Path $nugetRoot "microsoft.web.webview2\$webViewVersion"
foreach ($fileName in @("LICENSE.txt", "NOTICE.txt")) {
    $source = Join-Path $webViewPackage $fileName
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Missing WebView2 distribution notice: $source"
    }
    Copy-Item -LiteralPath $source `
        -Destination (Join-Path $distributionLicenses "WebView2-$fileName")
}

$dotnetRoot = Split-Path (Get-Command dotnet).Source -Parent
$dotnetDistributionFiles = @(
    @{ Source = (Join-Path $dotnetRoot "LICENSE.txt"); Name = "dotnet-LICENSE.txt" },
    @{ Source = (Join-Path $dotnetRoot "ThirdPartyNotices.txt"); Name = "dotnet-ThirdPartyNotices.txt" }
)
foreach ($dotnetFile in $dotnetDistributionFiles) {
    if (-not (Test-Path -LiteralPath $dotnetFile.Source)) {
        throw "Missing .NET distribution notice: $($dotnetFile.Source)"
    }
    Copy-Item -LiteralPath $dotnetFile.Source `
        -Destination (Join-Path $distributionLicenses $dotnetFile.Name)
}

$pythonBasePrefix = (& $pythonExecutable -c "import sys; print(sys.base_prefix)" |
    Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or -not $pythonBasePrefix) {
    throw "Could not locate the base CPython installation"
}
$pythonLicense = Join-Path $pythonBasePrefix "LICENSE.txt"
if (-not (Test-Path -LiteralPath $pythonLicense)) {
    throw "Missing CPython license: $pythonLicense"
}
Copy-Item -LiteralPath $pythonLicense `
    -Destination (Join-Path $distributionLicenses "CPython-LICENSE.txt")

$backendLicenseDirectory = Join-Path $distributionLicenses "backend"
& $pythonExecutable (Join-Path $winformRoot "scripts\write_dependency_licenses.py") `
    --output (Join-Path $distributionLicenses "python-packages.txt") `
    --project-notices-dir $backendLicenseDirectory `
    --include-package PyInstaller
if ($LASTEXITCODE -ne 0) { throw "Python dependency license collection failed" }

Compress-Archive -Path (Join-Path $outputRoot "*") -DestinationPath $portableArchive -CompressionLevel Optimal
$sha256 = (Get-FileHash -LiteralPath $portableArchive -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$sha256  $(Split-Path -Leaf $portableArchive)`r`n"
[System.IO.File]::WriteAllText(
    $portableChecksum,
    $checksumLine,
    [System.Text.UTF8Encoding]::new($false)
)

if (-not $SkipInstaller) {
    New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null
    & $installerCompiler.Source `
        "--define=MyAppVersion=$releaseVersion" `
        "--define=PortableSource=$outputRoot" `
        "--output-dir=$installerOutput" `
        "--output-filename=CSI-OpenBase-Setup-$releaseVersion-win-x64" `
        $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }
}

Write-Host "Windows release ready: $releaseDirectory"
Write-Host "Portable directory: $outputRoot"
Write-Host "Portable archive: $portableArchive"
Write-Host "SHA-256: $sha256"

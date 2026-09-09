[CmdletBinding()]
param(
    [string]$CurrentVersion,
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
$winformRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$versionFile = Join-Path $winformRoot "VERSION"
$versionPattern = '\A(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>[1-9][0-9])\z'

if ($Apply -and $PSBoundParameters.ContainsKey("CurrentVersion")) {
    throw "-Apply always updates the repository VERSION file; do not combine it with -CurrentVersion"
}

if ($PSBoundParameters.ContainsKey("CurrentVersion")) {
    $current = $CurrentVersion
} else {
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
        throw "Missing release version file: $versionFile"
    }
    $rawVersion = [System.IO.File]::ReadAllText($versionFile)
    if ($rawVersion -notmatch '\A(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.[1-9][0-9])(?:\r?\n)?\z') {
        throw "VERSION must contain exactly x.x.xx with a patch from 10 through 99"
    }
    $current = $Matches["version"]
}

$match = [System.Text.RegularExpressions.Regex]::Match($current, $versionPattern)
if (-not $match.Success) {
    throw "Version must be x.x.xx with a patch from 10 through 99: $current"
}

$major = [long]$match.Groups["major"].Value
$minor = [long]$match.Groups["minor"].Value
$patch = [int]$match.Groups["patch"].Value
if ($patch -eq 99) {
    $minor += 1
    $patch = 10
} else {
    $patch += 1
}
$nextVersion = "{0}.{1}.{2:D2}" -f $major, $minor, $patch

if ($Apply) {
    [System.IO.File]::WriteAllText(
        $versionFile,
        "$nextVersion`n",
        [System.Text.UTF8Encoding]::new($false)
    )
}

Write-Output $nextVersion

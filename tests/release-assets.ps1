#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$FullAssemblyPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$lightweightName = 'CodexProxyLauncher-{0}-win-x64-net48.exe' -f $Version
$fullName = 'CodexProxyLauncher-{0}-win-x64-net10.exe' -f $Version
$executableNames = @($lightweightName, $fullName)
$expectedNames = $executableNames + @($executableNames | ForEach-Object { "$_.sha256" })
$entries = @(Get-ChildItem -LiteralPath $Directory -Force)
if ($entries.Count -ne $expectedNames.Count -or
    ($entries | Where-Object { $_.PSIsContainer }) -or
    (Compare-Object -ReferenceObject $expectedNames -DifferenceObject $entries.Name -CaseSensitive)) {
    throw 'The release must contain exactly two EXEs and their two matching .sha256 files.'
}

$lightweightPath = Join-Path $Directory $lightweightName
$fullPath = Join-Path $Directory $fullName
foreach ($executable in $lightweightPath, $fullPath) {
    $filename = Split-Path -Leaf $executable
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedChecksum = '{0}  {1}' -f $hash, $filename
    $checksum = @(Get-Content -LiteralPath "$executable.sha256")
    if ($checksum.Count -ne 1 -or $checksum[0] -cne $expectedChecksum) {
        throw "The checksum file '$filename.sha256' does not match '$filename'."
    }
}

$licenseFiles = [ordered]@{
    'CodexProxyLauncher.LICENSE' = 'LICENSE'
    'CodexProxyLauncher.THIRD_PARTY_NOTICES.md' = 'THIRD_PARTY_NOTICES.md'
}
foreach ($assemblyPath in $lightweightPath, $FullAssemblyPath) {
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath))
    foreach ($name in $licenseFiles.Keys) {
        $notice = $assembly.GetManifestResourceStream($name)
        if (-not $notice) { throw "Missing embedded notice '$name' in '$assemblyPath'." }
        try {
            $embeddedHash = (Get-FileHash -InputStream $notice -Algorithm SHA256).Hash
            $sourceHash = (Get-FileHash -LiteralPath (Join-Path $projectRoot $licenseFiles[$name]) -Algorithm SHA256).Hash
            if ($embeddedHash -cne $sourceHash) { throw "Embedded notice '$name' differs from its source." }
        } finally {
            $notice.Dispose()
        }
    }
}

$runtimeNotices = @(
    'CodexProxyLauncher.Runtime.NET.LICENSE.TXT'
    'CodexProxyLauncher.Runtime.NET.THIRD-PARTY-NOTICES.TXT'
    'CodexProxyLauncher.Runtime.WindowsDesktop.LICENSE'
)
foreach ($name in $runtimeNotices) {
    $notice = $assembly.GetManifestResourceStream($name)
    if (-not $notice) { throw "Missing embedded runtime notice '$name' in the full build." }
    try {
        if ($notice.Length -eq 0) { throw "Embedded runtime notice '$name' is empty." }
    } finally {
        $notice.Dispose()
    }
}

Write-Output 'PASS: two release EXEs, individual SHA-256 files, and embedded license notices.'

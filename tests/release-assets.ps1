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
$expectedNames = @($lightweightName, $fullName, 'SHA256SUMS.txt')
$entries = @(Get-ChildItem -LiteralPath $Directory -Force)
if ($entries.Count -ne $expectedNames.Count -or
    ($entries | Where-Object { $_.PSIsContainer }) -or
    (Compare-Object -ReferenceObject $expectedNames -DifferenceObject $entries.Name -CaseSensitive)) {
    throw 'The release must contain exactly the two EXEs and SHA256SUMS.txt.'
}

$lightweightPath = Join-Path $Directory $lightweightName
$fullPath = Join-Path $Directory $fullName
$expectedChecksums = foreach ($executable in $lightweightPath, $fullPath) {
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, (Split-Path -Leaf $executable)
}
$checksums = Get-Content -LiteralPath (Join-Path $Directory 'SHA256SUMS.txt')
if (($checksums -join [Environment]::NewLine) -cne ($expectedChecksums -join [Environment]::NewLine)) {
    throw 'The SHA-256 manifest does not match the two release executables.'
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

Write-Output 'PASS: two release EXEs, SHA-256 checksums, and embedded license notices.'

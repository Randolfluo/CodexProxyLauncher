#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArchivePath,
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
$archiveFile = Get-Item -LiteralPath $ArchivePath
$name = [regex]::Match($archiveFile.Name, '^CodexProxyLauncher-(?<version>.+)-win-x64\.zip$')
if (-not $name.Success) {
    throw "Unexpected lightweight package filename '$($archiveFile.Name)'."
}

$expectedTag = 'v' + $name.Groups['version'].Value
if ($Tag -and $Tag -cne $expectedTag) {
    throw "Tag '$Tag' does not match package version '$expectedTag'."
}

$expectedFiles = @('CodexProxyLauncher.exe', 'LICENSE', 'THIRD_PARTY_NOTICES.md')
$archive = [IO.Compression.ZipFile]::OpenRead($archiveFile.FullName)
try {
    if ($archive.Entries.Count -ne $expectedFiles.Count -or
        (Compare-Object -ReferenceObject $expectedFiles -DifferenceObject $archive.Entries.FullName -CaseSensitive)) {
        throw 'The lightweight ZIP must contain the launcher and both license files at its root.'
    }
} finally {
    $archive.Dispose()
}

Write-Output "PASS: lightweight ZIP layout and package version $expectedTag."

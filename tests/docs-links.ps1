#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$documentRoots = @(
    $projectRoot
    (Join-Path $projectRoot 'scripts')
    (Join-Path $projectRoot 'src/CodexProxyLauncher')
    (Join-Path $projectRoot 'tests')
)

$linkCount = 0
$documents = Get-ChildItem -LiteralPath $documentRoots -File -Filter '*.md'
foreach ($document in $documents) {
    $html = (ConvertFrom-Markdown -LiteralPath $document.FullName).Html
    foreach ($match in [regex]::Matches($html, '<a\b[^>]*\bhref="([^"]+)"')) {
        $href = [Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
        if ($href -match '^(#|//|[a-zA-Z][a-zA-Z0-9+.-]*:)') { continue }

        $target = [Uri]::UnescapeDataString(($href -split '[?#]', 2)[0])
        if (-not (Test-Path -LiteralPath (Join-Path $document.DirectoryName $target))) {
            throw "Broken link '$href' in $($document.FullName)."
        }
        $linkCount++
    }
}
Write-Output "PASS: $linkCount local Markdown links."

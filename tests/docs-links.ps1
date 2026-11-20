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
$referencePattern = '<(?:a\b[^>]*?\shref|(?<image>img)\b[^>]*?\ssrc)="(?<url>[^"]+)"'
$documents = Get-ChildItem -LiteralPath $documentRoots -File -Filter '*.md'
foreach ($document in $documents) {
    $html = (ConvertFrom-Markdown -LiteralPath $document.FullName).Html
    foreach ($match in [regex]::Matches($html, $referencePattern)) {
        $url = [Net.WebUtility]::HtmlDecode($match.Groups['url'].Value)
        if ($url -match '^(#|//|[a-zA-Z][a-zA-Z0-9+.-]*:)') { continue }

        $target = [Uri]::UnescapeDataString(($url -split '[?#]', 2)[0])
        $pathType = if ($match.Groups['image'].Success) { 'Leaf' } else { 'Any' }
        if (-not (Test-Path -LiteralPath (Join-Path $document.DirectoryName $target) -PathType $pathType)) {
            throw "Broken link '$url' in $($document.FullName)."
        }
        $linkCount++
    }
}
Write-Output "PASS: $linkCount local Markdown links and images."

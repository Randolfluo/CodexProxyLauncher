#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectReadme = Join-Path $projectRoot 'README.md'
$previewPath = Join-Path $projectRoot 'docs/ui-preview.html'
$releaseLink = '<a href="https://github.com/Randolfluo/CodexProxyLauncher/releases">'
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
    if ($document.FullName -eq $projectReadme) {
        $intro = ($html -split '<h2\b', 2)[0]
        if (-not $intro.Contains($releaseLink)) {
            throw 'The project README introduction must link to GitHub Releases.'
        }
        if (-not $html.Contains('<a href="docs/ui-preview.html">')) {
            throw 'The project README must link to the UI preview.'
        }
    }

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
Write-Output "PASS: README download link and $linkCount local Markdown links and images."

$page = Get-Content -Raw -Encoding utf8 -LiteralPath $previewPath
if (-not $page.StartsWith('<!doctype html>') -or -not $page.Contains('<meta charset="utf-8">')) {
    throw 'The UI preview must open as a standalone UTF-8 HTML document.'
}
$fragmentPattern = '(?ms)^<!-- BEGIN CODEX PREVIEW -->\r?\n(?<fragment>.+?)^<!-- END CODEX PREVIEW -->\r?$'
$fragments = [regex]::Matches($page, $fragmentPattern)
if ($fragments.Count -ne 1) {
    throw 'Expected one marked fragment for the Codex preview.'
}
$fragment = $fragments[0].Groups['fragment'].Value
if ($fragment -match '<(?:!doctype|html|head|body)\b') {
    throw 'The Codex preview fragment must fit inside an existing HTML document.'
}

$controls = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($match in [regex]::Matches($fragment, '\bdata-ui="(?<name>[^"]+)"')) {
    $name = $match.Groups['name'].Value
    if (-not $controls.Add($name)) {
        throw "Duplicate UI preview control '$name'."
    }
}
foreach ($name in 'window', 'trayMenu', 'confirmation') {
    if (-not $controls.Contains($name)) {
        throw "The UI preview is missing '$name'."
    }
}
foreach ($reference in [regex]::Matches($fragment, '\bui\.(?<name>[a-zA-Z]\w*)')) {
    $name = $reference.Groups['name'].Value
    if (-not $controls.Contains($name)) {
        throw "The UI preview script references a missing control '$name'."
    }
}
Write-Output 'PASS: standalone UI preview, Codex fragment, and preview control bindings.'

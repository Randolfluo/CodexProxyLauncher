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
        foreach ($term in 'Codex Desktop', 'HTTP 代理', 'HTTP proxy', 'Windows 11 x64', 'TUN') {
            if ($intro -notlike "*$term*") {
                throw "The project README introduction must include '$term'."
            }
        }
        if (-not $intro.Contains($releaseLink)) {
            throw 'The project README introduction must link to GitHub Releases.'
        }
        if (-not $html.Contains('<a href="docs/ui-preview.html">')) {
            throw 'The project README must link to the UI preview.'
        }

        $quickStart = [regex]::Match($html, '(?s)<h2\b[^>]*>快速开始</h2>.*?(?<steps><ol\b[^>]*>.*?</ol>)')
        $screenshot = [regex]::Match($html, '<img\b[^>]*\bsrc="docs/images/settings\.png"')
        if (-not $quickStart.Success -or -not $screenshot.Success -or $quickStart.Index + $quickStart.Length -gt $screenshot.Index) {
            throw 'The README must show the ordered quick start before the settings screenshot.'
        }
        $steps = $quickStart.Groups['steps'].Value
        if (-not $steps.Contains('检测代理') -or -not $steps.Contains('使用代理启动 Codex')) {
            throw 'The quick start must explain the proxy test and the launch action.'
        }
        $items = [regex]::Matches($steps, '(?s)<li\b[^>]*>.*?</li>')
        if ($items.Count -lt 4 -or -not $items[3].Value.Contains('启动前清理旧进程')) {
            throw 'The fourth quick-start step must explain the startup cleanup option.'
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
Write-Output "PASS: README download link, quick start and $linkCount local Markdown links and images."

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

$projectFile = Join-Path $projectRoot 'src/CodexProxyLauncher/CodexProxyLauncher.csproj'
$project = [xml](Get-Content -Raw -Encoding utf8 -LiteralPath $projectFile)
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$previewVersionPattern = '>version\s+' + [regex]::Escape($version) + '\s*</span>'
if ($fragment -notmatch $previewVersionPattern) {
    throw "The UI preview footer must display the launcher version $version."
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
Write-Output 'PASS: standalone UI preview, version, Codex fragment, and preview control bindings.'

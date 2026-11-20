#requires -Version 7.0
[CmdletBinding()]
param([string]$Tag)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build.ps1'
$testDirectory = Join-Path $projectRoot 'tests'
$verificationDirectory = Join-Path $projectRoot ('artifacts\ci\' + [Guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $verificationDirectory 'package'
$extractedDirectory = Join-Path $verificationDirectory 'extracted'
$fullDirectory = Join-Path $verificationDirectory 'full'
$assetsDirectory = Join-Path $verificationDirectory 'release-assets'

& $buildScript -Action Package -OutputDirectory $packageDirectory | Out-Host

$packages = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.zip' -File)
if ($packages.Count -ne 1) {
    throw 'Expected exactly one lightweight ZIP from the current build.'
}
$package = $packages[0]
& (Join-Path $testDirectory 'release-package.ps1') -ArchivePath $package.FullName -Tag $Tag | Out-Host
Expand-Archive -LiteralPath $package.FullName -DestinationPath $extractedDirectory
& $buildScript -Action Publish -Runtime win-x64 -OutputDirectory $fullDirectory | Out-Host

$version = $package.Name -creplace '^CodexProxyLauncher-(.+)-win-x64\.zip$', '$1'
$null = [IO.Directory]::CreateDirectory($assetsDirectory)
$lightweight = Join-Path $assetsDirectory ('CodexProxyLauncher-{0}-win-x64-net48.exe' -f $version)
$full = Join-Path $assetsDirectory ('CodexProxyLauncher-{0}-win-x64-net10.exe' -f $version)
Copy-Item -LiteralPath (Join-Path $extractedDirectory 'CodexProxyLauncher.exe') -Destination $lightweight
Copy-Item -LiteralPath (Join-Path $fullDirectory 'CodexProxyLauncher.exe') -Destination $full
foreach ($executable in $lightweight, $full) {
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = '{0}  {1}' -f $hash, (Split-Path -Leaf $executable)
    Set-Content -LiteralPath "$executable.sha256" -Value $checksum -Encoding ascii
}

$fullAssembly = Join-Path $projectRoot 'src\CodexProxyLauncher\bin\Release\net10.0-windows\win-x64\CodexProxyLauncher.dll'
& (Join-Path $testDirectory 'release-assets.ps1') -Directory $assetsDirectory -Version $version -FullAssemblyPath $fullAssembly | Out-Host
foreach ($executable in $lightweight, $full) {
    & (Join-Path $testDirectory 'compatibility.ps1') -Executable $executable | Out-Host
}

$windowsPowerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$smokeTest = Join-Path $testDirectory 'lightweight-smoke.ps1'
& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -STA -File $smokeTest -Executable $lightweight -CheckProxy | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Lightweight smoke tests failed with exit code $LASTEXITCODE."
}
& (Join-Path $testDirectory 'docs-links.ps1') | Out-Host

$assetsDirectory

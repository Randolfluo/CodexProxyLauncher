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

& $buildScript -Action Build | Out-Host
& $buildScript -Action Package -OutputDirectory $packageDirectory | Out-Host

$packages = @(Get-ChildItem -LiteralPath $packageDirectory -Filter '*.zip' -File)
if ($packages.Count -ne 1) {
    throw 'Expected exactly one lightweight ZIP from the current build.'
}
$package = $packages[0]
& (Join-Path $testDirectory 'release-package.ps1') -ArchivePath $package.FullName -Tag $Tag | Out-Host
Expand-Archive -LiteralPath $package.FullName -DestinationPath $extractedDirectory

$executable = Join-Path $extractedDirectory 'CodexProxyLauncher.exe'
& (Join-Path $testDirectory 'compatibility.ps1') -Executable $executable | Out-Host
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$smokeTest = Join-Path $testDirectory 'lightweight-smoke.ps1'
& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -STA -File $smokeTest -Executable $executable -CheckProxy | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Lightweight smoke tests failed with exit code $LASTEXITCODE."
}
& (Join-Path $testDirectory 'docs-links.ps1') | Out-Host

$package.FullName

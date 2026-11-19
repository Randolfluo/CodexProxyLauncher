[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
# Fixed allowlist: keep source, research, installed SDK, NuGet cache and final release.
$relativePaths = @(
    'src\CodexProxyLauncher\bin',
    'src\CodexProxyLauncher\obj',
    '.tools\dotnet-install.ps1',
    '.tools\dotnet-releases.json',
    '.tools\dotnet-sdk-win-x64.zip'
)

foreach ($relativePath in $relativePaths) {
    $candidate = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
    if (-not $candidate.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup target is outside the project: $candidate"
    }
    if (-not (Test-Path -LiteralPath $candidate)) { continue }
    # Reject redirected paths, including parent directories, before recursive removal.
    $cursor = Get-Item -LiteralPath $candidate -Force
    while ($cursor.FullName -ne $projectRoot) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing cleanup through a reparse point: $($cursor.FullName)"
        }
        $cursor = Get-Item -LiteralPath (Split-Path -Parent $cursor.FullName) -Force
    }
    if ($PSCmdlet.ShouldProcess($candidate, 'Remove generated files')) {
        Remove-Item -LiteralPath $candidate -Recurse -Force
        Write-Output "Removed: $relativePath"
    }
}

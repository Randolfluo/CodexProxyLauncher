[CmdletBinding()]
param(
    [ValidateSet('Build', 'Run', 'Publish', 'Package', 'Clean')]
    [string]$Action = 'Build',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\CodexProxyLauncher\CodexProxyLauncher.csproj'
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'

if ($Action -eq 'Clean') {
    & (Join-Path $PSScriptRoot 'clean.ps1')
    return
}

if ($Action -eq 'Package' -and $Runtime -ne 'win-x64') {
    throw 'The lightweight package targets Windows x64.'
}

if (Test-Path -LiteralPath $localDotnet) {
    $dotnetExecutable = $localDotnet
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw 'Install .NET 10 SDK, or place it in .tools/dotnet. See scripts/README.md.'
    }
    $dotnetExecutable = $dotnetCommand.Source
}

if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home' }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'

function Invoke-DotNet([string[]]$Arguments) {
    & $dotnetExecutable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function New-LightweightPackage([string]$Destination) {
    $version = (Invoke-DotNet @('msbuild', $projectFile, '-nologo', '-getProperty:Version')).Trim()
    if ($version -notmatch '^[0-9]+(\.[0-9]+){2,3}(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') {
        throw "Invalid package version '$version'."
    }

    $null = [IO.Directory]::CreateDirectory($Destination)
    $archive = Join-Path $Destination "CodexProxyLauncher-$version-$Runtime.zip"
    $buildId = [Guid]::NewGuid().ToString('N')
    $stagedArchive = Join-Path $Destination ".CodexProxyLauncher-$buildId.zip"
    $tempRoot = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName.TrimEnd('\')
    $buildRoot = Join-Path $tempRoot "CodexProxyLauncher-$buildId"
    $null = New-Item -Path $buildRoot -ItemType Directory

    try {
        $buildOutput = Join-Path $buildRoot 'output'
        Invoke-DotNet @('build', $projectFile, '--configuration', $Configuration, '--framework', 'net48',
            '--artifacts-path', $buildRoot, '--output', $buildOutput, '--nologo',
            '-p:TargetFrameworks=net48', '-p:DebugType=None', '-p:DebugSymbols=false')

        $launcher = Join-Path $buildOutput 'CodexProxyLauncher.exe'
        $packageFiles = @(
            $launcher
            (Join-Path $projectRoot 'LICENSE')
            (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md')
        )
        Compress-Archive -LiteralPath $packageFiles -DestinationPath $stagedArchive -CompressionLevel Optimal
        Copy-Item -LiteralPath $launcher -Destination $Destination
        if ([IO.File]::Exists($archive)) {
            [IO.File]::Replace($stagedArchive, $archive, [NullString]::Value)
        } else {
            [IO.File]::Move($stagedArchive, $archive)
        }
        [IO.File]::Delete((Join-Path $Destination 'CodexProxyLauncher.exe.config'))
        Write-Output "Package written to $archive"
    } finally {
        [IO.File]::Delete($stagedArchive)
        $staging = Get-Item -LiteralPath $buildRoot -Force
        if ($staging.FullName -ne $buildRoot -or $staging.Parent.FullName.TrimEnd('\') -ne $tempRoot -or
            ($staging.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Unexpected package working directory '$($staging.FullName)'."
        }
        Remove-Item -LiteralPath $staging.FullName -Recurse -Force
    }
}

if ($OutputDirectory) {
    $OutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
}

Push-Location $projectRoot
try {
    switch ($Action) {
        'Build' {
            Invoke-DotNet @('build', $projectFile, '--configuration', $Configuration,
                '--framework', 'net10.0-windows', '-p:TargetFrameworks=net10.0-windows', '--nologo')
        }
        'Run' {
            Invoke-DotNet @('run', '--project', $projectFile, '--configuration', $Configuration,
                '--framework', 'net10.0-windows', '--property:TargetFrameworks=net10.0-windows')
        }
        'Publish' {
            if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot ('artifacts\' + $Runtime) }
            Invoke-DotNet @('publish', $projectFile, '--configuration', $Configuration,
                '--framework', 'net10.0-windows', '--runtime', $Runtime, '--self-contained', 'true',
                '--nologo', '-p:TargetFrameworks=net10.0-windows', '-p:PublishSingleFile=true',
                '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true',
                '-p:DebugType=None', '-p:DebugSymbols=false', '--output', $OutputDirectory)
            Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $OutputDirectory
            Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $OutputDirectory
        }
        'Package' {
            if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\release' }
            New-LightweightPackage $OutputDirectory
        }
    }
} finally {
    Pop-Location
}

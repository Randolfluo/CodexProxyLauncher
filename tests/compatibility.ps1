#requires -Version 7.0
[CmdletBinding()]
param([string]$Executable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Executable) { $Executable = Join-Path $projectRoot 'artifacts\win-x64\CodexProxyLauncher.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path

function Invoke-CapturedProcess([string]$File, [string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        $null = $process.Start()
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(70000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Test child timed out.'
        }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output.GetAwaiter().GetResult(); Error = $errorOutput.GetAwaiter().GetResult() }
    } finally { $process.Dispose() }
}

$scriptHash = (Get-FileHash -LiteralPath (Join-Path $projectRoot 'src\CodexProxyLauncher\Launcher.ps1')).Hash
$literal = 'literal''; throw "must remain data"; # 中文'
$cases = @(
    @{ Name = 'default self-test'; Args = @('--self-test'); Code = 0; Text = 'Proxy: http://127.0.0.1:7897'; Restart = 'disabled' },
    @{ Name = 'explicit one-shot self-test'; Args = @('--launch', '--self-test'); Code = 0; Text = 'Proxy: http://127.0.0.1:7897'; Restart = 'disabled' },
    @{ Name = 'proxy alias'; Args = @('--self-test', '-Proxy', 'http://127.0.0.1:7890'); Code = 0; Text = 'Proxy: http://127.0.0.1:7890' },
    @{ Name = 'long aliases'; Args = @('--self-test', '--proxy', 'http://127.0.0.1:10808', '--app-id', 'Fixture!App'); Code = 0; Text = 'AppId: Fixture!App' },
    @{ Name = 'case-insensitive options'; Args = @('--SELF-TEST', '-APPID', 'Case!App'); Code = 0; Text = 'AppId: Case!App' },
    @{ Name = 'explicit restart self-test'; Args = @('--self-test', '--RESTART-RUNNING', '--proxy', 'http://127.0.0.1:7890', '--app-id', 'Fixture!App'); Code = 0; Text = 'AppId: Fixture!App'; Restart = 'enabled' },
    @{ Name = 'restart switch as literal value'; Args = @('--self-test', '--app-id', '--restart-running'); Code = 0; Text = 'AppId: --restart-running'; Restart = 'disabled' },
    @{ Name = 'literal parameter data'; Args = @('--self-test', '--app-id', $literal); Code = 0; Text = ('AppId: ' + $literal) },
    @{ Name = 'last proxy wins'; Args = @('--self-test', '-proxy', 'one', '--proxy', 'two'); Code = 0; Text = 'Proxy: two' },
    @{ Name = 'maximum length ASCII values'; Args = @('--self-test', '--proxy', ('a' * 2048), '--app-id', ('b' * 2048)); Code = 0; Text = ('AppId: ' + ('b' * 2048)) },
    @{ Name = 'unknown option'; Args = @('--self-test', '--unknown'); Code = 2; Text = 'Unknown option: --unknown' },
    @{ Name = 'missing proxy'; Args = @('--self-test', '-Proxy'); Code = 2; Text = 'Missing value for -Proxy' },
    @{ Name = 'empty value'; Args = @('--self-test', '--app-id', ''); Code = 2; Text = 'Missing value for --app-id' },
    @{ Name = 'oversize value'; Args = @('--self-test', '-Proxy', ('a' * 2049)); Code = 2; Text = 'Option value is too long.' },
    @{ Name = 'console help'; Args = @('--self-test', '--help'); Code = 0; Text = '--restart-running' },
    @{ Name = 'help alias'; Args = @('--self-test', '/?'); Code = 0; Text = 'Options:' }
)
foreach ($case in $cases) {
    $result = Invoke-CapturedProcess $Executable $case.Args
    $combined = $result.Output + $result.Error
    if ($result.ExitCode -ne $case.Code -or -not $combined.Contains($case.Text)) {
        throw "$($case.Name) failed: exit $($result.ExitCode); $combined"
    }
    if ($case.Restart -and -not $result.Output.Contains('Restart running: ' + $case.Restart)) {
        throw "$($case.Name): restart setting mismatch."
    }
    if ($case.Code -eq 0 -and -not ($case.Args -contains '--help') -and -not ($case.Args -contains '/?')) {
        if (-not $result.Output.Contains('No application was activated.') -or -not $result.Output.Contains('Embedded script SHA256: ' + $scriptHash)) {
            throw "$($case.Name): missing safe self-test output or payload hash mismatch."
        }
    }
    Write-Output ('PASS: ' + $case.Name)
}

$powerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$payload = Join-Path $projectRoot 'src\CodexProxyLauncher\Launcher.ps1'
$harness = Join-Path $PSScriptRoot 'payload-harness.ps1'
$fixtureResult = Invoke-CapturedProcess $powerShell @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $harness, '-PayloadPath', $payload)
if ($fixtureResult.ExitCode -ne 0 -or $fixtureResult.Output -notmatch 'PASS: (\d+) payload scenarios') {
    throw ('Payload harness failed: ' + $fixtureResult.Output + $fixtureResult.Error)
}
$fixtureCount = $Matches[1]
Write-Output $fixtureResult.Output.Trim()
Write-Output "PASS: $($cases.Count) executable cases and $fixtureCount isolated payload scenarios."

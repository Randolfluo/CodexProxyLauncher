param([Parameter(Mandatory = $true)][string]$PayloadPath)
$ErrorActionPreference = 'Stop'

# Compile the real embedded C# declarations without invoking COM.
$source = Get-Content -LiteralPath $PayloadPath -Raw -Encoding UTF8
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Payload syntax errors.' }
$addType = $ast.FindAll({ param($n)
    $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Add-Type'
}, $true) | Select-Object -First 1
$declaration = $addType.CommandElements | Where-Object {
    $_ -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
    $_.Value.Contains('namespace LauncherCompatibility')
} | Select-Object -First 1
if (-not $declaration) { throw 'COM declaration not found.' }
# Rename the declarations so the test script can supply fake activation and process handles.
Add-Type -Language CSharp -TypeDefinition ($declaration.Value.Replace('LauncherCompatibility', 'CompilationCheck'))

Add-Type -Language CSharp -TypeDefinition @'
using System;

namespace LauncherCompatibility {
    public static class Desktop {
        public static string Id;
        public static string Arguments;
        public static int Calls;
        public static Action OnStart;
        public static uint Start(string id, string args) {
            Id = id; Arguments = args; Calls++; OnStart(); return 42;
        }
    }
    public static class ProcessHandle {
        public static Func<int, bool, object> OpenFixture;
        public static object Open(int processId, bool forTermination) {
            return OpenFixture(processId, forTermination);
        }
    }
}
'@

$session = 2
$installRoot = 'C:\Program Files\WindowsApps\OpenAI.Codex_fixture'
$registeredId = 'OpenAI.Codex_fixture!App'
$mutexName = "Local\CodexProxyLauncher.Test.$PID"
$payload = [scriptblock]::Create($source.Replace('Local\CodexProxyLauncher.Launch', $mutexName))

function New-FixtureProcess {
    param(
        [int]$Id, [string]$Name, [string]$ImagePath = "$installRoot\app\$Name.exe",
        [int]$SessionId = $session, [string]$ReplacementImage, [int]$AppearsAfter,
        [switch]$DenyQuery, [switch]$DenyTermination, [switch]$Stall
    )
    [pscustomobject]@{
        Id = $Id; ProcessName = $Name; SessionId = $SessionId; ImagePath = $ImagePath
        ReplacementImage = $ReplacementImage; AppearsAfter = $AppearsAfter
        DenyQuery = $DenyQuery.IsPresent; DenyTermination = $DenyTermination.IsPresent
        Stall = $Stall.IsPresent; Exited = $false; TerminationRequested = $false
    }
}

[LauncherCompatibility.Desktop]::OnStart = { $script:events.Add('activate') }
[LauncherCompatibility.ProcessHandle]::OpenFixture = {
    param($processId, $forTermination)

    $item = $script:processes | Where-Object Id -eq $processId | Select-Object -First 1
    if ($null -eq $item -or $item.Exited) { return $null }
    if ($forTermination) { [void]$script:terminationHandles.Add($processId) }
    if ($item.DenyQuery -or ($forTermination -and $item.DenyTermination)) {
        throw [UnauthorizedAccessException]::new()
    }
    if ($forTermination -and $item.ReplacementImage) {
        $item.ImagePath = $item.ReplacementImage
    }
    $handle = [pscustomobject]@{
        ImagePath = $item.ImagePath; SessionId = $item.SessionId; HasExited = $item.Exited
        CanTerminate = $forTermination; Item = $item
    }
    Add-Member -InputObject $handle -MemberType ScriptMethod -Name Terminate -Value {
        if (-not $this.CanTerminate) { throw 'Termination requires the verified termination handle.' }
        if (-not $this.Item.TerminationRequested) {
            $script:events.Add('terminate:' + $this.Item.Id)
        }
        $this.Item.TerminationRequested = $true
        if (-not $this.Item.Stall) { $this.Item.Exited = $true }
    }
    Add-Member -InputObject $handle -MemberType ScriptMethod -Name Dispose -Value {}
    $handle
}

# All discovery and activation dependencies are replaced within this test process.
function Add-Type { param($Language, $TypeDefinition) }
function Get-Process {
    param([int]$Id, [string[]]$Name, $ErrorAction)
    if ($Id -eq $PID) {
        if ($script:case.SessionZero) { return [pscustomobject]@{ SessionId = 0 } }
        return [pscustomobject]@{ SessionId = $session }
    }

    foreach ($item in $script:processes) {
        if ($item.AppearsAfter -and -not $script:events.Contains('terminate:' + $item.AppearsAfter)) { continue }
        if ($item.ProcessName -like $Name[0] -or $item.ProcessName -like $Name[1]) { $item }
    }
}
function Get-StartApps {
    $script:menuCalls++
    if ($script:case.StartApps) { [pscustomobject]@{ AppID = $registeredId } }
}
function Get-AppxPackage { param($Name)
    if ($Name -ne 'OpenAI.Codex') { throw 'Unexpected package name.' }
    $script:packageCalls++
    if ($script:case.Name -ne 'missing' -and -not $script:case.NoPackage) {
        [pscustomobject]@{
            PackageFullName = 'FixtureFullName'; PackageFamilyName = 'OpenAI.Codex_fixture'; InstallLocation = $installRoot
        }
    }
}
function Get-AppxPackageManifest { param($Package)
    if ($Package -ne 'FixtureFullName') { throw 'Unexpected package lookup.' }
    $script:manifestCalls++
    if ($script:case.Name -eq 'missing-before-restart') { return }
    [pscustomobject]@{ Package = [pscustomobject]@{ Applications = [pscustomobject]@{
        Application = @([pscustomobject]@{ Id = 'Desktop' })
    } } }
}

function Invoke-WithCompetingLaunch([scriptblock]$Attempt) {
    $ready = [Threading.ManualResetEventSlim]::new()
    $release = [Threading.ManualResetEventSlim]::new()
    $worker = [PowerShell]::Create()
    $null = $worker.AddScript({
        param($mutexName, $ready, $release)
        $mutex = [Threading.Mutex]::new($false, $mutexName)
        try {
            if (-not $mutex.WaitOne(5000)) { throw 'Mutex fixture timed out.' }
            try {
                $ready.Set()
                $release.Wait(15000) | Out-Null
            } finally { $mutex.ReleaseMutex() }
        } finally { $mutex.Dispose() }
    }).AddArgument($mutexName).AddArgument($ready).AddArgument($release)
    $pending = $worker.BeginInvoke()
    try {
        if (-not $ready.Wait(5000)) { throw 'Mutex fixture did not start.' }
        & $Attempt
    } finally {
        $release.Set()
        $null = $worker.EndInvoke($pending)
        $worker.Dispose()
        $ready.Dispose()
        $release.Dispose()
    }
}

$otherProcesses = @(
    New-FixtureProcess 200 'ChatGPT' -ImagePath "$installRoot-other\app\ChatGPT.exe"
    New-FixtureProcess 201 'codex' -ImagePath 'C:\Tools\codex.exe'
    New-FixtureProcess 202 'codex-windows-sandbox-service' -SessionId 0
    New-FixtureProcess 203 'Codex' -SessionId ($session + 1)
    New-FixtureProcess 204 'CodexProxyLauncher' -ImagePath 'C:\Tools\CodexProxyLauncher.exe'
    New-FixtureProcess 205 'node'
)
$missingError = 'Could not find the registered Codex application ID.'
$cases = @(
    @{ Name = 'running'; Processes = @(New-FixtureProcess 101 'ChatGPT'); Code = 3; MenuCalls = 0 }
    @{ Name = 'menu'; StartApps = $true; Code = 0; ExpectedId = $registeredId; PackageCalls = 0 }
    @{ Name = 'manifest'; Code = 0; ExpectedId = 'OpenAI.Codex_fixture!Desktop' }
    @{ Name = 'explicit'; AppId = 'Explicit!App'; Code = 0; ExpectedId = 'Explicit!App'; MenuCalls = 0; PackageCalls = 0 }
    @{ Name = 'missing'; Error = $missingError }
    @{
        Name = 'restart'; Restart = $true; StartApps = $true; Code = 0; ExpectedId = $registeredId
        Processes = @(
            New-FixtureProcess 102 'coDeX'
            New-FixtureProcess 101 'ChatGPT'
            New-FixtureProcess 103 'codex-command-runner' -AppearsAfter 101
        ) + $otherProcesses
        Stopped = @(101, 102, 103); TerminationHandles = @(101, 102, 103)
    }
    @{ Name = 'restart-when-idle'; Restart = $true; AppId = $registeredId; Code = 0; ExpectedId = $registeredId; MenuCalls = 0 }
    @{
        Name = 'restart-with-explicit-id'; Restart = $true; AppId = $registeredId; Code = 0; ExpectedId = $registeredId; MenuCalls = 0
        Processes = @(New-FixtureProcess 105 'ChatGPT'); Stopped = @(105); TerminationHandles = @(105)
    }
    @{ Name = 'unrelated-only'; StartApps = $true; Processes = $otherProcesses; Code = 0; ExpectedId = $registeredId }
    @{
        Name = 'system-session'; SessionZero = $true; Restart = $true; StartApps = $true; Code = 0; ExpectedId = $registeredId
        Processes = @(New-FixtureProcess 202 'codex-windows-sandbox-service' -SessionId 0)
    }
    @{
        Name = 'reused-pid'; Restart = $true; AppId = $registeredId; Code = 0; ExpectedId = $registeredId
        Processes = @(New-FixtureProcess 100 'Codex' -ReplacementImage 'C:\Tools\codex.exe'); TerminationHandles = @(100)
    }
    @{
        Name = 'permissions-and-timeout'; Restart = $true; StartApps = $true
        Processes = @(
            New-FixtureProcess 120 'ChatGPT' -DenyQuery
            New-FixtureProcess 121 'codex' -DenyTermination
            New-FixtureProcess 122 'codex-code-mode-host' -Stall
        )
        Stopped = @(122); TerminationHandles = @(121, 122)
        Error = 'ChatGPT (PID 120)'; ErrorProcesses = @('codex (PID 121)', 'codex-code-mode-host (PID 122)')
    }
    @{ Name = 'missing-before-restart'; Restart = $true; Processes = @(New-FixtureProcess 101 'ChatGPT'); Error = $missingError }
    @{ Name = 'concurrent'; Concurrent = $true; Restart = $true; StartApps = $true; Processes = @(New-FixtureProcess 101 'ChatGPT'); Code = 4; MenuCalls = 0 }
    @{
        Name = 'cleanup'; Cleanup = $true; Code = 0
        Processes = @(
            New-FixtureProcess 101 'ChatGPT'
            New-FixtureProcess 103 'codex-command-runner' -AppearsAfter 101
        ) + $otherProcesses
        Stopped = @(101, 103); TerminationHandles = @(101, 103)
    }
    @{ Name = 'cleanup-idle'; Cleanup = $true; Processes = $otherProcesses; Code = 5 }
    @{ Name = 'cleanup-no-package'; Cleanup = $true; NoPackage = $true; Processes = @(New-FixtureProcess 101 'ChatGPT'); Code = 5 }
    @{ Name = 'cleanup-concurrent'; Cleanup = $true; Concurrent = $true; Processes = @(New-FixtureProcess 101 'ChatGPT'); Code = 4; PackageCalls = 0 }
    @{
        Name = 'cleanup-timeout'; Cleanup = $true
        Processes = @(New-FixtureProcess 121 'codex' -DenyTermination)
        TerminationHandles = @(121); Error = 'codex (PID 121)'
    }
)

$checks = 0
foreach ($script:case in $cases) {
    $script:processes = $case.Processes
    $script:events = [Collections.Generic.List[string]]::new()
    $script:terminationHandles = [Collections.Generic.HashSet[int]]::new()
    $script:menuCalls = 0; $script:packageCalls = 0; $script:manifestCalls = 0
    [LauncherCompatibility.Desktop]::Calls = 0
    $caught = $null
    $status = $null
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    try {
        $attempt = {
            if ($case.Cleanup) {
                & $payload -CleanupOnly
            } else {
                & $payload -Proxy 'http://127.0.0.1:7890' -AppId ([string]$case.AppId) -RestartRunning:([bool]$case.Restart)
            }
        }
        if ($case.Concurrent) {
            $status = Invoke-WithCompetingLaunch $attempt
        } else {
            $status = & $attempt
        }
    } catch { $caught = $_.Exception.Message }
    $elapsed.Stop()

    if ($case.Error) {
        if (-not $caught -or -not $caught.Contains($case.Error)) { throw "$($case.Name): unexpected error '$caught'." }
        foreach ($processText in $case.ErrorProcesses) {
            if (-not $caught.Contains($processText)) { throw "$($case.Name): missing process '$processText'." }
        }
    } elseif ($caught -or $status -isnot [int] -or $status -ne $case.Code) {
        throw "$($case.Name): unexpected status '$status' or error '$caught'."
    }
    if ($case.Name -in 'permissions-and-timeout', 'cleanup-timeout' -and
        ($elapsed.Elapsed.TotalSeconds -lt 9 -or $elapsed.Elapsed.TotalSeconds -gt 20)) {
        throw 'The wait did not respect the shared exit deadline.'
    }
    if ($case.ContainsKey('MenuCalls') -and $script:menuCalls -ne $case.MenuCalls) { throw "$($case.Name): menu lookup count mismatch." }
    if ($case.ContainsKey('PackageCalls') -and $script:packageCalls -ne $case.PackageCalls) { throw "$($case.Name): package lookup count mismatch." }
    if ($case.Cleanup -and ($script:menuCalls -ne 0 -or $script:manifestCalls -ne 0 -or [LauncherCompatibility.Desktop]::Calls -ne 0)) {
        throw "$($case.Name): cleanup must avoid application ID lookup and activation."
    }

    $expectedEvents = @(foreach ($processId in $case.Stopped) { "terminate:$processId" })
    if ($case.ExpectedId) {
        $expectedEvents += 'activate'
        if ([LauncherCompatibility.Desktop]::Calls -ne 1 -or [LauncherCompatibility.Desktop]::Id -ne $case.ExpectedId) { throw 'App ID or activation count mismatch.' }
        if ([LauncherCompatibility.Desktop]::Arguments -ne '--proxy-server=http://127.0.0.1:7890 --proxy-bypass-list=localhost;127.0.0.1;::1') { throw 'Proxy activation arguments mismatch.' }
    }
    if (($script:events -join ',') -ne ($expectedEvents -join ',')) {
        throw "$($case.Name): termination or activation order mismatch. Observed: $script:events"
    }
    if (-not $script:terminationHandles.SetEquals([int[]]@($case.TerminationHandles | Where-Object { $null -ne $_ }))) {
        throw "$($case.Name): termination handle scope mismatch."
    }
    $checks++
}
Write-Output "PASS: $checks payload scenarios; COM declarations compiled; no real application activated."

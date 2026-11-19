# Functional reimplementation of the locally analyzed launcher's behavior.
# Design reference: https://microyy.github.io/posts/codex-desktop-proxy/
# This file is newly written; the archived payload is not embedded verbatim.
param(
    [string]$Proxy = 'http://127.0.0.1:7897',
    [string]$AppId = '',
    [switch]$RestartRunning
)

$ErrorActionPreference = 'Stop'
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace LauncherCompatibility {
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivation {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.LPWStr)] string args,
            uint options, out uint pid);
    }
    public static class Desktop {
        public static uint Start(string id, string args) {
            Type activationType = Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"));
            object instance = Activator.CreateInstance(activationType);
            try {
                uint pid;
                int result = ((IActivation)instance).ActivateApplication(id, args, 0, out pid);
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                return pid;
            } finally {
                Marshal.ReleaseComObject(instance);
            }
        }
    }

    public sealed class ProcessHandle : IDisposable {
        private const uint QueryLimitedInformation = 0x1000;
        private const uint Synchronize = 0x00100000;
        private const uint TerminateAccess = 0x0001;
        private const int InvalidParameter = 87;
        private const uint WaitSignaled = 0;
        private const uint WaitTimedOut = 258;
        private readonly SafeProcessHandle handle;
        private readonly int processId;

        private ProcessHandle(SafeProcessHandle handle, int processId) {
            this.handle = handle;
            this.processId = processId;
        }

        public static ProcessHandle Open(int processId, bool forTermination) {
            uint access = QueryLimitedInformation | Synchronize;
            if (forTermination) access |= TerminateAccess;
            SafeProcessHandle opened = OpenProcess(access, false, processId);
            if (!opened.IsInvalid) return new ProcessHandle(opened, processId);
            int error = Marshal.GetLastWin32Error();
            opened.Dispose();
            // An enumerated PID can disappear before OpenProcess.
            if (error == InvalidParameter) return null;
            throw new Win32Exception(error);
        }

        public uint SessionId {
            get {
                uint sessionId;
                if (!ProcessIdToSessionId(processId, out sessionId))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return sessionId;
            }
        }

        public string ImagePath {
            get {
                var path = new StringBuilder(32768);
                uint length = (uint)path.Capacity;
                if (!QueryFullProcessImageNameW(handle, 0, path, ref length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return path.ToString();
            }
        }

        public bool HasExited {
            get {
                uint wait = WaitForSingleObject(handle, 0);
                if (wait == WaitSignaled) return true;
                if (wait == WaitTimedOut) return false;
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public void Terminate() {
            if (TerminateProcess(handle, 1)) return;
            int error = Marshal.GetLastWin32Error();
            if (!HasExited) throw new Win32Exception(error);
        }

        public void Dispose() {
            handle.Dispose();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ProcessIdToSessionId(int processId, out uint sessionId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags,
            StringBuilder path, ref uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    }
}
'@

$sessionId = (Get-Process -Id $PID).SessionId
$discovery = @{ Packages = $null; InstallRoots = $null }
$exitTimeoutMilliseconds = 10000

function Get-CodexPackages {
    if ($null -eq $discovery.Packages) {
        $discovery.Packages = @(Get-AppxPackage -Name OpenAI.Codex)
    }
    $discovery.Packages
}

function Get-CodexProcessState {
    param([int]$ProcessId, [string[]]$InstallRoots, [switch]$Terminate)

    $handle = $null
    try {
        $handle = [LauncherCompatibility.ProcessHandle]::Open($ProcessId, $Terminate.IsPresent)
        if ($null -eq $handle -or $handle.HasExited -or $handle.SessionId -ne $sessionId) { return }

        $imagePath = $handle.ImagePath
        if ([IO.Path]::GetFileNameWithoutExtension($imagePath) -notmatch 'codex|chatgpt') { return }
        if ($InstallRoots.Count -eq 0) { return 'Pending' }
        foreach ($root in $InstallRoots) {
            if (-not $imagePath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { continue }
            if (-not $Terminate) { return 'Running' }

            # Terminate through the same handle used to verify the live image and session.
            $handle.Terminate()
            # Always force a fresh process snapshot after requesting termination.
            return 'Pending'
        }
    } catch {
        return 'Pending'
    } finally {
        if ($null -ne $handle) { $handle.Dispose() }
    }
}

function Get-RunningCodex {
    param([Diagnostics.Stopwatch]$RestartTimer)

    if ($sessionId -eq 0) { return }
    $candidates = @(Get-Process -Name '*codex*', '*chatgpt*' -ErrorAction SilentlyContinue |
        Where-Object { $_.SessionId -eq $sessionId -and $_.ProcessName -ne 'CodexProxyLauncher' } |
        Sort-Object { $_.ProcessName -ne 'ChatGPT' })
    if ($candidates.Count -eq 0) { return }

    if ($null -eq $discovery.InstallRoots) {
        $discovery.InstallRoots = @(Get-CodexPackages | ForEach-Object {
            if ($_.InstallLocation) {
                [IO.Path]::GetFullPath($_.InstallLocation).TrimEnd([char[]]'\/') + '\'
            }
        })
    }
    if ($discovery.Packages.Count -eq 0) { return }

    foreach ($candidate in $candidates) {
        $state = Get-CodexProcessState -ProcessId $candidate.Id -InstallRoots $discovery.InstallRoots
        if ($state -eq 'Running' -and $null -ne $RestartTimer -and
            $RestartTimer.ElapsedMilliseconds -lt $exitTimeoutMilliseconds) {
            $state = Get-CodexProcessState -ProcessId $candidate.Id -InstallRoots $discovery.InstallRoots -Terminate
        }
        if ($state) {
            [pscustomobject]@{ Name = $candidate.ProcessName; Id = $candidate.Id }
        }
    }
}

function Stop-RunningCodex {
    $restartTimer = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        $running = @(Get-RunningCodex -RestartTimer $restartTimer)
        if ($running.Count -eq 0) { return }
        $remainingMilliseconds = $exitTimeoutMilliseconds - $restartTimer.ElapsedMilliseconds
        if ($remainingMilliseconds -le 0) {
            $running = @(Get-RunningCodex)
            if ($running.Count -eq 0) { return }
            $details = ($running | ForEach-Object { '{0} (PID {1})' -f $_.Name, $_.Id }) -join ', '
            throw ([string]::Join([Environment]::NewLine, '请在任务管理器中结束以下进程，然后重试。', $details))
        }
        Start-Sleep -Milliseconds ([int][Math]::Min(100, $remainingMilliseconds))
    }
}

$mutex = [Threading.Mutex]::new($false, 'Local\CodexProxyLauncher.Launch')
$ownsMutex = $false
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) { return 4 }

    if (-not $RestartRunning -and @(Get-RunningCodex).Count -ne 0) { return 3 }

    if ([string]::IsNullOrEmpty($AppId)) {
        $entry = Get-StartApps | Where-Object { $_.AppID -like 'OpenAI.Codex_*!*' } | Select-Object -First 1
        if ($entry) { $AppId = $entry.AppID }
    }
    if ([string]::IsNullOrEmpty($AppId)) {
        $installed = Get-CodexPackages | Select-Object -First 1
        if ($installed) {
            $definition = Get-AppxPackageManifest -Package $installed.PackageFullName
            $first = @($definition.Package.Applications.Application) | Select-Object -First 1
            if ($first -and $first.Id) {
                $AppId = '{0}!{1}' -f $installed.PackageFamilyName, $first.Id
            }
        }
    }
    if ([string]::IsNullOrEmpty($AppId)) {
        throw 'Could not find the registered Codex application ID.'
    }

    if ($RestartRunning) {
        Stop-RunningCodex
    } elseif (@(Get-RunningCodex).Count -ne 0) {
        return 3
    }

    $flags = '--proxy-server={0} --proxy-bypass-list=localhost;127.0.0.1;::1' -f $Proxy
    Write-Host 'Starting Codex Desktop...' -ForegroundColor Cyan
    Write-Host "Application ID: $AppId"
    Write-Host "Proxy address: $Proxy"
    $startedPid = [LauncherCompatibility.Desktop]::Start($AppId, $flags)
    Write-Host 'Windows application activation succeeded.' -ForegroundColor Green
    Write-Host "Returned process ID: $startedPid"
    Write-Host 'Windows system proxy: Not required'
    Write-Host 'Clash Verge TUN: Not required'
    return 0
} finally {
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

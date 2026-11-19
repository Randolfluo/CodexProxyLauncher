#requires -Version 5.1
#requires -PSEdition Desktop
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Executable,
    [switch]$CheckProxy
)

$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if (Test-Path -LiteralPath "$Executable.config") {
    throw 'Extract the lightweight package into a clean directory without an external configuration file.'
}
$app = [Reflection.Assembly]::LoadFile($Executable)
$framework = [Attribute]::GetCustomAttribute($app, [Runtime.Versioning.TargetFrameworkAttribute])
if ($framework.FrameworkName -ne '.NETFramework,Version=v4.8') {
    throw 'The lightweight package does not target .NET Framework 4.8.'
}
$resource = $app.GetManifestResourceStream('CodexProxyLauncher.WinForms.config')
if (-not $resource) { throw 'The packaged high-DPI configuration is missing.' }
try {
    $applicationConfig = [xml]::new()
    $applicationConfig.Load($resource)
    $dpi = $applicationConfig.SelectSingleNode("/configuration/System.Windows.Forms.ApplicationConfigurationSection/add[@key='DpiAwareness']")
    if (-not $dpi -or $dpi.GetAttribute('value') -ne 'PerMonitorV2') {
        throw 'The embedded configuration does not enable PerMonitorV2.'
    }
} finally {
    $resource.Dispose()
}

Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
[Windows.Forms.Application]::SetCompatibleTextRenderingDefault($false)
$storeType = $app.GetType('CodexProxyLauncher.LauncherSettingsStore', $true)
$windowType = $app.GetType('CodexProxyLauncher.SettingsWindow', $true)
$themeType = $app.GetType('CodexProxyLauncher.LauncherTheme', $true)
$settingsPath = Join-Path ([IO.Path]::GetTempPath()) ('launcher-test-' + [Guid]::NewGuid().ToString('N') + '.json')
$icon = $null
$window = $null

try {
    $constructorFlags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
    $storeConstructor = $storeType.GetConstructor($constructorFlags, $null, [type[]]@([string]), $null)
    $store = $storeConstructor.Invoke([object[]]@($settingsPath.PSObject.BaseObject))
    [IO.File]::WriteAllText($settingsPath, '{"Host":"127.0.0.1","Port":10808,"RestartRunning":true,"Appearance":"Dark"}')
    $warning = $null
    $settings = $store.Load([ref]$warning)
    if ($warning -or $settings.Host -ne '127.0.0.1' -or $settings.Port -ne 10808 -or
        -not $settings.RestartRunning -or $settings.Appearance.ToString() -ne 'Dark') {
        throw 'Could not read existing launcher settings.'
    }
    $store.Save($settings)
    $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if ($saved.Host -ne $settings.Host -or $saved.Port -ne $settings.Port -or
        $saved.RestartRunning -ne $true -or $saved.Appearance -cne 'Dark') {
        throw 'Saved settings do not match the shared JSON format.'
    }

    $icon = [Drawing.Icon]::new($windowType, 'App.ico')
    $window = $windowType.GetConstructor([type[]]@($settings.GetType(), [Drawing.Icon])).Invoke(
        [object[]]@($settings.PSObject.BaseObject, $icon.PSObject.BaseObject))
    $null = $window.Handle
    $dark = $themeType.GetProperty('Dark').GetValue($null)
    $themeMethodFlags = [Reflection.BindingFlags]'Instance,NonPublic'
    $themeArguments = [object[]]@($dark.PSObject.BaseObject)
    $null = $windowType.GetMethod('ApplyTheme', $themeMethodFlags).Invoke($window, $themeArguments)
    if (-not $window.ReadSettings().Equals($settings)) {
        throw 'The hidden settings window returned unexpected state.'
    }

    [IO.File]::WriteAllText($settingsPath, '{"Host":"127.0.0.1","Port":7897,"RestartRunning":false}')
    if ($store.Load([ref]$warning).Appearance.ToString() -ne 'System' -or $warning) {
        throw 'The settings format without an appearance preference should default to System.'
    }
    [IO.File]::WriteAllText($settingsPath, '{broken json')
    $defaults = $store.Load([ref]$warning)
    if (-not $warning -or $defaults.Port -ne 7897) {
        throw 'Invalid settings should fall back to defaults and report a warning.'
    }
    Write-Output 'PASS: embedded configuration, existing settings, settings round-trip and fallback, and settings window.'
} finally {
    if ($window) { $window.Dispose() }
    if ($icon) { $icon.Dispose() }
    [IO.File]::Delete($settingsPath)
}

& (Join-Path $PSScriptRoot 'tray-menu.ps1') -AssemblyPath $Executable

if (-not $CheckProxy) { return }

function Wait-TaskResult([Threading.Tasks.Task]$Task, [int]$Milliseconds) {
    if (-not $Task.Wait($Milliseconds)) { throw 'Local proxy fixture timed out.' }
    $Task.GetAwaiter().GetResult()
}

function Test-ProxyReply([string]$Reply, [string]$ExpectedStatus) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $connection = $null
    $reader = $null
    $cancellation = [Threading.CancellationTokenSource]::new()
    try {
        $listener.Start()
        $proxy = [uri]"http://127.0.0.1:$($listener.LocalEndpoint.Port)"
        $probe = $checkAsync.Invoke($null, [object[]]@($proxy.PSObject.BaseObject, $cancellation.Token))
        $tcpCheck = Wait-TaskResult ($listener.AcceptTcpClientAsync()) 4000
        $tcpCheck.Dispose()
        $connection = Wait-TaskResult ($listener.AcceptTcpClientAsync()) 4000
        $stream = $connection.GetStream()
        $stream.ReadTimeout = 4000
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
        if ($reader.ReadLine() -notmatch '^CONNECT api\.openai\.com:443 HTTP/') {
            throw 'Expected an HTTPS CONNECT request to the local proxy fixture.'
        }
        while ($reader.ReadLine()) { }

        if ($Reply) {
            $response = [Text.Encoding]::ASCII.GetBytes($Reply)
            $stream.Write($response, 0, $response.Length)
            $connection.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)
        }
        $result = Wait-TaskResult $probe 12000
        if ($result.TcpKind.ToString() -ne 'Success' -or $result.HttpKind.ToString() -ne 'Error' -or
            $result.HttpStatus -cne $ExpectedStatus) {
            throw "Expected '$ExpectedStatus' from the proxy fixture but got '$($result.HttpStatus)'."
        }
    } finally {
        $cancellation.Cancel()
        if ($reader) { $reader.Dispose() }
        if ($connection) { $connection.Dispose() }
        $listener.Stop()
        $cancellation.Dispose()
    }
}

$checkAsync = $app.GetType('CodexProxyLauncher.ProxyDiagnostics', $true).GetMethod('CheckAsync')
$crlf = [string]::new([char[]]@(13, 10))
$authReply = @('HTTP/1.1 407 Proxy Authentication Required', 'Proxy-Authenticate: Basic realm=fixture',
    'Content-Length: 0', 'Connection: close', '', '') -join $crlf
$badGatewayReply = @('HTTP/1.1 502 Bad Gateway', 'Content-Length: 0', 'Connection: close', '', '') -join $crlf
Test-ProxyReply $authReply '失败（代理要求身份验证）'
Test-ProxyReply $badGatewayReply '失败（代理拒绝建立 HTTPS 隧道）'
Test-ProxyReply '' '请求超时'
Write-Output 'PASS: local CONNECT proxy authentication, tunnel rejection and timeout.'

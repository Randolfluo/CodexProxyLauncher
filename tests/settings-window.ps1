#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class SettingsWindowNative
{
    public const int Restore = 9;
    public const int NcHitTest = 0x0084;
    public const int NcActivate = 0x0086;
    public const int HitLeft = 10;
    public const int ExStyle = -20;
    public const int ExComposited = 0x02000000;

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr window, int index);

    public static int ReadWindowPixel(IntPtr window, int x, int y)
    {
        IntPtr dc = GetWindowDC(window);
        if (dc == IntPtr.Zero)
            return -1;
        try { return GetPixel(dc, x, y); }
        finally { ReleaseDC(window, dc); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern int GetPixel(IntPtr dc, int x, int y);
}
'@

$app = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $AssemblyPath).Path)
$windowType = $app.GetType('CodexProxyLauncher.SettingsWindow', $true)
$settingsType = $app.GetType('CodexProxyLauncher.LauncherSettings', $true)
$themeType = $app.GetType('CodexProxyLauncher.LauncherTheme', $true)
$settings = $settingsType.GetProperty('Default').GetValue($null)
$openFromTray = $windowType.GetMethod('OpenFromTray', [Reflection.BindingFlags]'Instance,NonPublic')
$applyTheme = $windowType.GetMethod('ApplyTheme', [Reflection.BindingFlags]'Instance,NonPublic')
$icon = [Drawing.Icon]::new($windowType, 'App.ico')
$window = $null
try {
    $window = [Activator]::CreateInstance($windowType, [object[]]@($settings.PSObject.BaseObject, $icon))
    $light = $themeType.GetProperty('Light').GetValue($null)
    $null = $applyTheme.Invoke($window, [object[]]@($light.PSObject.BaseObject))
    $area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $window.StartPosition = 'Manual'
    $window.Bounds = [Drawing.Rectangle]::new($area.Left, $area.Top, ($area.Width + 40), ($area.Height + 40))
    $null = $openFromTray.Invoke($window, @())
    if (-not $window.Visible -or -not $area.Contains($window.Bounds)) {
        throw 'The window must fit the working area before the first show returns.'
    }

    $handle = $window.Handle
    $style = [SettingsWindowNative]::GetWindowLong($handle, [SettingsWindowNative]::ExStyle)
    if (($style -band [SettingsWindowNative]::ExComposited) -eq 0) {
        throw 'Resizing must buffer the child windows together with the background.'
    }

    $window.Width = [Math]::Min($area.Width, $window.LogicalToDeviceUnits(680))
    $hostInput = $window.Controls.Find('ProxyHost', $true)[0]
    $portInput = $window.Controls.Find('ProxyPort', $true)[0]
    $normal = $window.Bounds
    $hostY = $hostInput.PointToScreen([Drawing.Point]::Empty).Y
    $portY = $portInput.PointToScreen([Drawing.Point]::Empty).Y
    $window.Width = [Math]::Max($window.MinimumSize.Width, $window.LogicalToDeviceUnits(350))
    if ($portInput.PointToScreen([Drawing.Point]::Empty).Y -le $hostInput.PointToScreen([Drawing.Point]::Empty).Y) {
        throw 'The proxy fields must stack at a narrow width.'
    }
    $window.Bounds = $normal
    if ($hostInput.PointToScreen([Drawing.Point]::Empty).Y -ne $hostY -or
        $portInput.PointToScreen([Drawing.Point]::Empty).Y -ne $portY) {
        throw 'The proxy fields did not return to their original layout after expanding the window.'
    }
    [Windows.Forms.Application]::DoEvents()

    $edgeY = $window.Top + [int]($window.Height / 2)
    $edge = [IntPtr](($edgeY -shl 16) -bor (($window.Left + 2) -band 0xffff))
    if ([SettingsWindowNative]::SendMessage($handle, [SettingsWindowNative]::NcHitTest, [IntPtr]::Zero, $edge).ToInt32() -ne
        [SettingsWindowNative]::HitLeft) {
        throw 'The custom window edge must still start a native resize.'
    }
    $middle = [int]($window.Height / 2)
    $background = [Drawing.ColorTranslator]::ToWin32($window.BackColor)
    $before = [SettingsWindowNative]::ReadWindowPixel($handle, 2, $middle)
    $null = [SettingsWindowNative]::SendMessage($handle, [SettingsWindowNative]::NcActivate, [IntPtr]1, [IntPtr]::Zero)
    if ($before -eq $background -and [SettingsWindowNative]::ReadWindowPixel($handle, 2, $middle) -ne $background) {
        throw 'Windows painted the system frame over the client area during activation.'
    }

    $titleBar = $window.Controls.Find('SettingsTitleBar', $true)[0]
    $selector = $window.Controls.Find('AppearanceSystem', $true)[0].Parent
    $selectorRow = $titleBar.GetRow($selector)
    $paint = @{ Count = 0 }
    $hostInput.Parent.add_Paint({ $paint.Count++ }.GetNewClosure())

    foreach ($restore in 'taskbar', 'tray', 'taskbar') {
        $window.WindowState = 'Minimized'
        [Windows.Forms.Application]::DoEvents()
        if ($titleBar.GetRow($selector) -ne $selectorRow) {
            throw 'Minimizing must keep the layout for the normal window size.'
        }
        $count = $paint.Count
        if ($restore -eq 'taskbar') {
            $null = [SettingsWindowNative]::ShowWindow($handle, [SettingsWindowNative]::Restore)
        } else {
            $null = $openFromTray.Invoke($window, @())
        }
        if ($window.WindowState -ne 'Normal' -or $window.Bounds -ne $normal -or $paint.Count -le $count) {
            throw "Restoring from the $restore must keep the window bounds and immediately repaint the controls."
        }
    }

    $window.WindowState = 'Maximized'
    $maximizedClient = $window.RectangleToScreen($window.ClientRectangle)
    if (-not $area.Contains($maximizedClient)) {
        throw 'The maximized content must stay inside the working area.'
    }
    $window.WindowState = 'Normal'
    if ($window.Bounds -ne $normal) {
        throw 'The window must return to its original bounds after maximizing.'
    }
    $window.Hide()
    $null = $openFromTray.Invoke($window, @())
    if (-not $window.Visible -or $window.Bounds -ne $normal -or $window.Handle -ne $handle) {
        throw 'Opening a hidden window from the tray must reuse its existing controls and bounds.'
    }

    Write-Output 'PASS: first show, responsive layout, native resize edge, activation frame, taskbar and tray restore.'
} finally {
    if ($window) { $window.Dispose() }
    $icon.Dispose()
}

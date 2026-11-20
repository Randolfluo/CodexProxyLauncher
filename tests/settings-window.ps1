#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing

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

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    public static extern int GetDpiForWindow(IntPtr window);

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr window, int index);

    public static int ReadWindowPixel(IntPtr window, int x, int y)
    {
        IntPtr dc = window == IntPtr.Zero ? GetDC(IntPtr.Zero) : GetWindowDC(window);
        if (dc == IntPtr.Zero)
            return -1;
        try { return GetPixel(dc, x, y); }
        finally { ReleaseDC(window, dc); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")]
    private static extern int GetPixel(IntPtr dc, int x, int y);
}
'@

function Wait-ScreenPixel([Drawing.Point]$Point, [int]$Color) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        [Windows.Forms.Application]::DoEvents()
        $null = [SettingsWindowNative]::DwmFlush()
        if ([SettingsWindowNative]::ReadWindowPixel([IntPtr]::Zero, $Point.X, $Point.Y) -eq $Color) {
            return $true
        }
        [Threading.Thread]::Sleep(10)
    } while ($watch.ElapsedMilliseconds -lt 1000)
    return $false
}

$app = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $AssemblyPath).Path)
$windowType = $app.GetType('CodexProxyLauncher.SettingsWindow', $true)
$settingsType = $app.GetType('CodexProxyLauncher.LauncherSettings', $true)
$themeType = $app.GetType('CodexProxyLauncher.LauncherTheme', $true)
$settings = $settingsType.GetProperty('Default').GetValue($null)
$openFromTray = $windowType.GetMethod('OpenFromTray', [Reflection.BindingFlags]'Instance,NonPublic')
$applyTheme = $windowType.GetMethod('ApplyTheme', [Reflection.BindingFlags]'Instance,NonPublic')
$icon = [Drawing.Icon]::new($windowType, 'App.ico')
$window = $null
$backdrop = $null
# Screen pixels are in physical coordinates even when the PowerShell host is DPI-unaware.
$previousDpi = [SettingsWindowNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    [Windows.Forms.Application]::EnableVisualStyles()
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
    $window.Width = [Math]::Min($area.Width, $window.LogicalToDeviceUnits(680))
    $hostInput = $window.Controls.Find('ProxyHost', $true)[0]
    $portInput = $window.Controls.Find('ProxyPort', $true)[0]
    $cleanupButton = $window.Controls.Find('CleanupCodex', $true)[0]
    $cleanupStatus = $window.Controls.Find('CleanupStatus', $true)[0]
    $cleanupRow = $cleanupButton.Parent
    $normal = $window.Bounds
    $hostY = $hostInput.PointToScreen([Drawing.Point]::Empty).Y
    $portY = $portInput.PointToScreen([Drawing.Point]::Empty).Y
    $cleanupCell = $cleanupRow.GetCellPosition($cleanupButton)
    if ($window.ClientSize.Width -gt $window.LogicalToDeviceUnits(520) -and
        ($cleanupCell.Row -ne 0 -or $cleanupCell.Column -ne 1)) {
        throw 'The cleanup action must sit to the right of its description at a normal width.'
    }
    $window.Width = [Math]::Max($window.MinimumSize.Width, $window.LogicalToDeviceUnits(350))
    if ($portInput.PointToScreen([Drawing.Point]::Empty).Y -le $hostInput.PointToScreen([Drawing.Point]::Empty).Y) {
        throw 'The proxy fields must stack at a narrow width.'
    }
    if ($cleanupRow.GetRow($cleanupButton) -ne 1 -or
        $cleanupButton.PointToScreen([Drawing.Point]::Empty).Y -lt $cleanupStatus.PointToScreen([Drawing.Point]::Empty).Y + $cleanupStatus.Height -or
        -not $cleanupStatus.Parent.ClientRectangle.Contains($cleanupStatus.Bounds)) {
        throw 'The cleanup action must fit below its description at a narrow width.'
    }
    $window.Bounds = $normal
    if ($hostInput.PointToScreen([Drawing.Point]::Empty).Y -ne $hostY -or
        $portInput.PointToScreen([Drawing.Point]::Empty).Y -ne $portY) {
        throw 'The proxy fields did not return to their original layout after expanding the window.'
    }
    if ($cleanupRow.GetCellPosition($cleanupButton) -ne $cleanupCell) {
        throw 'The cleanup action did not return to its original layout after expanding the window.'
    }
    [Windows.Forms.Application]::DoEvents()

    $help = $window.Controls.Find('RestartHelp', $true)[0]
    $help.PerformClick()
    $helpPopup = $window.OwnedForms | Where-Object AccessibleRole -eq ToolTip
    $helpDpi = [SettingsWindowNative]::GetDpiForWindow($help.Handle)
    # The title and five body lines use 12 logical pixels of text and 16 pixels of total vertical padding.
    $minimumHeight = (6 * 12 + 16) * $helpDpi / 96
    if (-not $helpPopup.Visible -or $helpPopup.Height -lt $minimumHeight -or -not $area.Contains($helpPopup.Bounds)) {
        throw 'The restart help must scale with the settings window DPI and fit the working area.'
    }
    $helpPopup.Hide()

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

    # A paint callback alone does not prove the desktop compositor displayed that paint.
    $backdrop = [Windows.Forms.Form]::new()
    $backdrop.FormBorderStyle = 'None'
    $backdrop.StartPosition = 'Manual'
    $backdrop.Bounds = $normal
    $backdrop.ShowInTaskbar = $false
    $backdrop.BackColor = [Drawing.Color]::FromArgb(211, 24, 197)
    $backdrop.TopMost = $true
    $backdrop.Show()
    $window.TopMost = $true
    $window.BringToFront()
    $sample = $window.PointToScreen([Drawing.Point]::new(
        [int]($window.Padding.Left / 2), [int]($window.ClientSize.Height * 0.75)))
    $minimize = $window.Controls.Find('MinimizeWindow', $true)[0]
    $minimize.PerformClick()
    $marker = [Drawing.ColorTranslator]::ToWin32($backdrop.BackColor)
    $desktopAvailable = Wait-ScreenPixel $sample $marker
    if (-not [SettingsWindowNative]::ShowWindowAsync($handle, [SettingsWindowNative]::Restore)) {
        throw 'Could not request a native asynchronous restore.'
    }
    if ($desktopAvailable) {
        if (-not (Wait-ScreenPixel $sample $background)) {
            throw 'The restored window did not cover the backdrop with its client background.'
        }
    } else {
        [Windows.Forms.Application]::DoEvents()
        Write-Output 'SKIP: the desktop did not display the test backdrop.'
    }
    if ($window.WindowState -ne 'Normal' -or $window.Bounds -ne $normal) {
        throw 'Asynchronous restore must return the window to its previous size and position.'
    }
    $backdrop.Dispose()
    $backdrop = $null
    $window.TopMost = $false

    $style = [SettingsWindowNative]::GetWindowLong($handle, [SettingsWindowNative]::ExStyle)
    if (($style -band [SettingsWindowNative]::ExComposited) -ne 0) {
        throw 'The window must not share a native paint buffer with its child windows.'
    }
    $getStyle = [Windows.Forms.Control].GetMethod('GetStyle', [Reflection.BindingFlags]'Instance,NonPublic')
    foreach ($control in $window, $titleBar, $hostInput.Parent) {
        if (-not $getStyle.Invoke($control, [object[]]@([Windows.Forms.ControlStyles]::OptimizedDoubleBuffer))) {
            throw 'Resizing must keep per-control double buffering.'
        }
    }

    $maximize = $window.Controls.Find('MaximizeWindow', $true)[0]
    $testButton = $window.Controls.Find('TestProxy', $true)[0]
    $rightColumnPaint = @{ Count = 0 }
    $testButton.add_Paint({ $rightColumnPaint.Count++ }.GetNewClosure())

    $count = $rightColumnPaint.Count
    $maximize.PerformClick()
    $maximizedClient = $window.RectangleToScreen($window.ClientRectangle)
    if ($window.WindowState -ne 'Maximized' -or -not $area.Contains($maximizedClient)) {
        throw 'The maximized content must stay inside the working area.'
    }
    if ($rightColumnPaint.Count -le $count) {
        throw 'Maximizing must repaint the controls at their new positions before returning.'
    }
    [Windows.Forms.Application]::DoEvents()

    $count = $rightColumnPaint.Count
    $maximize.PerformClick()
    if ($window.WindowState -ne 'Normal' -or $window.Bounds -ne $normal -or $rightColumnPaint.Count -le $count) {
        throw 'Restoring from maximized must return to the normal bounds and immediately repaint the controls.'
    }

    $narrowWidth = [Math]::Max($window.MinimumSize.Width, $window.LogicalToDeviceUnits(350))
    foreach ($width in $narrowWidth, $normal.Width, $narrowWidth, $normal.Width) {
        $count = $rightColumnPaint.Count
        $window.Width = $width
        if ($window.Width -ne $width -or $rightColumnPaint.Count -le $count) {
            throw "Changing the width to $width must repaint the right-side controls at every step."
        }
    }

    $window.Hide()
    $null = $openFromTray.Invoke($window, @())
    if (-not $window.Visible -or $window.Bounds -ne $normal -or $window.Handle -ne $handle) {
        throw 'Opening a hidden window from the tray must reuse its existing controls and bounds.'
    }

    Write-Output 'PASS: first show, live resize, activation frame, maximize and restore, and taskbar and tray restore.'
} finally {
    if ($window) { $window.Dispose() }
    if ($backdrop) { $backdrop.Dispose() }
    $icon.Dispose()
    if ($previousDpi -ne [IntPtr]::Zero) {
        $null = [SettingsWindowNative]::SetThreadDpiAwarenessContext($previousDpi)
    }
}

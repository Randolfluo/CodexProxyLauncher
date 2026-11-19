#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()

$app = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $AssemblyPath).Path)
$menuType = $app.GetType('CodexProxyLauncher.TrayQuickMenu', $true)
$themeType = $app.GetType('CodexProxyLauncher.LauncherTheme', $true)
$instanceFlags = [Reflection.BindingFlags]'Instance,NonPublic'
$applyTheme = $menuType.GetMethod('ApplyTheme', $instanceFlags)
$applyDpi = $menuType.GetMethod('ApplyDpi', $instanceFlags)
$menu = [Activator]::CreateInstance($menuType)

function Assert-Pixel([Drawing.Bitmap]$Bitmap, [int]$X, [int]$Y, [int]$Expected, [string]$Description) {
    $actual = $Bitmap.GetPixel($X, $Y).ToArgb() -band 0xffffff
    if ($actual -ne $Expected) {
        throw ('{0} at ({1}, {2}): expected #{3:x6}, got #{4:x6}.' -f $Description, $X, $Y, $Expected, $actual)
    }
}

function Assert-MenuColors([int]$Surface, [int]$AddressFill, [int]$LaunchFill, [int]$SettingsFill = $Surface) {
    $bitmap = [Drawing.Bitmap]::new($menu.ClientSize.Width, $menu.ClientSize.Height)
    try {
        $menu.DrawToBitmap($bitmap, $menu.ClientRectangle)
        $sampleX = $bitmap.Width - $menu.Padding.Right - 12
        $launchY = $menu.LaunchItem.Bounds.Top + [int]($menu.LaunchItem.Bounds.Height / 2)
        $addressY = $address.Bounds.Top + [int]($address.Bounds.Height / 2)
        $settingsY = $menu.SettingsItem.Bounds.Top + [int]($menu.SettingsItem.Bounds.Height / 2)
        Assert-Pixel $bitmap ($menu.Padding.Left - 2) $launchY $Surface 'Panel inset'
        Assert-Pixel $bitmap $sampleX $addressY $AddressFill 'Proxy address row'
        Assert-Pixel $bitmap $sampleX $launchY $LaunchFill 'Launch row'
        Assert-Pixel $bitmap $sampleX $settingsY $SettingsFill 'Settings row'
    } finally {
        $bitmap.Dispose()
    }
}

try {
    $null = $menu.Handle
    $address = $menu.Items['ProxyAddress']
    if ($menu.Items.Count -ne 6 -or $address.CanSelect -or
        $menu.LaunchItem.Enabled -or -not $menu.SettingsItem.Enabled -or -not $menu.ExitItem.Enabled -or
        $address.Text -cne '请在设置中修正代理地址') {
        throw 'The menu should show the address error and disable launch before a valid proxy is set.'
    }

    $proxy = [uri]'http://127.0.0.1:7897'
    $menu.SetState($proxy, $false)
    $null = $applyDpi.Invoke($menu, [object[]]@([int]96))
    $menu.PerformLayout()
    if ($menu.ClientSize.Width -ne 264 -or $menu.ClientSize.Height -ne 203 -or $menu.Padding.Left -ne 13 -or
        $address.Bounds.Top -ne 41 -or $menu.LaunchItem.Bounds.Top -ne 77 -or $menu.ExitItem.Bounds.Bottom -ne 190 -or
        -not $menu.Region -or $menu.Region.IsVisible(0, 0) -or -not $menu.Region.IsVisible(13, 13)) {
        throw "Unexpected 96 DPI layout or popup region. Size: $($menu.ClientSize), padding: $($menu.Padding)."
    }
    if ($address.Text -cne 'HTTP  127.0.0.1:7897' -or $address.ToolTipText -cne $address.Text -or
        -not $menu.LaunchItem.Enabled -or $menu.LaunchItem.Text -cne '使用代理启动 Codex') {
        throw 'A valid proxy should display its endpoint and enable launch.'
    }
    Assert-MenuColors 0xffffff 0xf0f4ef 0x2f6b4d
    $settingsItem = $menu.GetNextItem($menu.LaunchItem, [Windows.Forms.ArrowDirection]::Down)
    $exitItem = $menu.GetNextItem($menu.SettingsItem, [Windows.Forms.ArrowDirection]::Down)
    if ($settingsItem.Name -cne 'OpenSettings' -or $exitItem.Name -cne 'ExitLauncher') {
        throw 'Native arrow navigation should reach settings and skip the separator before exit.'
    }
    $settingsItem.Select()
    Assert-MenuColors 0xffffff 0xf0f4ef 0x2f6b4d 0xeef3ef

    $dark = $themeType.GetProperty('Dark').GetValue($null)
    $null = $applyTheme.Invoke($menu, [object[]]@($dark.PSObject.BaseObject))
    Assert-MenuColors 0x1d2520 0x303d34 0x7dd59b 0x2b382f
    $menu.SetState($null, $false)
    if ($menu.LaunchItem.Enabled -or -not $menu.SettingsItem.Enabled -or -not $menu.ExitItem.Enabled -or
        $address.Text -cne '请在设置中修正代理地址') {
        throw 'An invalid proxy should disable launch and leave settings and exit available.'
    }
    Assert-MenuColors 0x1d2520 0x3b2b28 0x303c35 0x2b382f
    $menu.SetState($proxy, $true)
    if ($menu.LaunchItem.Enabled -or $menu.ExitItem.Enabled -or -not $menu.SettingsItem.Enabled -or
        $menu.LaunchItem.Text -cne '正在启动 Codex…') {
        throw 'Launching should show progress and disable launch and exit.'
    }
    Assert-MenuColors 0x1d2520 0x303d34 0x31493b 0x2b382f

    $hostName = 'abcdefghijklmnopqrstuvwxyz.abcdefghijklmnopqrstuvwxyz.abcdefghijklmnopqrstuvwxyz.example'
    $menu.SetState([uri]"http://${hostName}:7897", $false)
    $menu.PerformLayout()
    if ($menu.ClientSize.Width -ne 264 -or $address.Bounds.Width -ne 238 -or
        $address.ToolTipText -cne "HTTP  ${hostName}:7897" -or $address.AccessibilityObject.Name -cne $address.Text -or
        -not $menu.LaunchItem.Enabled -or -not $menu.ExitItem.Enabled -or $menu.LaunchItem.Text -cne '使用代理启动 Codex') {
        throw 'A long endpoint must preserve its full tooltip and accessible text without expanding the menu.'
    }
    $menu.SetState([uri]'http://[2001:db8::1]:7897', $false)
    # .NET Framework may expand the IPv6 groups in the URI.
    if ($address.ToolTipText -notmatch '^HTTP  \[(?<Address>[0-9a-fA-F:]+)\]:7897$' -or
        -not ([Net.IPAddress]::Parse($Matches.Address)).Equals([Net.IPAddress]::Parse('2001:db8::1'))) {
        throw 'IPv6 addresses must retain brackets and the port.'
    }
    $menu.SetState($proxy, $false)

    $null = $applyDpi.Invoke($menu, [object[]]@([int]192))
    if ($menu.ClientSize.Width -ne 528 -or $menu.ClientSize.Height -ne 406 -or $menu.Padding.Left -ne 26 -or
        $menu.LaunchItem.Bounds.Top -ne 154 -or $menu.LaunchItem.Bounds.Height -ne 72 -or
        $menu.ExitItem.Bounds.Bottom -ne 380) {
        throw 'The popup, padding and rows must scale together at 200%.'
    }
    Assert-MenuColors 0x1d2520 0x303d34 0x7dd59b 0x2b382f
    $null = $applyDpi.Invoke($menu, [object[]]@([int]96))
    if ($menu.ClientSize.Width -ne 264 -or $menu.Padding.Left -ne 13) {
        throw 'Returning to 96 DPI should restore the original dimensions.'
    }

    $appearanceType = $app.GetType('CodexProxyLauncher.AppearanceMode', $true)
    $resolve = $themeType.GetMethod('Resolve', [Reflection.BindingFlags]'Static,NonPublic')
    $highContrast = $resolve.Invoke($null, [object[]]@([Enum]::Parse($appearanceType, 'System'), $false, $true))
    $null = $applyTheme.Invoke($menu, [object[]]@($highContrast.PSObject.BaseObject))
    if ($null -ne $menu.Region) {
        throw 'High contrast should use a rectangular popup.'
    }
    $systemMenu = [Drawing.SystemColors]::Menu.ToArgb() -band 0xffffff
    $highlight = [Drawing.SystemColors]::Highlight.ToArgb() -band 0xffffff
    Assert-MenuColors $systemMenu $systemMenu $systemMenu $highlight
    $null = $applyTheme.Invoke($menu, [object[]]@($dark.PSObject.BaseObject))
    if (-not $menu.Region -or $menu.Region.IsVisible(0, 0)) {
        throw 'Switching away from high contrast should restore the rounded popup.'
    }
    Assert-MenuColors 0x1d2520 0x303d34 0x7dd59b 0x2b382f
} finally {
    $menu.Dispose()
}

Write-Output 'PASS: tray menu states, keyboard navigation, endpoint tooltip, rendered themes, rounded popup, and DPI scaling.'

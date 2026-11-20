using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace CodexProxyLauncher;

internal sealed class LauncherTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int UseImmersiveDarkMode = 20;
    private const int WindowCornerPreference = 33;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private const int DefaultWindowColor = -1;
    private const int RoundWindowCorners = 2;

    private LauncherTheme(bool dark, bool highContrast = false)
    {
        IsDark = dark;
        IsHighContrast = highContrast;
    }

    public static LauncherTheme Light { get; } = new(false);
    public static LauncherTheme Dark { get; } = new(true);
    private static LauncherTheme HighContrast { get; } = new(false, highContrast: true);

    public bool IsDark { get; }
    public bool IsHighContrast { get; }
    public Color Background => Choose(0xf5f7f4, 0x181d1a, SystemColors.Control);
    public Color Surface => Choose(0xffffff, 0x222a25, SystemColors.Window);
    public Color SoftSurface => Choose(0xf0f4ef, 0x303d34, SystemColors.Control);
    public Color CloseButtonHover => Choose(0xfbefed, 0x382322, SystemColors.Control);
    public Color CloseButtonPressed => Choose(0xf1d6d2, 0x59302d, SystemColors.Control);
    public Color MenuSurface => Choose(0xffffff, 0x1d2520, SystemColors.Menu);
    public Color ResultsSurface => Choose(0xffffff, 0x141b16, SystemColors.Window);
    public Color Border => Choose(0xd6ded7, 0x435148, SystemColors.WindowText);
    public Color ControlBorder => Choose(0xaebbb1, 0x819386, SystemColors.WindowText);
    public Color Ink => Choose(0x26322c, 0xedf3ee, SystemColors.WindowText);
    public Color Muted => Choose(0x53635a, 0xb8c6bb, SystemColors.WindowText);
    public Color Accent => Choose(0x2f6b4d, 0x7dd59b, SystemColors.Highlight);
    public Color AccentHover => Choose(0x255b40, 0x9de7b2, SystemColors.Highlight);
    public Color OnAccent => Choose(0xffffff, 0x102a19, SystemColors.HighlightText);
    public Color SwitchOff => Choose(0x788a7f, 0x344138, SystemColors.ControlDarkDark);
    public Color SwitchOffBorder => Choose(0x788a7f, 0x8b9b90, SystemColors.WindowText);
    public Color SwitchThumb => Choose(0xffffff, 0xd2ded4, SystemColors.ControlLightLight);
    public Color Selection => Choose(0xdfeee3, 0x304b39, SystemColors.Highlight);
    public Color SelectionBorder => Choose(0x6f927a, 0x8fba99, SystemColors.Highlight);
    public Color OnSelection => Choose(0x254e36, 0xe6f4e9, SystemColors.HighlightText);
    public Color Focus => Choose(0x196849, 0xc2f7cd, SystemColors.Highlight);
    public Color DisabledSurface => Choose(0xf0f4ef, 0x2a342d, SystemColors.Control);
    public Color DisabledInk => Choose(0x53635a, 0xaab8ad, SystemColors.GrayText);
    public Color Warning => Choose(0x7e521b, 0xf1ce88, SystemColors.WindowText);
    public Color Error => Choose(0xa63030, 0xffb3aa, SystemColors.WindowText);
    public Color DestructiveHover => Choose(0x8b2727, 0xffc9c3, SystemColors.Highlight);
    public Color OnDestructive => Choose(0xffffff, 0x381817, SystemColors.HighlightText);

    public static LauncherTheme ForPreference(AppearanceMode mode) =>
        Resolve(mode, mode == AppearanceMode.System && WindowsUsesDarkAppearance(), SystemInformation.HighContrast);

    internal static LauncherTheme Resolve(AppearanceMode mode, bool systemDark, bool highContrast)
    {
        if (highContrast)
            return HighContrast;

        bool dark = mode switch
        {
            AppearanceMode.Dark => true,
            AppearanceMode.Light => false,
            _ => systemDark,
        };
        return dark ? Dark : Light;
    }

    private static bool WindowsUsesDarkAppearance()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    public Color StatusColor(StatusKind kind) => kind switch
    {
        StatusKind.Working or StatusKind.Success => Choose(0x20583c, 0xa8e9ba, SystemColors.WindowText),
        StatusKind.Warning => Warning,
        StatusKind.Error => Error,
        _ => Muted,
    };

    public void ApplyWindowFrame(Form window, bool integratedTitleBar = false)
    {
#if NETFRAMEWORK
        bool supportsFrame = Environment.OSVersion.Version.Build >= 22000;
#else
        bool supportsFrame = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
#endif
        if (!window.IsHandleCreated || !supportsFrame)
            return;

        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(window.Handle, UseImmersiveDarkMode, in dark, sizeof(int));
        if (integratedTitleBar)
        {
            int border = ColorTranslator.ToWin32(Choose(0xcdd7ce, 0x627469, SystemColors.WindowText));
            DwmSetWindowAttribute(window.Handle, WindowCornerPreference, RoundWindowCorners, sizeof(int));
            DwmSetWindowAttribute(window.Handle, BorderColor, in border, sizeof(int));
            return;
        }

        int caption = IsDark ? ColorTranslator.ToWin32(FromRgb(0x141a16)) : DefaultWindowColor;
        int text = IsDark ? ColorTranslator.ToWin32(Ink) : DefaultWindowColor;
        DwmSetWindowAttribute(window.Handle, CaptionColor, in caption, sizeof(int));
        DwmSetWindowAttribute(window.Handle, TextColor, in text, sizeof(int));
    }

    private Color Choose(int light, int dark, Color highContrast) =>
        IsHighContrast ? highContrast : FromRgb(IsDark ? dark : light);

    private static Color FromRgb(int rgb) => Color.FromArgb(unchecked((int)0xff000000) | rgb);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, in int value, int size);
}

using Microsoft.Win32;

namespace CodexProxyLauncher;

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexProxyLauncher";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

#if NETFRAMEWORK
        string executable = Application.ExecutablePath;
#else
        string executable = Environment.ProcessPath ?? throw new IOException("无法确定启动器的 EXE 路径。");
#endif
        string command = $"\"{executable}\" {LauncherOptions.TrayArgument}";
        if (command.Length > 260)
            throw new IOException("启动器路径过长，请移动到路径更短的目录后再开启自启。");

        using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        run.SetValue(ValueName, command, RegistryValueKind.String);
    }
}

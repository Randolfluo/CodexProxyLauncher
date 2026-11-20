using Microsoft.Win32;

namespace CodexProxyLauncher;

internal enum StartupState
{
    NotRegistered,
    InvalidCommand,
    MissingExecutable,
    Registered,
}

internal sealed record StartupEntry(StartupState State, string? Executable = null, bool IsLegacy = false);

internal sealed class StartupRegistration(string runKey, string? executable)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CodexProxyLauncher";
    private const int MaxCommandLength = 260;

    public static StartupRegistration ForCurrentUser()
    {
#if NETFRAMEWORK
        return new(RunKey, Application.ExecutablePath);
#else
        string? path = Environment.ProcessPath;
        if (string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            path = null;
        return new(RunKey, path);
#endif
    }

    public StartupEntry Read()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey);
        object? value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
            return new(StartupState.NotRegistered);
        if (key!.GetValueKind(ValueName) != RegistryValueKind.String ||
            value is not string { Length: > 0 and <= MaxCommandLength } command || command[0] != '"')
            return new(StartupState.InvalidCommand);

        int endOfPath = command.IndexOf('"', 1);
        if (endOfPath <= 1 || endOfPath == command.Length - 1 || command[endOfPath + 1] is not (' ' or '\t'))
            return new(StartupState.InvalidCommand);
        string argument = command.Substring(endOfPath + 1).Trim(' ', '\t');
        bool legacy = argument.Equals(LauncherOptions.TrayArgument, StringComparison.OrdinalIgnoreCase);
        string path = command.Substring(1, endOfPath - 1);
        if ((!legacy && !argument.Equals(LauncherOptions.SignInArgument, StringComparison.OrdinalIgnoreCase)) ||
            !IsAbsoluteExecutablePath(path))
            return new(StartupState.InvalidCommand);

        return new(ExecutableExists(path) ? StartupState.Registered : StartupState.MissingExecutable, path, legacy);
    }

    public bool IsCurrentExecutable(StartupEntry entry) =>
        executable is not null && string.Equals(entry.Executable, executable, StringComparison.OrdinalIgnoreCase);

    public StartupEntry RepairForManualStart()
    {
        StartupEntry entry = Read();
        if (entry.State == StartupState.MissingExecutable ||
            (entry.State == StartupState.Registered && entry.IsLegacy && IsCurrentExecutable(entry)))
            return SetEnabled(true);
        return entry;
    }

    public StartupEntry SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (executable is null || !IsAbsoluteExecutablePath(executable))
                throw new IOException("无法确定启动器的 EXE 路径。");
            string command = $"\"{executable}\" {LauncherOptions.SignInArgument}";
            if (command.Length > MaxCommandLength)
                throw new IOException("启动器路径过长，请移动到路径更短的目录后再开启自启。");
            if (!ExecutableExists(executable))
                throw new IOException("找不到启动器的 EXE 文件。");

            using RegistryKey run = Registry.CurrentUser.CreateSubKey(runKey, writable: true);
            run.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        StartupEntry entry = Read();
        bool saved = enabled
            ? entry.State == StartupState.Registered && !entry.IsLegacy && IsCurrentExecutable(entry)
            : entry.State == StartupState.NotRegistered;
        if (!saved)
            throw new IOException("Windows 登录启动项未保存为所选设置。");
        return entry;
    }

    private static bool IsAbsoluteExecutablePath(string path)
    {
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool ExecutableExists(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}

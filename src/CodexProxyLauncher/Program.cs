namespace CodexProxyLauncher;

internal static class Program
{
    internal const string WindowTitle = "Codex Proxy Launcher";

    [STAThread]
    private static int Main(string[] args)
    {
        bool diagnostic = args.Contains("--self-test", StringComparer.OrdinalIgnoreCase);
        try
        {
#if NETFRAMEWORK
            return RunWithEmbeddedConfiguration(args, diagnostic);
#else
            return Run(args, diagnostic);
#endif
        }
        catch (Exception exception)
        {
            if (diagnostic)
                WriteStandard(exception.Message, error: true);
            else
                ShowError(exception.Message);
            return 2;
        }
    }

#if NETFRAMEWORK
    private static int RunWithEmbeddedConfiguration(string[] args, bool diagnostic)
    {
        string path = Path.Combine(Path.GetTempPath(), $"CodexProxyLauncher-{Guid.NewGuid():N}.config");
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (output)
            using (Stream configuration = typeof(Program).Assembly.GetManifestResourceStream("CodexProxyLauncher.WinForms.config")
                ?? throw new InvalidOperationException("启动器缺少内嵌的 DPI 配置。"))
                configuration.CopyTo(output);

            AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", path);
            return Run(args, diagnostic);
        }
        finally
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // WinForms must load its settings after the application domain has the configuration path.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
#endif
    private static int Run(string[] args, bool diagnostic)
    {
#if NETFRAMEWORK
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#else
        ApplicationConfiguration.Initialize();
#endif
        if (LauncherOptions.GetDesktopMode(args) is DesktopLaunchMode mode)
        {
            TrayApplication.Run(mode);
            return 0;
        }

        LauncherOptions options = LauncherOptions.Parse(args);
        if (options.Help)
        {
            if (diagnostic)
                WriteStandard(LauncherOptions.HelpText);
            else
                ShowInformation(LauncherOptions.HelpText);
            return 0;
        }

        LaunchResult result = PowerShellLauncher.Execute(options);
        if (diagnostic)
        {
            WriteStandard(result.Output);
            if (result.Error.Length > 0)
                WriteStandard(result.Error, error: true);
            if (result.ExitCode == 0)
                WriteStandard("Embedded script SHA256: " + PowerShellLauncher.PayloadHash);
        }
        else if (result.ExitCode == PowerShellLauncher.CodexRunningExitCode)
        {
            ShowInformation("Codex 已在运行。请保存工作，在任务管理器中结束 Codex、ChatGPT 及相关进程，然后重新运行启动器。");
        }
        else if (result.ExitCode == PowerShellLauncher.OperationInProgressExitCode)
        {
            ShowInformation("另一个启动器正在处理启动或清理请求。请稍后重试。");
        }
        else if (result.ExitCode != 0)
        {
            ShowError("启动失败。\r\n\r\n" + result.FailureDetail);
        }
        return result.ExitCode;
    }

    private static void ShowInformation(string message) =>
        MessageBox.Show(message, WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static void ShowError(string message) =>
        MessageBox.Show(message, WindowTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static void WriteStandard(string message, bool error = false)
    {
        using var writer = new StreamWriter(error ? Console.OpenStandardError() : Console.OpenStandardOutput(),
            new System.Text.UTF8Encoding(false));
        writer.WriteLine(message);
    }
}

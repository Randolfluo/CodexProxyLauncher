namespace CodexProxyLauncher;

internal sealed class LauncherOptions
{
    public const string DefaultProxy = "http://127.0.0.1:7897";
    public const string TrayArgument = "--tray";
    public const string HelpText = "Codex Proxy Launcher\r\n\r\n" +
        "Double-click to open the settings window.\r\n" +
        "If Codex is running, quit it in Task Manager or opt in to restarting it.\r\n\r\nOptions:\r\n" +
        "  --tray       Start the launcher in the notification area.\r\n" +
        "  --launch     Launch Codex once (default proxy: http://127.0.0.1:7897).\r\n" +
        "  -Proxy URL / --proxy URL\r\n  -AppId ID / --app-id ID\r\n" +
        "  --restart-running  Force-close running Codex processes before launching.\r\n" +
        "                     This can interrupt active work. Off by default.\r\n" +
        "  --self-test  Validate without launching Codex.\r\n" +
        "  --help       Show this help.\r\n\r\nExample:\r\n" +
        "CodexProxyLauncher.exe --restart-running -Proxy http://127.0.0.1:7890";

    public string Proxy { get; private set; } = DefaultProxy;
    public string AppId { get; private set; } = "";
    public bool RestartRunning { get; private set; }
    public bool SelfTest { get; private set; }
    public bool Help { get; private set; }

    public static LauncherOptions ForDesktop(Uri proxy, bool restartRunning) =>
        new()
        {
            Proxy = proxy.GetComponents(UriComponents.SchemeAndServer | UriComponents.StrongPort, UriFormat.UriEscaped),
            RestartRunning = restartRunning,
        };

    public static LauncherOptions Parse(string[] args)
    {
        var options = new LauncherOptions
        {
            SelfTest = args.Contains("--self-test", StringComparer.OrdinalIgnoreCase),
        };
        for (int i = 0; i < args.Length; i++)
        {
            string option = args[i].ToLowerInvariant();
            if (option is "--self-test" or "--launch")
                continue;
            if (option == "--restart-running")
            {
                options.RestartRunning = true;
                continue;
            }
            if (option is "--help" or "-help" or "/?")
            {
                options.Help = true;
                return options;
            }
            if (option is not ("--proxy" or "-proxy" or "--app-id" or "-appid"))
                throw new ArgumentException("Unknown option: " + args[i]);
            if (++i >= args.Length || args[i].Length == 0)
                throw new ArgumentException("Missing value for " + args[i - 1]);
            if (args[i].Length > 2048)
                throw new ArgumentException("Option value is too long.");
            if (option is "--proxy" or "-proxy")
                options.Proxy = args[i];
            else
                options.AppId = args[i];
        }
        return options;
    }
}

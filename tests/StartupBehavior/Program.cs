using System.Globalization;
using System.Reflection;
using CodexProxyLauncher;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var test = new Fixture();
        VerifySettings(test);
        VerifyRegistration(test);
        VerifyStartupModes(test);
        VerifyTrayLifecycle(test);
        VerifyWindowDetails(test);
        Console.WriteLine("PASS: settings, Windows login registration, startup modes, tray lifecycle, and window details.");
    }

    private static void VerifySettings(Fixture test)
    {
        File.WriteAllText(test.SettingsPath, """
            {"Host":"127.0.0.1","Port":10808,"RestartRunning":true,"Appearance":"Dark"}
            """);
        LauncherSettings legacy = test.Settings.Load(out string? warning);
        Require(warning is null && legacy.Host == "127.0.0.1" && legacy.Port == 10808 && legacy.RestartRunning &&
            legacy.Appearance == AppearanceMode.Dark && !legacy.StartSilently && legacy.CloseToTray &&
            !legacy.LaunchCodexOnStartup, "Older settings must load with compatible launcher defaults.");
        LauncherSettings edited = legacy with { StartSilently = true, CloseToTray = false, LaunchCodexOnStartup = true };
        test.Settings.Save(edited);
        Require(test.Settings.Load(out warning) == edited && warning is null, "New settings must round-trip.");
    }

    private static void VerifyRegistration(Fixture test)
    {
        StartupRegistration registration = test.Registration;
        string bootCommand = $"\"{test.Executable}\" --boot";
        Require(registration.RepairForManualStart().State == StartupState.NotRegistered && test.ReadCommand() is null,
            "Manual startup must keep an absent login entry disabled.");

        test.WriteCommand($"\"{Path.Combine(test.DirectoryPath, "Old Build", "launcher.exe")}\" --tray");
        Require(registration.Read().State == StartupState.MissingExecutable,
            "A deleted target must not count as registered.");
        Require(registration.RepairForManualStart() == new StartupEntry(StartupState.Registered, test.Executable) &&
            test.ReadCommand() == bootCommand, "Manual startup must repair a deleted target using a quoted current path.");

        test.WriteCommand($"\"{test.Executable}\" --tray");
        Require(registration.Read().IsLegacy, "The legacy login command must be understood.");
        registration.RepairForManualStart();
        Require(test.ReadCommand() == bootCommand, "A legacy command targeting this launcher must be migrated.");

        string otherCommand = $"\"{test.OtherExecutable}\" --tray";
        test.WriteCommand(otherCommand);
        StartupEntry other = registration.RepairForManualStart();
        Require(other.State == StartupState.Registered && !registration.IsCurrentExecutable(other) &&
            test.ReadCommand() == otherCommand, "A valid registration for another copy must be preserved.");

        const string malformed = "\"relative.exe\" --boot";
        test.WriteCommand(malformed);
        Require(registration.RepairForManualStart().State == StartupState.InvalidCommand &&
            test.ReadCommand() == malformed, "Automatic repair must leave malformed commands for explicit user action.");
        registration.SetEnabled(true);
        Require(test.ReadCommand() == bootCommand, "Enabling must replace an invalid command.");
        Require(registration.SetEnabled(false).State == StartupState.NotRegistered && test.ReadCommand() is null,
            "Disabling must remove the entry.");
    }

    private static void VerifyStartupModes(Fixture test)
    {
        Require(LauncherOptions.GetDesktopMode([]) == DesktopLaunchMode.Interactive &&
            LauncherOptions.GetDesktopMode(["--BOOT"]) == DesktopLaunchMode.SignIn &&
            LauncherOptions.GetDesktopMode(["--TRAY"]) == DesktopLaunchMode.Tray &&
            LauncherOptions.GetDesktopMode(["--launch"]) is null && LauncherOptions.GetDesktopMode(["--self-test"]) is null,
            "Only launcher entry points should use the desktop startup preferences.");

        foreach (bool silent in new[] { false, true })
        foreach (bool autoLaunch in new[] { false, true })
        {
            var saved = new LauncherSettings("127.0.0.1", 10808, RestartRunning: silent)
            {
                StartSilently = silent,
                LaunchCodexOnStartup = autoLaunch,
            };
            test.Settings.Save(saved);
            test.Registration.SetEnabled(false);
            CheckLaunch(DesktopLaunchMode.Interactive, showWindow: true, launchCodex: false, registered: false);
            CheckLaunch(DesktopLaunchMode.Tray, showWindow: false, launchCodex: false, registered: false);
            Require(test.ReadCommand() is null, "Startup preferences must not create a Windows startup entry.");

            test.Registration.SetEnabled(true);
            string command = test.ReadCommand()!;
            DesktopLaunchMode signIn = LauncherOptions.GetDesktopMode([command.Substring(command.LastIndexOf(' ') + 1)])!.Value;
            CheckLaunch(signIn, showWindow: !silent, launchCodex: autoLaunch, registered: true);

            void CheckLaunch(DesktopLaunchMode mode, bool showWindow, bool launchCodex, bool registered)
            {
                var attempts = new List<LauncherOptions>();
                test.Run(mode, options =>
                {
                    attempts.Add(options);
                    return Task.FromResult(new LaunchResult(0, "", ""));
                }, (app, window, tray) =>
                {
                    if (launchCodex && attempts.Count == 0)
                        return;
                    Require(tray.Visible && window.Visible == showWindow,
                        $"Unexpected window visibility in {mode} (silent: {silent}, auto-launch: {autoLaunch}).");
                    Require(window.RunAtSignIn == registered && attempts.Count == (launchCodex ? 1 : 0),
                        $"Only Windows sign-in should use the automatic Codex setting. Mode: {mode}.");
                    Label timestamp = Control<Label>(window, "LaunchTime");
                    if (attempts.Count == 0)
                        Require(timestamp.Text.Length == 0, "The launch time must be empty until a launch is requested.");
                    if (mode == DesktopLaunchMode.Interactive && silent && autoLaunch)
                    {
                        Control<Button>(window, "LaunchCodex").PerformClick();
                        Require(attempts.Count == 1, "Manual startup must leave the launch button available.");
                    }
                    if (attempts.Count != 0)
                    {
                        Require(attempts[0].Proxy == "http://127.0.0.1:10808" && attempts[0].RestartRunning == saved.RestartRunning,
                            "The launch request must carry the saved proxy and old-process preference.");
                        Require(DateTime.TryParseExact(timestamp.Text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out _), "Both manual and automatic requests must record a local launch time.");
                    }
                    app.ExitThread();
                });
            }
        }

        string missing = Path.Combine(test.DirectoryPath, "missing.exe");
        string legacy = $"\"{missing}\" --tray";
        test.WriteCommand(legacy);
        test.Settings.Save(LauncherSettings.Default);
        test.Run(DesktopLaunchMode.SignIn, UnexpectedLaunch, (app, window, _) =>
        {
            Require(!window.RunAtSignIn && Control<Label>(window, "StartupRegistrationWarning").Text.Contains(missing),
                "A stale entry must be reported as disabled with the missing path.");
            app.ExitThread();
        });
        Require(test.ReadCommand() == legacy, "The Windows login entry point must not rewrite startup registration.");
        test.Run(DesktopLaunchMode.Interactive, UnexpectedLaunch, (app, window, _) =>
        {
            Require(window.RunAtSignIn && test.ReadCommand() == $"\"{test.Executable}\" --boot",
                "An interactive start must repair a stale entry and reflect the saved state.");
            app.ExitThread();
        });

        string longPath = Path.Combine(test.DirectoryPath, new string('x', 258 - test.DirectoryPath.Length - 5) + ".exe");
        test.Registration.SetEnabled(false);
        test.Run(DesktopLaunchMode.Interactive, UnexpectedLaunch, (app, window, _) =>
        {
            Control<CheckBox>(window, "RunAtSignIn").Checked = true;
            string warning = Control<Label>(window, "StartupRegistrationWarning").Text;
            Require(!window.RunAtSignIn && warning.Contains("路径过长") && test.ReadCommand() is null,
                "An overlong command must show the failure and restore the actual registration state.");
            app.ExitThread();
        }, new StartupRegistration(test.RunKey, longPath));
    }

    private static void VerifyTrayLifecycle(Fixture test)
    {
        test.Settings.Save(new LauncherSettings("127.0.0.1", 10808, RestartRunning: false)
            { StartSilently = true, LaunchCodexOnStartup = true });
        var result = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        int step = 0;
        string? requestedAt = null;
        test.Run(DesktopLaunchMode.SignIn, _ =>
        {
            attempts++;
            return result.Task;
        }, (_, window, tray) =>
        {
            if (step == 0)
            {
                Require(!window.Visible && tray.Visible && attempts == 1, "Silent startup must queue one request in the tray.");
                Button launch = Control<Button>(window, "LaunchCodex");
                Require(!launch.Enabled && launch.Text == "正在启动 Codex…", "A pending request must show busy feedback and block repeat clicks.");
                requestedAt = Control<Label>(window, "LaunchTime").Text;
                Require(!string.IsNullOrEmpty(requestedAt), "The time must be recorded when the request starts.");
                test.StartDuplicate(DesktopLaunchMode.SignIn);
                test.StartDuplicate(DesktopLaunchMode.Tray);
                result.SetResult(new LaunchResult(1, "", "Fixture failure"));
                step++;
                return;
            }

            if (step == 1)
            {
                if (Control<Label>(window, "LaunchStatus").Text != "启动失败")
                    return;
                Require(!window.Visible, "An automatic failure must not reveal a silent window.");
                test.StartDuplicate(DesktopLaunchMode.Interactive);
                step++;
                return;
            }

            if (!window.Visible)
                return;
            Require(attempts == 1, "A second invocation must not launch Codex again.");
            Label timestamp = Control<Label>(window, "LaunchTime");
            Require(timestamp.Visible && timestamp.Text == requestedAt, "Reopening settings must show the original request time.");
            Button launchButton = Control<Button>(window, "LaunchCodex");
            Require(launchButton.Enabled && launchButton.Text == "使用代理启动 Codex", "The launch button must become available after failure.");
            Button close = Control<Button>(window, "CloseWindow");
            string version = Control<Label>(window, "LauncherVersion").Text;
            Require(close.AccessibleName?.Contains("托盘") == true, "The close button must describe the default action.");
            close.PerformClick();
            Require(!window.Visible && tray.Visible, "Close-to-tray must hide the settings window.");
            tray.ContextMenuStrip!.Items["OpenSettings"]!.PerformClick();
            Require(window.Visible, "The tray must be able to reopen the settings window.");

            CheckBox logon = Control<CheckBox>(window, "RunAtSignIn");
            logon.Checked = true;
            Require(logon.Checked && test.Registration.Read().State == StartupState.Registered,
                "Enabling login startup in the window must register it.");
            logon.Checked = false;
            Require(!logon.Checked && test.ReadCommand() is null, "Disabling the option must unregister it.");

            Control<TextBox>(window, "ProxyPort").Text = "invalid";
            Control<CheckBox>(window, "StartSilently").Checked = false;
            Control<CheckBox>(window, "LaunchCodexOnStartup").Checked = false;
            Control<CheckBox>(window, "CloseToTray").Checked = false;
            Require(close.AccessibleName == "退出启动器" && Control<Label>(window, "LauncherVersion").Text == version,
                "Disabling close-to-tray must update the tooltip and keep the displayed version.");
            close.PerformClick();
        });

        LauncherSettings saved = test.Settings.Load(out string? warning);
        Require(warning is null && saved.Port == 10808 && !saved.StartSilently && !saved.LaunchCodexOnStartup && !saved.CloseToTray,
            "Closing must save launcher preferences while keeping the last valid proxy.");
    }

    private static void VerifyWindowDetails(Fixture test)
    {
        test.Settings.Save(LauncherSettings.Default);
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        int step = 0;
        test.Run(DesktopLaunchMode.Interactive, UnexpectedLaunch, (app, window, _) =>
        {
            CheckBox restart = Control<CheckBox>(window, "RestartRunning");
            Button launch = Control<Button>(window, "LaunchCodex");
            var slide = (ControlTransition)restart.GetType().GetField("slide", privateInstance)!.GetValue(restart)!;
            var hover = (ControlTransition)launch.GetType().GetField("hover", privateInstance)!.GetValue(launch)!;
            if (step == 0)
            {
                string version = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                    .InformationalVersion.Split('+')[0];
                Require(Control<Label>(window, "LauncherVersion").Text == $"version {version}",
                    "The footer must show this build's version.");
                int[] options = new[] { "ImportantOptionsHeading", "RestartRunning", "LauncherOptionsHeading",
                    "RunAtSignIn", "StartSilently", "LaunchCodexOnStartup", "CloseToTray" }
                    .Select(name => Control<Control>(window, name).PointToScreen(Point.Empty).Y).ToArray();
                Require(options.Zip(options.Skip(1), (first, second) => first < second).All(inOrder => inOrder),
                    "The important option must come first, followed by the three adjacent sign-in options.");
                Require(Control<CheckBox>(window, "RunAtSignIn").AccessibleName == "登录自启" &&
                    Control<CheckBox>(window, "LaunchCodexOnStartup").AccessibleName == "登录自启后使用代理启动 Codex",
                    "The sign-in options must use the reviewed wording.");

                Button help = Control<Button>(window, "RestartHelp");
                string helpText = help.AccessibleDescription ?? "";
                Require(help.Parent!.Controls[0].Text == restart.AccessibleName && help.Left >= help.Parent.Controls[0].Right &&
                    helpText.Contains("传入当前代理设置") &&
                    helpText.Contains("旧实例里正在运行的任务可能中断"),
                    "The help button must sit directly next to the restart label and explain its effect and risk.");
                Require(helpText.Split('\n').All(line => !string.IsNullOrWhiteSpace(line)),
                    "The help text must have no blank lines.");

                Form? activeForm = Form.ActiveForm;
                window.ApplyTheme(LauncherTheme.Dark);
                help.GetType().GetMethod("OnMouseHover", privateInstance)!.Invoke(help, [EventArgs.Empty]);
                HelpPopup popup = window.OwnedForms.OfType<HelpPopup>().Single();
                Require(popup.Visible && Form.ActiveForm == activeForm && !popup.ShowInTaskbar &&
                    popup.Text == help.AccessibleName && popup.AccessibleDescription == helpText,
                    "Hovering must show the help popup with its full text without moving focus.");
                Require(popup.BackColor == LauncherTheme.Dark.Surface && popup.ForeColor == LauncherTheme.Dark.Ink,
                    "The help popup must follow the window theme.");
                Require(popup.Region is Region outline && !outline.IsVisible(0, 0) &&
                    !outline.IsVisible(popup.Width - 1, popup.Height - 1) &&
                    outline.IsVisible(popup.Width / 2, popup.Height / 2),
                    "The help popup must clip its window to rounded corners.");

                help.GetType().GetMethod("OnMouseLeave", privateInstance)!.Invoke(help, [EventArgs.Empty]);
                Require(!popup.Visible, "Moving off the help button must dismiss the popup.");
                help.PerformClick();
                Require(popup.Visible && Form.ActiveForm == activeForm, "Clicking must reopen the help without moving focus.");
                help.GetType().GetMethod("OnLeave", privateInstance)!.Invoke(help, [EventArgs.Empty]);
                Require(!popup.Visible, "Leaving the help button by keyboard must dismiss the popup.");

                Label heading = Control<Label>(window, "LastLaunchHeading");
                Label time = Control<Label>(window, "LaunchTime");
                Require(!time.Visible, "The launch time must start hidden.");
                var example = new DateTimeOffset(2026, 11, 20, 9, 42, 15, TimeSpan.FromHours(8));
                window.SetLaunchStatus("正在启动…", kind: StatusKind.Working, requestedAt: example);
                window.SetLaunchStatus("Windows 已接受启动请求", kind: StatusKind.Success);
                Require(time.Visible && time.Text == "2026-11-20 09:42:15" && time.Parent == heading.Parent &&
                    (time.Left >= heading.Right || time.Top >= heading.Bottom),
                    "The result must keep the timestamp alongside the heading, allowing it to wrap in a narrow window.");

                window.ApplyTheme(LauncherTheme.Light);
                hover.MoveTo(0f, animate: false);
                restart.Checked = true;
                launch.GetType().GetMethod("OnMouseEnter", privateInstance)!.Invoke(launch, [EventArgs.Empty]);
                if (ControlTransition.AnimationsEnabled && restart.IsHandleCreated && launch.IsHandleCreated)
                    Require(slide.Position < 1f && hover.Position < 1f, "The controls must animate to their new appearance.");
                step = 1;
                return;
            }

            if (step == 1 && slide.Position == 1f && hover.Position == 1f)
            {
                restart.Checked = false;
                launch.GetType().GetMethod("OnMouseLeave", privateInstance)!.Invoke(launch, [EventArgs.Empty]);
                step = 2;
            }
            else if (step == 2 && slide.Position == 0f && hover.Position == 0f)
            {
                app.ExitThread();
            }
        });
    }

    private static T Control<T>(SettingsWindow window, string name) where T : Control =>
        (T)window.Controls.Find(name, searchAllChildren: true).Single();

    private static Task<LaunchResult> UnexpectedLaunch(LauncherOptions _) =>
        throw new InvalidOperationException("Unexpected Codex launch.");

    private static void Require(bool condition, string explanation)
    {
        if (!condition)
            throw new InvalidOperationException(explanation);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string id = Guid.NewGuid().ToString("N");
        public string DirectoryPath => Path.Combine(Path.GetTempPath(), "Codex Launcher " + id);
        public string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
        public string Executable => Path.Combine(DirectoryPath, "启动 器.exe");
        public string OtherExecutable => Path.Combine(DirectoryPath, "other.exe");
        public string RunKey => @"Software\CodexProxyLauncher.Tests." + id;
        public LauncherSettingsStore Settings => new(SettingsPath);
        public StartupRegistration Registration => new(RunKey, Executable);

        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Executable, "fixture");
            File.WriteAllText(OtherExecutable, "fixture");
        }

        public string? ReadCommand()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(StartupRegistration.ValueName) as string;
        }

        public void WriteCommand(string command)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(StartupRegistration.ValueName, command, RegistryValueKind.String);
        }

        public void StartDuplicate(DesktopLaunchMode mode) => Task.Run(() => TrayApplication.Run(mode, id,
            () => throw new InvalidOperationException("A duplicate must not start another tray application."))).GetAwaiter().GetResult();

        public void Run(DesktopLaunchMode mode, Func<LauncherOptions, Task<LaunchResult>> launch,
            Action<TrayApplication, SettingsWindow, NotifyIcon> check, StartupRegistration? registration = null)
        {
            using var timer = new Timer { Interval = 20 };
            using var watchdog = new Timer { Interval = 5000 };
            watchdog.Tick += (_, _) => throw new TimeoutException($"The {mode} launcher did not exit.");
            bool exited = false;
            TrayApplication.Run(mode, id, () =>
            {
                var app = new TrayApplication(Settings, registration ?? Registration, launch);
                BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var window = (SettingsWindow)typeof(TrayApplication).GetField("window", flags)!.GetValue(app)!;
                var tray = (NotifyIcon)typeof(TrayApplication).GetField("tray", flags)!.GetValue(app)!;
                timer.Tick += (_, _) => check(app, window, tray);
                app.ThreadExit += (_, _) =>
                {
                    Require(!tray.Visible, "Exiting must remove the tray icon.");
                    exited = true;
                };
                timer.Start();
                watchdog.Start();
                return app;
            });
            Require(exited, "The launcher must exit its WinForms message loop.");
        }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKey(RunKey, throwOnMissingSubKey: false); }
            finally
            {
                File.Delete(SettingsPath);
                File.Delete(Executable);
                File.Delete(OtherExecutable);
                Directory.Delete(DirectoryPath);
            }
        }
    }
}

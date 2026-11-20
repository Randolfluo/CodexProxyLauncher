using System.Security;
using Microsoft.Win32;

namespace CodexProxyLauncher;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly Icon icon = new(typeof(TrayApplication), "App.ico");
    private readonly Icon trayIcon;
    private readonly SettingsWindow window;
    private readonly TrayQuickMenu menu = new();
    private readonly NotifyIcon tray;
    private readonly LauncherSettingsStore store;
    private readonly StartupRegistration startupRegistration;
    private readonly Func<LauncherOptions, Task<LaunchResult>> execute;
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 500 };
    private LauncherSettings savedSettings;
    private LauncherSettings? draftSettings;
    private LauncherTheme? appliedTheme;
    private Uri? proxy;
    private CancellationTokenSource? proxyCheck;
    private CodexOperation operation;
    private bool exiting;
    private bool disposed;

    internal TrayApplication(LauncherSettingsStore store, StartupRegistration startupRegistration,
        Func<LauncherOptions, Task<LaunchResult>> execute)
    {
        this.store = store;
        this.startupRegistration = startupRegistration;
        this.execute = execute;
        savedSettings = store.Load(out string? warning);
        window = new SettingsWindow(savedSettings, icon);
        window.SetSettingsWarning(warning);

        window.FormClosing += OnWindowClosing;
        window.FormClosed += (_, _) => ExitThread();
        window.SettingsEdited += (_, _) => ReadSettings();
        window.AppearanceChanged += (_, _) =>
        {
            RefreshAppearance();
            ReadSettings();
        };
        window.StartupChanged += (_, _) => UpdateStartupRegistration(
            () => startupRegistration.SetEnabled(window.RunAtSignIn), "无法更新 Windows 登录启动项。");
        window.LaunchRequested += async (_, _) => await LaunchFromSettingsAsync();
        window.CleanupRequested += async (_, _) => await CleanupAsync();
        window.TestRequested += async (_, _) => await CheckProxyAsync();
        menu.LaunchItem.Click += async (_, _) => await LaunchFromSettingsAsync();
        menu.SettingsItem.Click += (_, _) => OpenSettings();
        menu.ExitItem.Click += (_, _) => ExitThread();
        menu.Opening += (_, _) => RefreshAppearance();
        saveTimer.Tick += (_, _) => SaveSettings();

        RefreshAppearance();
        ReadSettings();
        trayIcon = new Icon(icon, SystemInformation.SmallIconSize);
        tray = new NotifyIcon { Icon = trayIcon, Text = Program.WindowTitle, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
                OpenSettings();
        };
    }

    public static void Run(DesktopLaunchMode mode) => Run(mode, "", () => new TrayApplication(
        new LauncherSettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexProxyLauncher", "settings.json")),
        StartupRegistration.ForCurrentUser(), options => Task.Run(() => PowerShellLauncher.Execute(options))));

    internal static void Run(DesktopLaunchMode mode, string instanceSuffix, Func<TrayApplication> createApplication)
    {
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\CodexProxyLauncher.ShowSettings" + instanceSuffix);
        using var instance = new Mutex(false, @"Local\CodexProxyLauncher.Tray" + instanceSuffix);
        bool ownsInstance;
        try
        {
            ownsInstance = instance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsInstance = true;
        }
        if (!ownsInstance)
        {
            if (mode == DesktopLaunchMode.Interactive)
                activation.Set();
            return;
        }

        try
        {
            using var app = createApplication();
            app.UpdateStartupRegistration(mode == DesktopLaunchMode.Interactive
                ? app.startupRegistration.RepairForManualStart : app.startupRegistration.Read,
                "无法读取或修复 Windows 登录启动项。");
            LauncherSettings startupSettings = app.savedSettings;
            using var dispatcher = new WindowsFormsSynchronizationContext();
            void PostToUi(Action action)
            {
                try
                {
                    dispatcher.Post(_ =>
                    {
                        if (!app.exiting && !app.disposed)
                            action();
                    }, null);
                }
                catch (InvalidOperationException)
                {
                    // The message loop can dispose the dispatcher as the launcher exits.
                }
            }

            RegisteredWaitHandle request = ThreadPool.RegisterWaitForSingleObject(activation,
                (_, _) => PostToUi(app.OpenSettings), null, Timeout.Infinite, false);
            UserPreferenceChangedEventHandler preferenceChanged = (_, _) => PostToUi(app.RefreshAppearance);
            bool subscribed = false;
            try
            {
                try
                {
                    SystemEvents.UserPreferenceChanged += preferenceChanged;
                    subscribed = true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
                {
                    // Opening settings or the tray menu still refreshes the Windows preference.
                }
                bool signingIn = mode == DesktopLaunchMode.SignIn;
                if (mode == DesktopLaunchMode.Interactive || (signingIn && !startupSettings.StartSilently))
                    app.OpenSettings();
                if (signingIn && startupSettings.LaunchCodexOnStartup)
                {
                    var options = LauncherOptions.ForDesktop(startupSettings.CreateProxy(), startupSettings.RestartRunning);
                    PostToUi(async () => await app.LaunchAsync(options, revealFailure: false));
                }
                Application.Run(app);
            }
            finally
            {
                if (subscribed)
                    SystemEvents.UserPreferenceChanged -= preferenceChanged;
                using var callbackFinished = new ManualResetEvent(false);
                if (request.Unregister(callbackFinished))
                    callbackFinished.WaitOne();
            }
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    private void OpenSettings()
    {
        if (exiting)
            return;
        RefreshAppearance();
        window.OpenFromTray();
    }

    private void RefreshAppearance()
    {
        LauncherTheme palette = LauncherTheme.ForPreference(window.Appearance);
        if (ReferenceEquals(palette, appliedTheme) && !palette.IsHighContrast)
            return;
        window.ApplyTheme(palette);
        menu.ApplyTheme(palette);
        appliedTheme = palette;
    }

    private void OnWindowClosing(object? sender, FormClosingEventArgs e)
    {
        SaveSettings();
        if (e.CloseReason == CloseReason.UserClosing && !exiting && window.CloseToTray)
        {
            e.Cancel = true;
            window.Hide();
        }
    }

    private void ReadSettings()
    {
        draftSettings = window.ReadSettings();
        Uri? next = draftSettings?.CreateProxy();
        if (!Equals(proxy, next))
        {
            proxy = next;
            CancelProxyCheck();
            window.SetProxyStatus(new("待检测", "待检测"));
        }
        saveTimer.Stop();
        if (draftSettings is null)
            window.SetSaveStatus("待修正", StatusKind.Warning);
        else if (draftSettings != savedSettings)
            window.SetSaveStatus("正在保存…", StatusKind.Working);
        else
            window.SetSaveStatus("更改将自动保存");
        if (PendingSettings != savedSettings)
            saveTimer.Start();
        UpdateActions();
    }

    private LauncherSettings PendingSettings => draftSettings ?? window.ReadPreferences(savedSettings);

    private void SaveSettings()
    {
        saveTimer.Stop();
        LauncherSettings pending = PendingSettings;
        if (pending == savedSettings)
            return;
        try
        {
            store.Save(pending);
            savedSettings = pending;
            if (draftSettings is not null)
                window.SetSaveStatus("已保存", StatusKind.Success);
            window.SetSettingsWarning(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            window.SetSaveStatus("保存失败", StatusKind.Error);
            window.SetSettingsWarning("无法保存本地设置，请检查配置目录的写入权限。");
        }
    }

    private void UpdateStartupRegistration(Func<StartupEntry> update, string failureMessage)
    {
        string? warning = null;
        StartupEntry? entry = null;
        try
        {
            entry = update();
        }
        catch (Exception ex) when (IsStartupError(ex))
        {
            warning = failureMessage + "\n" + ex.Message;
            try { entry = startupRegistration.Read(); }
            catch (Exception readError) when (IsStartupError(readError)) { }
        }

        warning ??= entry switch
        {
            { State: StartupState.InvalidCommand } => "Windows 登录启动项无效，开启开关可重新登记。",
            { State: StartupState.MissingExecutable } => "Windows 登录启动文件已不存在。\n" + entry.Executable,
            { State: StartupState.Registered } when !startupRegistration.IsCurrentExecutable(entry) =>
                "登录时将运行另一位置的启动器。关闭后重新开启可切换到此版本。\n" + entry.Executable,
            _ => null,
        };
        window.SetStartupState(entry?.State == StartupState.Registered, entry is not null, warning);
    }

    private static bool IsStartupError(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;

    private bool CanRunCodexOperation => operation == CodexOperation.None && !exiting && !disposed;

    private void UpdateActions()
    {
        bool launching = operation == CodexOperation.Launching;
        window.SetActions(proxy is not null && CanRunCodexOperation, launching,
            CanRunCodexOperation, operation == CodexOperation.CleaningUp,
            proxy is not null && proxyCheck is null, proxyCheck is not null);
        menu.SetState(proxy, launching, cleanupPending: operation is CodexOperation.ConfirmingCleanup or CodexOperation.CleaningUp);
    }

    private void CancelProxyCheck()
    {
        CancellationTokenSource? previous = proxyCheck;
        proxyCheck = null;
        previous?.Cancel();
    }

    private async Task CheckProxyAsync()
    {
        if (proxy is null || proxyCheck is not null)
            return;
        using var request = new CancellationTokenSource();
        proxyCheck = request;
        window.SetProxyStatus(new("检测中…", "等待端口检测", StatusKind.Working));
        UpdateActions();
        try
        {
            ProxyCheckResult result = await ProxyDiagnostics.CheckAsync(proxy, request.Token);
            if (proxyCheck == request)
                window.SetProxyStatus(result);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            if (proxyCheck == request)
            {
                proxyCheck = null;
                UpdateActions();
            }
        }
    }

    private Task LaunchFromSettingsAsync()
    {
        if (proxy is null || draftSettings is null || !CanRunCodexOperation)
            return Task.CompletedTask;
        SaveSettings();
        return LaunchAsync(LauncherOptions.ForDesktop(proxy, draftSettings.RestartRunning), revealFailure: true);
    }

    private async Task LaunchAsync(LauncherOptions options, bool revealFailure)
    {
        if (!CanRunCodexOperation)
            return;
        operation = CodexOperation.Launching;
        window.SetLaunchStatus("正在启动…", kind: StatusKind.Working, requestedAt: DateTimeOffset.Now);
        UpdateActions();
        try
        {
            LaunchResult result = await execute(options);
            if (exiting || disposed)
                return;
            switch (result.ExitCode)
            {
                case 0:
                    window.SetLaunchStatus("Windows 已接受启动请求", kind: StatusKind.Success);
                    window.SetCleanupStatus();
                    break;
                case PowerShellLauncher.CodexRunningExitCode:
                    window.SetLaunchStatus("Codex 已在运行，请退出旧实例后重试", kind: StatusKind.Warning);
                    break;
                case PowerShellLauncher.OperationInProgressExitCode:
                    window.SetLaunchStatus("已有启动或清理请求，请稍后重试", kind: StatusKind.Warning);
                    break;
                default:
                    window.SetLaunchStatus("启动失败", result.FailureDetail, StatusKind.Error);
                    break;
            }
            if (revealFailure && result.ExitCode != 0)
                OpenSettings();
        }
        catch (Exception ex)
        {
            if (!exiting && !disposed)
            {
                window.SetLaunchStatus("启动失败", ex.Message, StatusKind.Error);
                if (revealFailure)
                    OpenSettings();
            }
        }
        finally
        {
            operation = CodexOperation.None;
            if (!exiting && !disposed)
                UpdateActions();
        }
    }

    private async Task CleanupAsync()
    {
        if (!CanRunCodexOperation)
            return;

        operation = CodexOperation.ConfirmingCleanup;
        UpdateActions();
        try
        {
            if (!window.ConfirmCleanup() || exiting || disposed)
                return;

            operation = CodexOperation.CleaningUp;
            window.SetCleanupStatus("正在等待 Codex 进程退出…", kind: StatusKind.Working);
            UpdateActions();
            LaunchResult result = await execute(LauncherOptions.ForCleanup());
            if (exiting || disposed)
                return;

            switch (result.ExitCode)
            {
                case 0:
                    window.SetCleanupStatus("清理完成，Codex 已退出。", kind: StatusKind.Success);
                    break;
                case PowerShellLauncher.NoCodexProcessesExitCode:
                    window.SetCleanupStatus("当前没有需要清理的 Codex 进程。");
                    break;
                case PowerShellLauncher.OperationInProgressExitCode:
                    window.SetCleanupStatus("已有启动或清理请求，请稍后重试。", kind: StatusKind.Warning);
                    break;
                default:
                    window.SetCleanupStatus("清理失败", result.FailureDetail, StatusKind.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (!exiting && !disposed)
                window.SetCleanupStatus("清理失败", ex.Message, StatusKind.Error);
        }
        finally
        {
            operation = CodexOperation.None;
            if (!exiting && !disposed)
                UpdateActions();
        }
    }

    protected override void ExitThreadCore()
    {
        if (exiting)
            return;
        exiting = true;
        SaveSettings();
        CancelProxyCheck();
        tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            tray.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            window.Dispose();
            icon.Dispose();
            saveTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private enum CodexOperation
    {
        None,
        Launching,
        ConfirmingCleanup,
        CleaningUp,
    }
}

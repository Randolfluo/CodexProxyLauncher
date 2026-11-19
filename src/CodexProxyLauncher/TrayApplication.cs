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
    private readonly LauncherSettingsStore store = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexProxyLauncher", "settings.json"));
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 500 };
    private LauncherSettings savedSettings;
    private LauncherSettings? draftSettings;
    private LauncherTheme? appliedTheme;
    private Uri? proxy;
    private CancellationTokenSource? proxyCheck;
    private bool launching;
    private bool settingStartup;
    private bool exiting;
    private bool disposed;

    private TrayApplication()
    {
        savedSettings = store.Load(out string? warning);
        window = new SettingsWindow(savedSettings, icon);
        window.SetSettingsWarning(warning);
        try
        {
            window.RunAtSignIn = StartupRegistration.IsEnabled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            window.DisableStartupSwitch();
            window.SetSettingsWarning("无法读取 Windows 登录启动设置。");
        }

        window.FormClosing += OnWindowClosing;
        window.SettingsEdited += (_, _) => ReadSettings();
        window.AppearanceChanged += (_, _) =>
        {
            RefreshAppearance();
            ReadSettings();
        };
        window.StartupChanged += (_, _) => ChangeStartup();
        window.LaunchRequested += async (_, _) => await LaunchAsync();
        window.TestRequested += async (_, _) => await CheckProxyAsync();
        menu.LaunchItem.Click += async (_, _) => await LaunchAsync();
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

    public static void Run(bool startInTray)
    {
        using var activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexProxyLauncher.ShowSettings");
        using var instance = new Mutex(false, @"Local\CodexProxyLauncher.Tray");
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
            if (!startInTray)
                activation.Set();
            return;
        }

        try
        {
            using var app = new TrayApplication();
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
                if (!startInTray)
                    app.OpenSettings();
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
        if (e.CloseReason == CloseReason.UserClosing && !exiting)
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

    private LauncherSettings PendingSettings => draftSettings ?? (savedSettings with { Appearance = window.Appearance });

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

    private void ChangeStartup()
    {
        if (settingStartup)
            return;
        bool enabled = window.RunAtSignIn;
        try
        {
            StartupRegistration.SetEnabled(enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            settingStartup = true;
            window.RunAtSignIn = !enabled;
            settingStartup = false;
            window.ShowWarning("无法更新 Windows 登录启动设置。\n\n" + ex.Message);
        }
    }

    private void UpdateActions()
    {
        bool canLaunch = proxy is not null && !launching;
        window.SetActions(canLaunch, launching, proxy is not null && proxyCheck is null, proxyCheck is not null);
        menu.SetState(proxy, launching);
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

    private async Task LaunchAsync()
    {
        if (proxy is null || draftSettings is null || launching)
            return;
        SaveSettings();
        LauncherOptions options = LauncherOptions.ForDesktop(proxy, draftSettings.RestartRunning);
        launching = true;
        window.SetLaunchStatus("正在启动…", kind: StatusKind.Working);
        UpdateActions();
        try
        {
            LaunchResult result = await Task.Run(() => PowerShellLauncher.Execute(options));
            switch (result.ExitCode)
            {
                case 0:
                    window.SetLaunchStatus("Windows 已接受启动请求", kind: StatusKind.Success);
                    break;
                case PowerShellLauncher.CodexRunningExitCode:
                    window.SetLaunchStatus("Codex 已在运行，请退出旧实例后重试", kind: StatusKind.Warning);
                    break;
                case PowerShellLauncher.LaunchInProgressExitCode:
                    window.SetLaunchStatus("已有启动请求，请稍后重试", kind: StatusKind.Warning);
                    break;
                default:
                    window.SetLaunchStatus("启动失败", result.FailureDetail, StatusKind.Error);
                    break;
            }
            if (result.ExitCode != 0)
                OpenSettings();
        }
        catch (Exception ex)
        {
            window.SetLaunchStatus("启动失败", ex.Message, StatusKind.Error);
            OpenSettings();
        }
        finally
        {
            launching = false;
            UpdateActions();
        }
    }

    protected override void ExitThreadCore()
    {
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
}

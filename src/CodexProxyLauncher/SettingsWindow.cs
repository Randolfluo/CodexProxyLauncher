using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CodexProxyLauncher;

internal sealed class SettingsWindow : Form
{
    private static readonly Size DefaultClientSize = new(680, 532);
    private static readonly Size MinimumClientSize = new(320, 256);
    private const int CompactWindowWidth = 520;
    private const int StackedFieldsWidth = 400;
    private const int ContentInset = 22;
    private const int CompactContentInset = 16;
    private const int ResizeInset = 6;
    private const float AddressFontSizeInPoints = 10;
    private const int WsCaption = 0x00C00000;
    private const int WsThickFrame = 0x00040000;
    private const int WsExWindowEdge = 0x00000100;
    private const int WmSize = 0x0005;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcPaint = 0x0085;
    private const int WmNcActivate = 0x0086;
    private const int WmNcLButtonDown = 0x00A1;
    private const int WmSysCommand = 0x0112;
    private const int ScRestore = 0xF120;
    private const int SizeRestored = 0;
    private const int SizeMinimized = 1;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private static readonly IntPtr SkipNonClientPaint = new(-1);

    private int lastSizeType;
    private bool calculatingRestoreBounds;

    private readonly Font bodyFont = new("Microsoft YaHei UI", 9);
    private readonly Font titleFont = new("Segoe UI", 10.5f, FontStyle.Bold);
    private readonly Font sectionFont = new("Microsoft YaHei UI", 10, FontStyle.Bold);
    private readonly Font captionFont = new("Microsoft YaHei UI", 8.5f);
    private Font addressFont = new("Consolas", AddressFontSizeInPoints);
    private readonly ToolTip captionToolTip = new();
    private Bitmap? appMark;
    private readonly List<Action<LauncherTheme>> themeBindings = [];
    private readonly Dictionary<Label, StatusKind> statusKinds = [];
    private readonly (AppearanceMode Mode, RadioButton Button)[] appearanceChoices =
    [
        (AppearanceMode.Light, AppearanceButton("AppearanceLight", "浅色模式")),
        (AppearanceMode.Dark, AppearanceButton("AppearanceDark", "深色模式")),
        (AppearanceMode.System, AppearanceButton("AppearanceSystem", "跟随系统")),
    ];
    private LauncherTheme theme = LauncherTheme.Light;

    private readonly TextBox host = new() { Name = "ProxyHost", AccessibleName = "代理主机", MaxLength = 255 };
    private readonly TextBox port = new() { Name = "ProxyPort", AccessibleName = "代理端口" };
    private readonly Button testButton = new() { Name = "TestProxy", Text = "检测代理" };
    private readonly Button launchButton = new() { Name = "LaunchCodex", Text = "使用代理启动 Codex" };
    private readonly ToggleSwitch restart = new() { AccessibleName = "启动前清理旧进程" };
    private readonly ToggleSwitch startup = new() { AccessibleName = "登录时启动启动器" };
    private readonly Label validation = TextLabel("");
    private readonly Label tcpStatus = TextLabel("");
    private readonly Label httpStatus = TextLabel("");
    private readonly Label launchStatus = TextLabel("");
    private readonly Label settingsWarning = TextLabel("");
    private readonly Label saveStatus = TextLabel("更改将自动保存");
    private readonly LinkLabel launchDetails = new()
    {
        AutoSize = true, Text = "查看失败详情", Visible = false, Margin = new Padding(0, 2, 0, 0),
    };
    private string? launchError;
    private bool invalidHost;
    private bool invalidPort;

    public event EventHandler? AppearanceChanged;
    public event EventHandler? SettingsEdited;
    public event EventHandler? StartupChanged;
    public event EventHandler? LaunchRequested;
    public event EventHandler? TestRequested;

    public SettingsWindow(LauncherSettings settings, Icon icon)
    {
        // Buffer managed painting without putting child HWNDs on one native restore surface.
        DoubleBuffered = true;
        SuspendLayout();
        Text = Program.WindowTitle;
        Icon = icon;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        SizeGripStyle = SizeGripStyle.Hide;
        // The client area includes the resize margin and the integrated title bar.
        Padding = new Padding(ResizeInset);
        Size = DefaultClientSize;
        MinimumSize = MinimumClientSize;
        Font = bodyFont;
        Appearance = settings.Appearance;

        var frame = Stack();
        frame.AutoSize = false;
        frame.Dock = DockStyle.Fill;
        var (header, arrangeHeader, maximize) = BuildHeader(icon);
        AddRow(frame, header);

        var contents = Stack(new Padding(ContentInset, 2, ContentInset, 0));
        var viewport = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0), TabIndex = 1 };
        viewport.Controls.Add(contents);
        var (proxySection, arrangeFields) = BuildProxySection();
        AddRow(contents, proxySection);
        AddRow(contents, Divider(new Padding(0, 14, 0, 12)));
        var (launchSection, arrangeLaunch) = BuildLaunchSection();
        AddRow(contents, launchSection);
        AddRow(contents, Divider(new Padding(0, 12, 0, 12)));
        AddRow(contents, BuildBehaviorSection());
        settingsWarning.Margin = new Padding(0, 0, 0, 10);
        AddRow(contents, settingsWarning);
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        frame.Controls.Add(viewport, 0, frame.RowCount++);

        var (footer, resizeGrip) = BuildFooter();
        AddRow(frame, footer);
        Controls.Add(frame);

        bool compact = false;
        bool stackedFields = false;
        (int Width, int InnerWidth) lastReflow = default;
        void Reflow(bool dpiChanged = false)
        {
            var widths = (Width: ClientSize.Width, InnerWidth: ClientSize.Width - Padding.Horizontal);
            // A minimized window receives a small temporary size, then restores its original layout.
            if (WindowState == FormWindowState.Minimized || (!dpiChanged && widths == lastReflow))
                return;

            lastReflow = widths;
            bool nextCompact = widths.Width <= LogicalToDeviceUnits(CompactWindowWidth);
            bool nextStackedFields = widths.Width <= LogicalToDeviceUnits(StackedFieldsWidth);
            if (nextCompact != compact)
            {
                compact = nextCompact;
                arrangeLaunch(compact);
            }
            if (nextStackedFields != stackedFields)
            {
                stackedFields = nextStackedFields;
                contents.SuspendLayout();
                arrangeFields(stackedFields);
                int inset = LogicalToDeviceUnits(stackedFields ? CompactContentInset : ContentInset);
                contents.Padding = new Padding(inset, contents.Padding.Top, inset, contents.Padding.Bottom);
                header.Padding = new Padding(inset, header.Padding.Top, header.Padding.Right, header.Padding.Bottom);
                contents.ResumeLayout(performLayout: true);
            }
            arrangeHeader(widths.InnerWidth);
        }

        ClientSizeChanged += (_, _) => Reflow();
        PaddingChanged += (_, _) => Reflow();
        Resize += (_, _) => UpdateMaximizeButton(maximize, resizeGrip);
        DpiChanged += (_, _) => BeginInvoke((MethodInvoker)(() =>
        {
#if NETFRAMEWORK
            UpdateNativeInputFont();
#endif
            FitToWorkingArea();
            Reflow(dpiChanged: true);
        }));

        host.Text = settings.Host;
        port.Text = settings.Port.ToString(CultureInfo.InvariantCulture);
        restart.Checked = settings.RestartRunning;
        appearanceChoices.First(choice => choice.Mode == Appearance).Button.Checked = true;
        SetProxyStatus(new("待检测", "待检测"));
        SetLaunchStatus("尚未启动");
        SetSaveStatus("更改将自动保存");
        SetSettingsWarning(null);
        ApplyTheme(LauncherTheme.ForPreference(Appearance));

        foreach (var (mode, button) in appearanceChoices)
        {
            button.CheckedChanged += (_, _) =>
            {
                if (!button.Checked)
                    return;
                Appearance = mode;
                ApplyAppearanceSelection();
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
            };
        }
        host.TextChanged += OnSettingsEdited;
        port.TextChanged += OnSettingsEdited;
        restart.CheckedChanged += OnSettingsEdited;
        startup.CheckedChanged += (_, _) => StartupChanged?.Invoke(this, EventArgs.Empty);
        testButton.Click += (_, _) => TestRequested?.Invoke(this, EventArgs.Empty);
        launchButton.Click += (_, _) => LaunchRequested?.Invoke(this, EventArgs.Empty);
        launchDetails.LinkClicked += (_, _) =>
        {
            if (launchError is not null)
                ShowWarning(launchError);
        };
        ResumeLayout(performLayout: true);
        Reflow();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool RunAtSignIn { get => startup.Checked; set => startup.Checked = value; }

    internal AppearanceMode Appearance { get; private set; }

    internal void DisableStartupSwitch() => startup.Enabled = false;

    internal void OpenFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            SendMessage(Handle, WmSysCommand, (IntPtr)ScRestore, IntPtr.Zero);
        Activate();
    }

    public LauncherSettings? ReadSettings()
    {
        string proxyHost = host.Text.Trim();
        bool validHost = LauncherSettings.IsValidHost(proxyHost);
        bool validPort = int.TryParse(port.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int proxyPort)
            && LauncherSettings.IsValidPort(proxyPort);
        invalidHost = !validHost;
        invalidPort = !validPort;
        host.Parent?.Invalidate();
        port.Parent?.Invalidate();
        validation.Text = !validHost ? "请输入有效的代理主机名或 IP 地址。"
            : !validPort ? "代理端口必须是 1–65535 之间的整数。" : "";
        validation.Visible = validation.Text.Length != 0;
        return validHost && validPort
            ? new LauncherSettings(proxyHost, proxyPort, restart.Checked) { Appearance = Appearance }
            : null;
    }

    public void SetActions(bool launchEnabled, bool launchBusy, bool testEnabled, bool testBusy)
    {
        launchButton.Enabled = launchEnabled;
        launchButton.Text = launchBusy ? "正在启动 Codex…" : "使用代理启动 Codex";
        testButton.Enabled = testEnabled;
        testButton.Text = testBusy ? "检测中…" : "检测代理";
        ApplyActionColors();
    }

    public void SetProxyStatus(ProxyCheckResult result)
    {
        SetStatus(tcpStatus, result.TcpStatus, result.TcpKind);
        SetStatus(httpStatus, result.HttpStatus, result.HttpKind);
    }

    public void SetLaunchStatus(string status, string? error = null, StatusKind kind = StatusKind.Neutral)
    {
        SetStatus(launchStatus, status, kind);
        launchError = error;
        launchDetails.Visible = !string.IsNullOrWhiteSpace(error);
    }

    public void SetSettingsWarning(string? warning)
    {
        settingsWarning.Text = warning ?? "";
        settingsWarning.Visible = !string.IsNullOrWhiteSpace(warning);
    }

    public void SetSaveStatus(string text, StatusKind kind = StatusKind.Neutral) => SetStatus(saveStatus, text, kind);

    internal void ShowWarning(string message)
    {
        if (!theme.IsDark)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new Form
        {
            Text = Text, Icon = Icon, Font = bodyFont,
            ClientSize = new Size(460, 230), AutoScaleDimensions = new SizeF(96, 96),
            AutoScaleMode = AutoScaleMode.Dpi, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
        };
        var details = new TextBox
        {
            AccessibleName = "警告详情", Text = message, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
        };
        var close = new Button { Text = "确定", DialogResult = DialogResult.OK };
        ConfigureButton(close);
        close.Size = new Size(88, 33);
        close.Anchor = AnchorStyles.Right;
        var body = Stack(new Padding(18));
        body.Dock = DockStyle.Fill;
        body.AutoSize = false;
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(details, 0, body.RowCount++);
        AddRow(body, close);
        dialog.Controls.Add(body);
        dialog.AcceptButton = dialog.CancelButton = close;
        dialog.ActiveControl = close;

        void ApplyDialogTheme(LauncherTheme palette)
        {
            dialog.BackColor = details.BackColor = palette.Background;
            details.ForeColor = palette.Ink;
            ApplyButtonColors(close, palette, primary: true);
            palette.ApplyWindowFrame(dialog);
        }

        dialog.HandleCreated += (_, _) => theme.ApplyWindowFrame(dialog);
        ApplyDialogTheme(theme);
        themeBindings.Add(ApplyDialogTheme);
        try
        {
            dialog.ShowDialog(this);
        }
        finally
        {
            themeBindings.Remove(ApplyDialogTheme);
        }
    }

    internal void ApplyTheme(LauncherTheme palette)
    {
        theme = palette;
        BackColor = palette.Background;
        ForeColor = palette.Ink;
        validation.ForeColor = palette.Error;
        settingsWarning.ForeColor = palette.Warning;
        launchDetails.LinkColor = palette.Accent;
        launchDetails.ActiveLinkColor = palette.AccentHover;
        launchDetails.VisitedLinkColor = palette.Accent;
        foreach (Action<LauncherTheme> apply in themeBindings)
            apply(palette);
        foreach (var status in statusKinds)
            status.Key.ForeColor = palette.StatusColor(status.Value);

        ApplyAppearanceSelection();
        ApplyActionColors();
        restart.ApplyTheme(palette);
        startup.ApplyTheme(palette);
        palette.ApplyWindowFrame(this, integratedTitleBar: true);
        Invalidate(invalidateChildren: true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
#if NETFRAMEWORK
        UpdateNativeInputFont();
#endif
        theme.ApplyWindowFrame(this, integratedTitleBar: true);
    }

    protected override void OnLoad(EventArgs e)
    {
        FitToWorkingArea();
        base.OnLoad(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.Style &= ~WsCaption;
            if (calculatingRestoreBounds)
            {
                // WinForms may convert its saved client size using the native frame styles.
                // Our client already fills the window, so there is no border to add back.
                parameters.Style &= ~WsThickFrame;
                parameters.ExStyle &= ~WsExWindowEdge;
            }
            return parameters;
        }
    }

    protected override Rectangle GetScaledBounds(Rectangle bounds, SizeF factor, BoundsSpecified specified)
    {
        if (WindowState != FormWindowState.Normal)
            return base.GetScaledBounds(bounds, factor, specified);

        // WinForms would otherwise account for the caption and borders implied by the native styles.
        if ((specified & BoundsSpecified.Width) != 0)
            bounds.Width = (int)Math.Round(bounds.Width * factor.Width);
        if ((specified & BoundsSpecified.Height) != 0)
            bounds.Height = (int)Math.Round(bounds.Height * factor.Height);
        return bounds;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcCalcSize)
        {
            // The first RECT is the proposed window bounds for both forms of WM_NCCALCSIZE.
            // When zoomed, keep the client inside the destination monitor's work area.
            if (message.LParam != IntPtr.Zero && IsZoomed(message.HWnd))
                FitMaximizedClientArea(message.LParam);
            message.Result = IntPtr.Zero;
            return;
        }

        if (message.Msg == WmNcPaint && !IsIconic(message.HWnd))
        {
            message.Result = IntPtr.Zero;
            return;
        }

        if (message.Msg == WmNcActivate && !IsIconic(message.HWnd))
        {
            // Let Windows update activation without painting its frame over the client.
            message.LParam = SkipNonClientPaint;
        }

        if (message.Msg == WmWindowPosChanged && lastSizeType != SizeRestored && !IsIconic(message.HWnd))
        {
            RestoreWindowPosition(ref message);
            return;
        }

        if (message.Msg == WmSize)
            lastSizeType = message.WParam.ToInt32();

        if (message.Msg == WmNcHitTest && IsHandleCreated)
        {
            int coordinates = unchecked((int)message.LParam);
            var point = PointToClient(new Point((short)coordinates, (short)(coordinates >> 16)));
            if (ClientRectangle.Contains(point))
            {
                message.Result = (IntPtr)(WindowState == FormWindowState.Normal ? ResizeHitTest(point) : HitTest.Client);
                return;
            }
        }

        base.WndProc(ref message);
    }

    private void RestoreWindowPosition(ref Message message)
    {
        bool wasMinimized = lastSizeType == SizeMinimized;
        bool previous = calculatingRestoreBounds;
        calculatingRestoreBounds = !IsZoomed(message.HWnd);
        try
        {
            base.WndProc(ref message);
        }
        finally
        {
            calculatingRestoreBounds = previous;
        }

        // Windows can restore directly from the taskbar. Paint all controls after the layout settles.
        if (wasMinimized && lastSizeType != SizeMinimized)
            RedrawWindow(message.HWnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
    }

    private static void FitMaximizedClientArea(IntPtr parameters)
    {
        var proposed = Marshal.PtrToStructure<NativeRectangle>(parameters);
        Rectangle bounds = Rectangle.FromLTRB(proposed.Left, proposed.Top, proposed.Right, proposed.Bottom);
        Rectangle client = Rectangle.Intersect(bounds, Screen.FromRectangle(bounds).WorkingArea);
        if (client.Width > 0 && client.Height > 0)
        {
            Marshal.StructureToPtr(new NativeRectangle
            {
                Left = client.Left, Top = client.Top, Right = client.Right, Bottom = client.Bottom,
            }, parameters, fDeleteOld: false);
        }
    }

    private HitTest ResizeHitTest(Point point)
    {
        int row = point.Y < Padding.Top ? -1 : point.Y >= ClientSize.Height - Padding.Bottom ? 1 : 0;
        int column = point.X < Padding.Left ? -1 : point.X >= ClientSize.Width - Padding.Right ? 1 : 0;
        return (row, column) switch
        {
            (-1, -1) => HitTest.TopLeft,
            (-1, 0) => HitTest.Top,
            (-1, 1) => HitTest.TopRight,
            (0, -1) => HitTest.Left,
            (0, 1) => HitTest.Right,
            (1, -1) => HitTest.BottomLeft,
            (1, 0) => HitTest.Bottom,
            (1, 1) => HitTest.BottomRight,
            _ => HitTest.Client,
        };
    }

    private void BeginWindowMove(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || WindowState != FormWindowState.Normal || sender is not Control surface)
            return;

        Point screen = surface.PointToScreen(e.Location);
        int coordinates = (screen.X & 0xffff) | (screen.Y << 16);
        ReleaseCapture();
        SendMessage(Handle, WmNcLButtonDown, (IntPtr)HitTest.Caption, (IntPtr)coordinates);
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(Math.Max(value, minimum), maximum);

    private void FitToWorkingArea()
    {
        if (IsDisposed || WindowState != FormWindowState.Normal)
            return;
        var area = Screen.FromControl(this).WorkingArea;
        Size minimum = LogicalToDeviceUnits(MinimumClientSize);
        MinimumSize = new Size(Math.Min(minimum.Width, area.Width), Math.Min(minimum.Height, area.Height));
        int width = Math.Min(Width, area.Width);
        int height = Math.Min(Height, area.Height);
        var bounds = new Rectangle(Clamp(Left, area.Left, area.Right - width),
            Clamp(Top, area.Top, area.Bottom - height), width, height);
        if (Bounds != bounds)
            Bounds = bounds;
    }

    private (TableLayoutPanel Panel, Action<int> Arrange, Button Maximize) BuildHeader(Icon icon)
    {
        var header = Row(new Padding(ContentInset, 4, ResizeInset, 4));
        header.Name = "SettingsTitleBar";
        header.ColumnCount = 4;
        header.RowCount = 2;
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var iconColumn = new ColumnStyle(SizeType.Absolute, 36);
        header.ColumnStyles.Add(iconColumn);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var mark = new PictureBox
        {
            Name = "SettingsAppIcon", Size = new Size(28, 28), SizeMode = PictureBoxSizeMode.Zoom,
            Anchor = AnchorStyles.Left, Margin = new Padding(0), TabStop = false,
        };
        void UpdateAppMark()
        {
            using var sizedIcon = new Icon(icon, mark.Size);
            Bitmap? previous = appMark;
            mark.Image = appMark = sizedIcon.ToBitmap();
            previous?.Dispose();
        }
        mark.SizeChanged += (_, _) => UpdateAppMark();
        UpdateAppMark();
        header.Controls.Add(mark, 0, 0);
        var title = TextLabel(Program.WindowTitle, titleFont);
        title.Name = "SettingsWindowTitle";
        title.TextAlign = ContentAlignment.MiddleLeft;
        title.Dock = DockStyle.Fill;
        title.Margin = new Padding(0, 0, 8, 0);
        header.Controls.Add(title, 1, 0);

        var selector = BuildAppearanceSelector();
        selector.TabIndex = 0;
        header.Controls.Add(selector, 2, 0);
        var buttons = Row();
        buttons.Name = "WindowControls";
        buttons.Dock = DockStyle.None;
        buttons.Anchor = AnchorStyles.Right;
        buttons.Margin = new Padding(4, 0, 0, 0);
        buttons.TabIndex = 1;
        var minimize = BuildCaptionButton(CaptionCommand.Minimize);
        minimize.Margin = new Padding(0, 0, 4, 0);
        buttons.Controls.Add(minimize, 0, 0);
        var maximize = BuildCaptionButton(CaptionCommand.Maximize);
        maximize.Margin = new Padding(0, 0, 4, 0);
        maximize.TabIndex = 1;
        buttons.Controls.Add(maximize, 1, 0);
        var close = BuildCaptionButton(CaptionCommand.Close);
        close.TabIndex = 2;
        buttons.Controls.Add(close, 2, 0);
        header.Controls.Add(buttons, 3, 0);

        header.MouseDown += BeginWindowMove;
        mark.MouseDown += BeginWindowMove;
        title.MouseDown += BeginWindowMove;
        bool narrow = false;
        return (header, PositionSelector, maximize);

        void PositionSelector(int availableWidth)
        {
            int requiredWidth = header.Padding.Horizontal + (int)iconColumn.Width
                + title.GetPreferredSize(Size.Empty).Width + title.Margin.Horizontal
                + selector.GetPreferredSize(Size.Empty).Width
                + buttons.GetPreferredSize(Size.Empty).Width + buttons.Margin.Horizontal;
            bool nextNarrow = availableWidth < requiredWidth;
            if (narrow == nextNarrow)
                return;

            narrow = nextNarrow;
            header.SuspendLayout();
            header.SetColumnSpan(title, narrow ? 2 : 1);
            header.SetCellPosition(selector, new TableLayoutPanelCellPosition(narrow ? 0 : 2, narrow ? 1 : 0));
            header.SetColumnSpan(selector, narrow ? 4 : 1);
            selector.Anchor = narrow ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Right;
            selector.Margin = narrow ? new Padding(0, LogicalToDeviceUnits(8), 0, 0) : new Padding(0);
            header.ResumeLayout(performLayout: true);
        }
    }

    private Button BuildCaptionButton(CaptionCommand command)
    {
        var (name, label) = command switch
        {
            CaptionCommand.Minimize => ("MinimizeWindow", "最小化窗口"),
            CaptionCommand.Maximize => ("MaximizeWindow", "最大化窗口"),
            _ => ("CloseWindow", "关闭窗口并收进托盘"),
        };
        bool close = command == CaptionCommand.Close;
        var button = new Button
        {
            Name = name,
            AccessibleName = label,
            CausesValidation = false,
            Size = new Size(36, 36),
        };
        ConfigureButton(button);
        button.FlatAppearance.BorderSize = 0;
        captionToolTip.SetToolTip(button, label);
        BindTheme(button, (control, palette) =>
        {
            control.BackColor = palette.Background;
            control.ForeColor = palette.Ink;
            control.FlatAppearance.MouseOverBackColor = close ? palette.CloseButtonHover : palette.SoftSurface;
            control.FlatAppearance.MouseDownBackColor = close ? palette.CloseButtonPressed : palette.Selection;
        });
        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(button.ForeColor, button.LogicalToDeviceUnits(1));
            int x = button.ClientSize.Width / 2;
            int y = button.ClientSize.Height / 2;
            int arm = button.LogicalToDeviceUnits(5);
            if (close)
            {
                e.Graphics.DrawLine(pen, x - arm, y - arm, x + arm, y + arm);
                e.Graphics.DrawLine(pen, x - arm, y + arm, x + arm, y - arm);
            }
            else if (command == CaptionCommand.Minimize)
            {
                e.Graphics.DrawLine(pen, x - arm, y, x + arm, y);
            }
            else if (WindowState == FormWindowState.Maximized)
            {
                int offset = button.LogicalToDeviceUnits(3);
                var front = new Rectangle(x - arm, y - arm + offset, 2 * arm - offset, 2 * arm - offset);
                Point[] outline =
                [
                    new Point(front.Left + offset, front.Top),
                    new Point(front.Left + offset, y - arm),
                    new Point(x + arm, y - arm),
                    new Point(x + arm, front.Bottom - offset),
                    new Point(front.Right, front.Bottom - offset),
                ];
                e.Graphics.DrawLines(pen, outline);
                e.Graphics.DrawRectangle(pen, front);
            }
            else
            {
                e.Graphics.DrawRectangle(pen, x - arm, y - arm, 2 * arm, 2 * arm);
            }
        };
        button.Click += (_, _) =>
        {
            if (close)
                Close();
            else if (command == CaptionCommand.Minimize)
                WindowState = FormWindowState.Minimized;
            else if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                FitToWorkingArea();
            }
            else
                WindowState = FormWindowState.Maximized;
        };
        return button;
    }

    private void UpdateMaximizeButton(Button maximize, Button resizeGrip)
    {
        bool maximized = WindowState == FormWindowState.Maximized;
        string label = maximized ? "还原窗口" : "最大化窗口";
        if (maximize.AccessibleName == label)
            return;

        maximize.AccessibleName = label;
        captionToolTip.SetToolTip(maximize, label);
        maximize.Invalidate();
        resizeGrip.Visible = !maximized;
        Padding = new Padding(maximized ? 0 : LogicalToDeviceUnits(ResizeInset));
    }

    private TableLayoutPanel BuildAppearanceSelector()
    {
        var selector = Row(new Padding(2));
        selector.Dock = DockStyle.None;
        selector.Anchor = AnchorStyles.Right;
        selector.ColumnCount = appearanceChoices.Length;
        selector.AccessibleName = "外观模式";
        selector.AccessibleRole = AccessibleRole.Grouping;
        selector.Paint += (_, e) => DrawBorder(selector, e, theme.ControlBorder);
        for (int i = 0; i < appearanceChoices.Length; i++)
        {
            RadioButton button = appearanceChoices[i].Button;
            button.Dock = DockStyle.Fill;
            button.TabIndex = i;
            selector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 1));
            selector.Controls.Add(button, i, 0);
        }
        return BindTheme(selector, static (control, palette) => control.BackColor = palette.Surface);
    }

    private static RadioButton AppearanceButton(string name, string text) => new()
    {
        Name = name,
        Text = text,
        Appearance = System.Windows.Forms.Appearance.Button,
        FlatStyle = FlatStyle.Flat,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize = true,
        MinimumSize = new Size(0, 28),
        Padding = new Padding(8, 4, 8, 4),
        Margin = new Padding(1),
        UseVisualStyleBackColor = false,
    };

    private void ApplyAppearanceSelection()
    {
        foreach (var (_, button) in appearanceChoices)
        {
            button.BackColor = button.Checked ? theme.Selection : theme.Surface;
            button.ForeColor = button.Checked ? theme.OnSelection : theme.Ink;
            button.FlatAppearance.BorderColor = button.Checked ? theme.SelectionBorder : theme.Surface;
            button.FlatAppearance.CheckedBackColor = theme.Selection;
            button.FlatAppearance.MouseOverBackColor = button.Checked ? theme.Selection : theme.SoftSurface;
            button.FlatAppearance.MouseDownBackColor = theme.Selection;
        }
    }

    private (TableLayoutPanel Panel, Action<bool> Arrange) BuildProxySection()
    {
        var section = Stack();
        section.Dock = DockStyle.Fill;
        saveStatus.Font = captionFont;
        var heading = SplitRow(TextLabel("HTTP 代理", sectionFont), saveStatus);
        heading.Margin = new Padding(0, 0, 0, 8);
        AddRow(section, heading);

        var fields = Row();
        fields.RowCount = 2;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        var hostField = BuildProxyField("代理主机", host);
        hostField.Margin = new Padding(0, 0, 9, 0);
        var portField = BuildProxyField("代理端口", port);
        portField.TabIndex = 1;
        fields.Controls.Add(hostField, 0, 0);
        fields.Controls.Add(portField, 1, 0);
        AddRow(section, fields);
        validation.Margin = new Padding(0, 5, 0, 0);
        validation.Visible = false;
        AddRow(section, validation);

        ConfigureButton(testButton);
        testButton.Size = new Size(102, 32);
        var checkHeading = SplitRow(TextLabel("连接检测", sectionFont), testButton);
        checkHeading.Margin = new Padding(0, 10, 0, 6);
        AddRow(section, checkHeading);

        var checks = Stack(new Padding(11, 6, 11, 6));
        checks.Dock = DockStyle.Fill;
        BindTheme(checks, static (box, palette) => box.BackColor = palette.ResultsSurface);
        checks.Paint += (_, e) => DrawBorder(checks, e, theme.Border);
        AddRow(checks, BuildDetectionRow("TCP 端口", tcpStatus));
        AddRow(checks, Divider(new Padding(0, 5, 0, 5)));
        AddRow(checks, BuildDetectionRow("HTTP 代理请求", httpStatus));
        AddRow(section, checks);
        return (section, ArrangeFields);

        void ArrangeFields(bool stacked)
        {
            fields.SuspendLayout();
            fields.SetColumnSpan(hostField, stacked ? 2 : 1);
            fields.SetCellPosition(portField, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
            fields.SetColumnSpan(portField, stacked ? 2 : 1);
            hostField.Margin = stacked ? new Padding(0, 0, 0, LogicalToDeviceUnits(8))
                : new Padding(0, 0, LogicalToDeviceUnits(9), 0);
            fields.ResumeLayout(performLayout: true);
        }
    }

    private TableLayoutPanel BuildProxyField(string caption, TextBox input)
    {
        var field = Stack();
        field.Dock = DockStyle.Fill;
        AddRow(field, CaptionLabel(caption));
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        field.Controls.Add(BuildInput(input), 0, field.RowCount++);
        return field;
    }

    private Panel BuildInput(TextBox input)
    {
        var field = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(9, 7, 9, 6),
            TabIndex = ReferenceEquals(input, host) ? 0 : 1,
        };
        input.Font = addressFont;
        input.BorderStyle = BorderStyle.None;
        input.Dock = DockStyle.Fill;
        field.Controls.Add(input);
        field.Click += (_, _) => input.Focus();
        field.Paint += (_, e) =>
        {
            bool invalid = ReferenceEquals(input, host) ? invalidHost : invalidPort;
            Color border = input.Focused ? theme.Focus : theme.ControlBorder;
            DrawBorder(field, e, invalid ? theme.Error : border);
        };
        input.Enter += (_, _) => field.Invalidate();
        input.Leave += (_, _) => field.Invalidate();
        return BindTheme(field, (container, palette) =>
        {
            container.BackColor = palette.Surface;
            input.BackColor = palette.Surface;
            input.ForeColor = palette.Ink;
        });
    }

#if NETFRAMEWORK
    private void UpdateNativeInputFont()
    {
        // The native edit controls need a font sized in pixels for this window's DPI.
        float pixelSize = AddressFontSizeInPoints * DeviceDpi / 72f;
        if (addressFont.Unit == GraphicsUnit.Pixel && addressFont.Size == pixelSize)
            return;

        Font previous = addressFont;
        addressFont = new Font(previous.Name, pixelSize, GraphicsUnit.Pixel);
        host.Font = port.Font = addressFont;
        previous.Dispose();
    }
#endif

    private static TableLayoutPanel BuildDetectionRow(string caption, Label status)
    {
        var row = Row();
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var label = TextLabel(caption);
        label.Margin = new Padding(0, 0, 8, 2);
        label.Anchor = AnchorStyles.Left;
        var value = WithStatusDot(status);
        value.Anchor = AnchorStyles.Right;
        row.Controls.Add(label, 0, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    private static TableLayoutPanel WithStatusDot(Label status)
    {
        var row = Row();
        row.Dock = DockStyle.None;
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var dot = new Panel { Size = new Size(7, 7), Margin = new Padding(0, 0, 6, 0), Anchor = AnchorStyles.Left, TabStop = false };
        dot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(status.ForeColor);
            e.Graphics.FillEllipse(brush, dot.ClientRectangle);
        };
        status.ForeColorChanged += (_, _) => dot.Invalidate();
        status.Margin = new Padding(0);
        status.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        row.Controls.Add(dot, 0, 0);
        row.Controls.Add(status, 1, 0);
        return row;
    }

    private TableLayoutPanel BuildBehaviorSection()
    {
        var section = Stack();
        section.Dock = DockStyle.Fill;
        AddRow(section, TextLabel("启动器选项", sectionFont));
        AddRow(section, BuildSwitchRow(restart, "可能中断 Codex 正在运行的任务"));
        AddRow(section, Divider(new Padding(0, 2, 0, 2)));
        AddRow(section, BuildSwitchRow(startup, "登录 Windows 后在系统托盘运行"));
        return section;
    }

    private TableLayoutPanel BuildSwitchRow(ToggleSwitch toggle, string description)
    {
        toggle.AccessibleDescription = description;
        toggle.Anchor = AnchorStyles.Right;
        var row = Row(new Padding(0, 5, 0, 5));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var copy = Stack();
        copy.Dock = DockStyle.Fill;
        AddRow(copy, TextLabel(toggle.AccessibleName!));
        AddRow(copy, CaptionLabel(description));
        row.Controls.Add(copy, 0, 0);
        row.Controls.Add(toggle, 1, 0);
        return row;
    }

    private (TableLayoutPanel Panel, Action<bool> Arrange) BuildLaunchSection()
    {
        var lastLaunch = Stack();
        lastLaunch.Dock = DockStyle.Fill;
        lastLaunch.Margin = new Padding(0, 0, 12, 0);
        AddRow(lastLaunch, TextLabel("最近一次启动", sectionFont));
        var result = WithStatusDot(launchStatus);
        result.Anchor = AnchorStyles.Left;
        result.Margin = new Padding(0, 3, 0, 0);
        AddRow(lastLaunch, result);
        AddRow(lastLaunch, launchDetails);

        ConfigureButton(launchButton);
        launchButton.Size = new Size(183, 40);
        var row = SplitRow(lastLaunch, launchButton);
        row.RowCount = 2;
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return (row, ArrangeLaunch);

        void ArrangeLaunch(bool stacked)
        {
            row.SuspendLayout();
            row.SetColumnSpan(lastLaunch, stacked ? 2 : 1);
            row.SetCellPosition(launchButton, new TableLayoutPanelCellPosition(stacked ? 0 : 1, stacked ? 1 : 0));
            row.SetColumnSpan(launchButton, stacked ? 2 : 1);
            lastLaunch.Margin = new Padding(0, 0, stacked ? 0 : LogicalToDeviceUnits(12), 0);
            launchButton.Margin = new Padding(0, stacked ? LogicalToDeviceUnits(8) : 0, 0, 0);
            launchButton.Anchor = stacked ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Right;
            row.ResumeLayout(performLayout: true);
        }
    }

    private (TableLayoutPanel Panel, Button ResizeGrip) BuildFooter()
    {
        var footer = Stack();
        footer.Dock = DockStyle.Fill;
        AddRow(footer, Divider(new Padding(0)));
        var note = CaptionLabel("关闭窗口后，启动器继续在托盘运行。");
        note.Margin = new Padding(22, 9, 6, 8);
        var grip = BuildResizeGrip();
        AddRow(footer, SplitRow(note, grip));
        grip.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        return (footer, grip);
    }

    private Button BuildResizeGrip()
    {
        var grip = new Button
        {
            Name = "ResizeWindow",
            AccessibleName = "调整窗口大小",
            AccessibleDescription = "拖动或使用方向键调整窗口大小。按 Enter 或空格恢复默认大小。",
            Cursor = Cursors.SizeNWSE,
            Size = new Size(36, 32),
        };
        ConfigureButton(grip);
        grip.FlatAppearance.BorderSize = 0;
        BindTheme(grip, static (button, palette) =>
        {
            button.BackColor = palette.Background;
            button.ForeColor = palette.Muted;
            button.FlatAppearance.MouseOverBackColor = palette.SoftSurface;
            button.FlatAppearance.MouseDownBackColor = palette.SoftSurface;
        });
        grip.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(grip.ForeColor, grip.LogicalToDeviceUnits(1));
            int x = grip.Width - grip.LogicalToDeviceUnits(8);
            int y = grip.Height - grip.LogicalToDeviceUnits(8);
            for (int length = 4; length <= 12; length += 4)
            {
                int offset = grip.LogicalToDeviceUnits(length);
                e.Graphics.DrawLine(pen, x - offset, y, x, y - offset);
            }
        };

        Point? dragOrigin = null;
        Size dragSize = Size.Empty;
        bool dragged = false;
        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;
            dragOrigin = grip.PointToScreen(e.Location);
            dragSize = Size;
            dragged = false;
            grip.Capture = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (dragOrigin is not Point start || e.Button != MouseButtons.Left)
                return;
            Point point = grip.PointToScreen(e.Location);
            int dx = point.X - start.X;
            int dy = point.Y - start.Y;
            dragged |= Math.Max(Math.Abs(dx), Math.Abs(dy)) >= LogicalToDeviceUnits(3);
            if (dragged)
                ResizeFromGrip(new Size(dragSize.Width + dx, dragSize.Height + dy));
        };
        grip.MouseCaptureChanged += (_, _) =>
        {
            if (!grip.Capture)
                dragOrigin = null;
        };
        grip.Click += (_, e) =>
        {
            if (e is not MouseEventArgs || !dragged)
                ResetWindowSize();
        };
        grip.PreviewKeyDown += (_, e) =>
            e.IsInputKey = e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Enter;
        grip.KeyDown += (_, e) =>
        {
            if (e.Alt || e.Control)
                return;
            int step = LogicalToDeviceUnits(e.Shift ? 64 : 16);
            Size delta = e.KeyCode switch
            {
                Keys.Left => new Size(-step, 0),
                Keys.Right => new Size(step, 0),
                Keys.Up => new Size(0, -step),
                Keys.Down => new Size(0, step),
                _ => Size.Empty,
            };
            if (!delta.IsEmpty)
                ResizeFromGrip(Size + delta);
            else if (e.KeyCode == Keys.Enter)
                ResetWindowSize();
            else
                return;
            e.SuppressKeyPress = true;
        };
        return grip;
    }

    private void ResizeFromGrip(Size requested)
    {
        var area = Screen.FromControl(this).WorkingArea;
        Size = new Size(
            Clamp(requested.Width, MinimumSize.Width, Math.Max(MinimumSize.Width, area.Right - Left)),
            Clamp(requested.Height, MinimumSize.Height, Math.Max(MinimumSize.Height, area.Bottom - Top)));
    }

    private void ResetWindowSize()
    {
        Size = LogicalToDeviceUnits(DefaultClientSize);
        FitToWorkingArea();
    }

    private static void ConfigureButton(Button button)
    {
        button.Margin = new Padding(0);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.UseVisualStyleBackColor = false;
    }

    private void ApplyActionColors()
    {
        ApplyButtonColors(launchButton, theme, primary: true);
        ApplyButtonColors(testButton, theme, primary: false);
    }

    private static void ApplyButtonColors(Button button, LauncherTheme palette, bool primary)
    {
        bool emphasized = button.Enabled && primary;
        button.BackColor = emphasized ? palette.Accent : palette.Surface;
        button.ForeColor = emphasized ? palette.OnAccent : palette.Ink;
        if (!button.Enabled)
        {
            button.BackColor = palette.DisabledSurface;
            button.ForeColor = palette.DisabledInk;
        }
        button.FlatAppearance.BorderColor = emphasized ? palette.Accent : palette.ControlBorder;
        button.FlatAppearance.MouseOverBackColor = emphasized ? palette.AccentHover : palette.SoftSurface;
        button.FlatAppearance.MouseDownBackColor = button.FlatAppearance.MouseOverBackColor;
    }

    private void SetStatus(Label label, string text, StatusKind kind)
    {
        statusKinds[label] = kind;
        label.Text = text;
        label.ForeColor = theme.StatusColor(kind);
    }

    private Panel Divider(Padding margin) => BindTheme(
        new Panel { Dock = DockStyle.Fill, Height = 1, Margin = margin },
        static (line, palette) => line.BackColor = palette.Border);

    private static void DrawBorder(Control control, PaintEventArgs e, Color color)
    {
        if (control.Width == 0 || control.Height == 0)
            return;
        using var pen = new Pen(color);
        e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
    }

    private static TableLayoutPanel Stack(Padding padding = default)
    {
        var table = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Margin = new Padding(0), Padding = padding,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static TableLayoutPanel Row(Padding padding = default)
    {
        var table = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 1, Margin = new Padding(0), Padding = padding,
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table;
    }

    private static TableLayoutPanel SplitRow(Control left, Control right)
    {
        var row = Row();
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        right.Anchor = AnchorStyles.Right;
        row.Controls.Add(left, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private Label CaptionLabel(string text) =>
        BindTheme(TextLabel(text, captionFont), static (label, palette) => label.ForeColor = palette.Muted);

    private T BindTheme<T>(T control, Action<T, LauncherTheme> apply) where T : Control
    {
        themeBindings.Add(palette => apply(control, palette));
        return control;
    }

    private static Label TextLabel(string text, Font? font = null) => new()
    {
        Text = text, Font = font, AutoSize = true,
        Margin = new Padding(0, 0, 0, 2),
    };

    private static void AddRow(TableLayoutPanel table, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.TabIndex = row;
        table.Controls.Add(control, 0, row);
    }

    private void OnSettingsEdited(object? sender, EventArgs e) => SettingsEdited?.Invoke(this, e);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            captionToolTip.Dispose();
            appMark?.Dispose();
            bodyFont.Dispose();
            titleFont.Dispose();
            sectionFont.Dispose();
            captionFont.Dispose();
            addressFont.Dispose();
        }
    }

    private enum CaptionCommand
    {
        Minimize,
        Maximize,
        Close,
    }

    private enum HitTest
    {
        Client = 1,
        Caption = 2,
        Left = 10,
        Right = 11,
        Top = 12,
        TopLeft = 13,
        TopRight = 14,
        Bottom = 15,
        BottomLeft = 16,
        BottomRight = 17,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr updateRegion, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private sealed class BufferedTableLayoutPanel : TableLayoutPanel
    {
        public BufferedTableLayoutPanel() => DoubleBuffered = true;
    }

    private sealed class ToggleSwitch : CheckBox
    {
        private LauncherTheme theme = LauncherTheme.Light;

        public ToggleSwitch()
        {
            AutoSize = false;
            Size = new Size(42, 24);
            Margin = new Padding(0);
            AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            CheckedChanged += (_, _) => Invalidate();
        }

        internal void ApplyTheme(LauncherTheme palette)
        {
            theme = palette;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // The opaque CheckBox skips the framework's background paint.
            using var backgroundBrush = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = LogicalToDeviceUnits(2);
            int height = Height - 2 * inset;
            int width = Width - 2 * inset;
            if (height <= 0 || width < height)
                return;

            bool active = Enabled && Checked;
            Color track = active ? theme.Accent : theme.SwitchOff;
            using var trackBrush = new SolidBrush(track);
            using var knobBrush = new SolidBrush(active ? theme.OnAccent : theme.SwitchThumb);
            using var capsule = new GraphicsPath();
            capsule.AddArc(inset, inset, height, height, 90, 180);
            capsule.AddArc(inset + width - height, inset, height, height, 270, 180);
            capsule.CloseFigure();
            e.Graphics.FillPath(trackBrush, capsule);
            if (!active)
            {
                using var border = new Pen(theme.SwitchOffBorder);
                e.Graphics.DrawPath(border, capsule);
            }
            int padding = LogicalToDeviceUnits(3);
            int knobLeft = inset + padding + (Checked ? width - height : 0);
            e.Graphics.FillEllipse(knobBrush, knobLeft, inset + padding, height - 2 * padding, height - 2 * padding);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, theme.Focus, BackColor);
        }
    }
}

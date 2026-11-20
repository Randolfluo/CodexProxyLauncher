using System.Drawing.Drawing2D;
using static CodexProxyLauncher.DrawingPaths;

namespace CodexProxyLauncher;

internal sealed class TrayQuickMenu : ContextMenuStrip
{
    private const int LogicalDpi = 96;
    private const int MenuWidth = 264;
    private const int MenuInset = 13;
    private const int WmDpiChanged = 0x02e0;
    private const string InvalidAddress = "请在设置中修正代理地址";
    private const string LaunchText = "使用代理启动 Codex";

    private readonly ToolStripLabel title = new("代理启动器");
    private readonly ToolStripLabel address = new(InvalidAddress) { Name = "ProxyAddress" };
    private readonly ToolStripSeparator separator = new();
    private readonly System.Windows.Forms.Timer busyAnimation = new() { Interval = 60 };
    private LauncherTheme palette = LauncherTheme.Light;
    private MenuFonts fonts;
    private int layoutDpi;
    private string? endpoint;
    private bool launching;

    public ToolStripMenuItem LaunchItem { get; } = CreateItem("LaunchCodex", LaunchText);
    public ToolStripMenuItem SettingsItem { get; } = CreateItem("OpenSettings", "打开设置");
    public ToolStripMenuItem ExitItem { get; } = CreateItem("ExitLauncher", "退出启动器");

    public TrayQuickMenu()
    {
        layoutDpi = DeviceDpi;
        fonts = new(layoutDpi);
        AutoSize = false;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        ShowItemToolTips = true;
        AccessibleName = title.Text;
        Items.AddRange([title, address, LaunchItem, SettingsItem, separator, ExitItem]);
        busyAnimation.Tick += (_, _) => LaunchItem.Invalidate();
        ConfigureRows();
        ApplyTheme(palette);
        SetState(null, false);
    }

    internal void ApplyTheme(LauncherTheme theme)
    {
        palette = theme;
        BackColor = theme.MenuSurface;
        Renderer = new MenuRenderer(this, theme);
        UpdateRegion();
        UpdateAnimation();
        Invalidate();
    }

    public void SetState(Uri? proxy, bool isLaunching, bool cleanupPending = false)
    {
        endpoint = proxy?.GetComponents(UriComponents.HostAndPort, UriFormat.SafeUnescaped);
        launching = isLaunching;
        bool busy = launching || cleanupPending;
        address.Text = endpoint is null ? InvalidAddress : $"HTTP  {endpoint}";
        address.ToolTipText = address.Text;
        LaunchItem.Text = launching ? "正在启动 Codex…" : LaunchText;
        LaunchItem.Enabled = endpoint is not null && !busy;
        ExitItem.Enabled = !busy;
        UpdateAnimation();
        Invalidate();
    }

    // The native drop-down layout recalculates its default padding whenever item metrics change.
    protected override Padding DefaultPadding => new(ScalePixel(MenuInset));

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpi(DeviceDpi);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == WmDpiChanged)
            ApplyDpi((int)(message.WParam.ToInt64() & 0xffff));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (layoutDpi > 0)
            UpdateRegion();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateAnimation();
    }

    private void ApplyDpi(int dpi)
    {
        if (dpi <= 0 || dpi == layoutDpi || Disposing || IsDisposed)
            return;

        layoutDpi = dpi;
        MenuFonts previous = fonts;
        fonts = new(dpi);
        previous.Dispose();
        ConfigureRows();
        UpdateRegion();
        Invalidate();
    }

    private void ConfigureRows()
    {
        int width = ScalePixel(MenuWidth);
        int inset = ScalePixel(MenuInset);
        int height = inset * 2;
        SuspendLayout();
        try
        {
            ConfigureRow(title, 20, 8);
            ConfigureRow(address, 28, 8);
            ConfigureRow(LaunchItem, 36, 4);
            ConfigureRow(SettingsItem, 32, 4);
            ConfigureRow(separator, 1, 4);
            ConfigureRow(ExitItem, 32, 0);
            ClientSize = new Size(width, height);
        }
        finally
        {
            ResumeLayout(true);
        }

        void ConfigureRow(ToolStripItem item, int logicalHeight, int logicalGap)
        {
            int rowHeight = ScalePixel(logicalHeight);
            int gap = ScalePixel(logicalGap);
            item.AutoSize = false;
            // WinForms places menu items at x=0. The renderer insets their visible surfaces.
            item.Size = new Size(item is ToolStripMenuItem ? width : width - inset * 2, rowHeight);
            item.Margin = new Padding(0, 0, 0, gap);
            height += rowHeight + gap;
        }
    }

    private void UpdateRegion()
    {
        if (palette.IsHighContrast || ClientSize.Width == 0 || ClientSize.Height == 0)
        {
            Region = null;
            return;
        }

        using GraphicsPath path = RoundedRectangle(ClientRectangle, ScalePixel(12));
        Region = new Region(path);
    }

    private void UpdateAnimation()
    {
        busyAnimation.Enabled = Visible && launching && !palette.IsHighContrast && SystemInformation.IsMenuAnimationEnabled;
    }

    private int ScalePixel(int logical) => (logical * (layoutDpi == 0 ? LogicalDpi : layoutDpi) + LogicalDpi / 2) / LogicalDpi;

    private static ToolStripMenuItem CreateItem(string name, string text) =>
        new(text) { Name = name, DisplayStyle = ToolStripItemDisplayStyle.Text, ShowShortcutKeys = false };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            busyAnimation.Dispose();
            fonts.Dispose();
        }
    }

    private sealed class MenuFonts(int dpi) : IDisposable
    {
        public Font Title { get; } = NewFont("Microsoft YaHei UI", 14, FontStyle.Bold, dpi);
        public Font Action { get; } = NewFont("Microsoft YaHei UI", 13, FontStyle.Regular, dpi);
        public Font Primary { get; } = NewFont("Microsoft YaHei UI", 13, FontStyle.Bold, dpi);
        public Font Detail { get; } = NewFont("Microsoft YaHei UI", 12, FontStyle.Regular, dpi);
        public Font Protocol { get; } = NewFont("Segoe UI", 11, FontStyle.Bold, dpi);
        public Font Endpoint { get; } = NewFont("Consolas", 12, FontStyle.Regular, dpi);

        private static Font NewFont(string family, int pixels, FontStyle style, int dpi) =>
            new(family, pixels * dpi / (float)LogicalDpi, style, GraphicsUnit.Pixel);

        public void Dispose()
        {
            Title.Dispose();
            Action.Dispose();
            Primary.Dispose();
            Detail.Dispose();
            Protocol.Dispose();
            Endpoint.Dispose();
        }
    }

    private sealed class MenuRenderer(TrayQuickMenu menu, LauncherTheme theme) : ToolStripRenderer
    {
        private const TextFormatFlags TextFlags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis |
            TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;

        private Color Ink => theme.IsHighContrast ? SystemColors.MenuText : theme.Ink;
        private Color Muted => theme.IsHighContrast ? SystemColors.MenuText : theme.Muted;
        private Color PanelBorder => ColorFor(0xd8e2da, 0x39483e, SystemColors.MenuText);
        private Color Rule => ColorFor(0xe2e9e3, 0x39483e, SystemColors.MenuText);
        private Color Focus => ColorFor(0x24573f, 0xa1eab2, SystemColors.Highlight);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(theme.MenuSurface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var rect = new RectangleF(0.5f, 0.5f, menu.ClientSize.Width - 1, menu.ClientSize.Height - 1);
            GraphicsState state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = RoundedRectangle(rect, theme.IsHighContrast ? 0 : menu.ScalePixel(12));
            using var pen = new Pen(PanelBorder);
            e.Graphics.DrawPath(pen, path);
            e.Graphics.Restore(state);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle row = GetRow(e.Item);
            using var brush = new SolidBrush(Rule);
            e.Graphics.FillRectangle(brush, row);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // Each native item owns its hit target, text and accessibility. Paint the visual row inside it.
            GraphicsState state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle row = GetRow(e.Item);
            if (ReferenceEquals(e.Item, menu.title))
                DrawWithIcon(e.Graphics, row, menu.title.Text, menu.fonts.Title, Ink, Muted, IconKind.Link);
            else if (ReferenceEquals(e.Item, menu.address))
                DrawAddress(e.Graphics, row);
            else
                DrawAction(e.Graphics, e.Item, row);
            e.Graphics.Restore(state);
        }

        private Rectangle GetRow(ToolStripItem item)
        {
            int inset = menu.ScalePixel(MenuInset);
            return new Rectangle(inset - item.Bounds.Left, 0, menu.ClientSize.Width - 2 * inset, item.Height);
        }

        private void DrawAddress(Graphics graphics, Rectangle row)
        {
            if (menu.endpoint is null)
            {
                Color errorInk = ColorFor(0x863c31, 0xf4c1b2, SystemColors.MenuText);
                FillRow(graphics, row, ColorFor(0xfaefec, 0x3b2b28, SystemColors.Menu));
                DrawWithIcon(graphics, row, InvalidAddress, menu.fonts.Detail, errorInk, errorInk, IconKind.Warning);
                return;
            }

            FillRow(graphics, row, theme.IsHighContrast ? SystemColors.Menu : theme.SoftSurface);
            int padding = menu.ScalePixel(8);
            int dividerX = row.Left + padding + menu.ScalePixel(39);
            TextRenderer.DrawText(graphics, "HTTP", menu.fonts.Protocol,
                new Rectangle(row.Left + padding, 0, dividerX - row.Left - padding, row.Height), Muted, TextFlags);
            using var brush = new SolidBrush(ColorFor(0xccd8cf, 0x54655a, SystemColors.MenuText));
            graphics.FillRectangle(brush, dividerX, padding, menu.ScalePixel(1), row.Height - 2 * padding);
            int endpointX = dividerX + padding;
            TextRenderer.DrawText(graphics, menu.endpoint, menu.fonts.Endpoint,
                new Rectangle(endpointX, 0, row.Right - padding - endpointX, row.Height),
                ColorFor(0x35483d, 0xe1ebe3, SystemColors.MenuText), TextFlags);
        }

        private void DrawAction(Graphics graphics, ToolStripItem item, Rectangle row)
        {
            bool primary = ReferenceEquals(item, menu.LaunchItem);
            (Color background, Color foreground) = ActionColors(item, primary);
            FillRow(graphics, row, background);
            if (item.Selected)
            {
                int inset = menu.ScalePixel(2);
                using GraphicsPath focusPath = RoundedRectangle(Rectangle.Inflate(row, -inset, -inset),
                    theme.IsHighContrast ? 0 : menu.ScalePixel(4));
                Color focusColor = theme.IsHighContrast && item.Enabled ? SystemColors.HighlightText
                    : primary && item.Enabled && !theme.IsHighContrast ? foreground : Focus;
                using var pen = new Pen(focusColor, menu.ScalePixel(1));
                graphics.DrawPath(pen, focusPath);
            }

            IconKind icon = primary ? (menu.launching ? IconKind.Busy : IconKind.Launch)
                : ReferenceEquals(item, menu.SettingsItem) ? IconKind.Settings : IconKind.Exit;
            Color iconColor = !primary && item.Enabled && !theme.IsHighContrast ? Muted : foreground;
            DrawWithIcon(graphics, row, item.Text, primary ? menu.fonts.Primary : menu.fonts.Action,
                foreground, iconColor, icon);
        }

        private (Color Background, Color Foreground) ActionColors(ToolStripItem item, bool primary)
        {
            if (theme.IsHighContrast)
            {
                if (!item.Enabled)
                    return (SystemColors.Menu, SystemColors.GrayText);
                return item.Selected ? (SystemColors.Highlight, SystemColors.HighlightText) : (SystemColors.Menu, Ink);
            }

            if (primary)
            {
                if (menu.launching)
                    return (ColorFor(0xdcece1, 0x31493b), ColorFor(0x28563e, 0xb9e8c8));
                if (!item.Enabled)
                    return (ColorFor(0xe5eae6, 0x303c35), ColorFor(0x59675e, 0xa3b2a7));
                Color background = item.Pressed ? ColorFor(0x214c37, 0x70c88e)
                    : item.Selected ? ColorFor(0x285b42, 0x91e2ac) : theme.Accent;
                return (background, ColorFor(0xffffff, 0x173826));
            }

            if (!item.Enabled)
                return (theme.MenuSurface, ColorFor(0x59675e, 0xa3b2a7));
            Color secondary = item.Pressed ? ColorFor(0xe2ebe4, 0x36453b)
                : item.Selected ? ColorFor(0xeef3ef, 0x2b382f) : theme.MenuSurface;
            return (secondary, Ink);
        }

        private void FillRow(Graphics graphics, Rectangle row, Color background)
        {
            if (background == theme.MenuSurface)
                return;

            using GraphicsPath path = RoundedRectangle(row, theme.IsHighContrast ? 0 : menu.ScalePixel(6));
            using var brush = new SolidBrush(background);
            graphics.FillPath(brush, path);
        }

        private void DrawWithIcon(Graphics graphics, Rectangle row, string? text, Font font,
            Color textColor, Color iconColor, IconKind icon)
        {
            int left = row.Left + menu.ScalePixel(8);
            int iconSize = menu.ScalePixel(icon == IconKind.Warning ? 14 : 16);
            PaintIcon(graphics, icon, left, row.Top + (row.Height - iconSize) / 2, iconSize, iconColor);
            int textLeft = left + menu.ScalePixel(24);
            TextRenderer.DrawText(graphics, text, font,
                new Rectangle(textLeft, row.Top, row.Right - menu.ScalePixel(8) - textLeft, row.Height),
                textColor, TextFlags);
        }

        private Color ColorFor(int light, int dark, Color highContrast = default) =>
            theme.IsHighContrast ? highContrast : Color.FromArgb(unchecked((int)0xff000000) | (theme.IsDark ? dark : light));

        private void PaintIcon(Graphics graphics, IconKind icon, int x, int y, int size, Color color)
        {
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(x, y);
            graphics.ScaleTransform(size / 24f, size / 24f);
            using var pen = new Pen(color, 1.75f)
            {
                StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
            };
            switch (icon)
            {
                case IconKind.Link:
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(10, 13, 11.5f, 15, 14.3f, 15.1f, 16, 13.4f);
                        path.AddLine(16, 13.4f, 19.4f, 10);
                        path.AddBezier(19.4f, 10, 23.1f, 6.3f, 17.5f, 0.7f, 13.8f, 4.4f);
                        path.AddLine(13.8f, 4.4f, 12, 6.2f);
                        path.StartFigure();
                        path.AddBezier(14, 11, 12.5f, 9, 9.7f, 8.9f, 8, 10.6f);
                        path.AddLine(8, 10.6f, 4.6f, 14);
                        path.AddBezier(4.6f, 14, 0.9f, 17.7f, 6.5f, 23.3f, 10.2f, 19.6f);
                        path.AddLine(10.2f, 19.6f, 12, 17.8f);
                        graphics.DrawPath(pen, path);
                    }
                    break;
                case IconKind.Warning:
                    graphics.DrawEllipse(pen, 3, 3, 18, 18);
                    graphics.DrawLine(pen, 12, 7.5f, 12, 12.5f);
                    graphics.DrawLine(pen, 12, 16.5f, 12, 16.6f);
                    break;
                case IconKind.Launch:
                    graphics.DrawPolygon(pen, [new PointF(8, 5.5f), new PointF(20, 12), new PointF(8, 18.5f)]);
                    break;
                case IconKind.Busy:
                    float angle = menu.busyAnimation.Enabled ? (uint)Environment.TickCount % 900 * 0.4f : 0;
                    using (var track = new Pen(Color.FromArgb(72, color), pen.Width))
                        graphics.DrawEllipse(track, 3.5f, 3.5f, 17, 17);
                    graphics.DrawArc(pen, 3.5f, 3.5f, 17, 17, angle - 90, 90);
                    break;
                case IconKind.Settings:
                    graphics.DrawPolygon(pen, [
                        new PointF(10, 3), new PointF(14, 3), new PointF(14.6f, 5.6f), new PointF(16.1f, 6.5f),
                        new PointF(18.7f, 5.7f), new PointF(20.7f, 9.2f), new PointF(18.7f, 11), new PointF(18.7f, 13),
                        new PointF(20.7f, 14.8f), new PointF(18.7f, 18.3f), new PointF(16.1f, 17.5f), new PointF(14.6f, 18.4f),
                        new PointF(14, 21), new PointF(10, 21), new PointF(9.4f, 18.4f), new PointF(7.9f, 17.5f),
                        new PointF(5.3f, 18.3f), new PointF(3.3f, 14.8f), new PointF(5.3f, 13), new PointF(5.3f, 11),
                        new PointF(3.3f, 9.2f), new PointF(5.3f, 5.7f), new PointF(7.9f, 6.5f), new PointF(9.4f, 5.6f)]);
                    graphics.DrawEllipse(pen, 9.25f, 9.25f, 5.5f, 5.5f);
                    break;
                case IconKind.Exit:
                    graphics.DrawLines(pen, new[] { new Point(10, 4), new Point(4, 4), new Point(4, 20), new Point(10, 20) });
                    graphics.DrawLine(pen, 8.5f, 12, 21, 12);
                    graphics.DrawLines(pen, new[] { new Point(17, 8), new Point(21, 12), new Point(17, 16) });
                    break;
            }
            graphics.Restore(state);
        }
    }

    private enum IconKind { Link, Warning, Launch, Busy, Settings, Exit }
}

namespace CodexProxyLauncher;

internal sealed class TrayQuickMenu : ContextMenuStrip
{
    private readonly Font menuFont = new("Microsoft YaHei UI", 9);
    private readonly Font titleFont = new("Microsoft YaHei UI", 10, FontStyle.Bold);
    private readonly ToolStripLabel address = new() { TextAlign = ContentAlignment.MiddleLeft };

    public ToolStripMenuItem LaunchItem { get; } = CreateItem("使用代理启动 Codex");
    public ToolStripMenuItem SettingsItem { get; } = CreateItem("打开设置");
    public ToolStripMenuItem ExitItem { get; } = CreateItem("退出启动器");

    public TrayQuickMenu()
    {
        ShowImageMargin = false;
        ShowCheckMargin = false;
        Padding = new Padding(8);
        Font = menuFont;
        ShowItemToolTips = true;
        address.Margin = new Padding(9, 0, 9, 8);
        Items.AddRange([
            new ToolStripLabel("代理启动器")
            {
                Font = titleFont,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(9, 3, 9, 2),
            },
            address, LaunchItem, SettingsItem, new ToolStripSeparator(), ExitItem
        ]);
        ApplyTheme(LauncherTheme.Light);
    }

    internal void ApplyTheme(LauncherTheme palette)
    {
        BackColor = palette.MenuSurface;
        ForeColor = palette.Ink;
        foreach (ToolStripItem item in Items)
            item.ForeColor = palette.Ink;
        address.ForeColor = palette.Muted;
        ExitItem.ForeColor = palette.Muted;
        Renderer = palette.IsHighContrast
            ? new ToolStripSystemRenderer()
            : new PanelRenderer(LaunchItem, palette);
        Invalidate();
    }

    public void SetProxyAddress(Uri? proxy)
    {
        if (proxy is null)
        {
            address.Text = address.ToolTipText = "请在设置中修正代理地址";
            return;
        }

        string endpoint = proxy.GetComponents(UriComponents.HostAndPort, UriFormat.SafeUnescaped);
        string display = endpoint.Length <= 32 ? endpoint
            : $"{endpoint.Substring(0, 21)}…{endpoint.Substring(endpoint.Length - 10)}";
        address.Text = $"HTTP  {display}";
        address.ToolTipText = $"HTTP  {endpoint}";
    }

    private static ToolStripMenuItem CreateItem(string title) => new(title)
    {
        Padding = new Padding(10, 5, 10, 5),
        Margin = new Padding(0, 2, 0, 2),
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            menuFont.Dispose();
            titleFont.Dispose();
        }
    }

    private sealed class PanelRenderer(ToolStripMenuItem primary, LauncherTheme palette)
        : ToolStripProfessionalRenderer(new PanelColors(palette))
    {
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Color color = ReferenceEquals(e.Item, primary) && primary.Enabled
                ? (e.Item.Selected ? palette.AccentHover : palette.Accent)
                : e.Item.Selected && e.Item.Enabled ? palette.SoftSurface : palette.MenuSurface;
            using var brush = new SolidBrush(color);
            e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!e.Item.Enabled)
                e.TextColor = palette.DisabledInk;
            else if (ReferenceEquals(e.Item, primary))
                e.TextColor = palette.OnAccent;
            base.OnRenderItemText(e);
        }
    }

    private sealed class PanelColors(LauncherTheme palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.MenuSurface;
        public override Color MenuBorder => palette.Border;
        public override Color SeparatorDark => palette.Border;
        public override Color SeparatorLight => palette.MenuSurface;
    }
}

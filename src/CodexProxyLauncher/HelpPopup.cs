using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexProxyLauncher;

internal sealed class HelpPopup : Form
{
    private const int LogicalDpi = 96;
    private const int WsExNoActivate = 0x08000000;
    private const TextFormatFlags TextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

    private readonly string description;
    private readonly System.Windows.Forms.Timer dismissTimer = new() { Interval = 20000 };
    private int textDpi = LogicalDpi;
    private Font headingFont = CreateFont(LogicalDpi, FontStyle.Bold);
    private Font bodyFont = CreateFont(LogicalDpi, FontStyle.Regular);
    private Color borderColor;
    private Rectangle headingBounds;
    private Rectangle bodyBounds;

    public HelpPopup(string heading, string description)
    {
        Text = AccessibleName = heading;
        AccessibleDescription = this.description = description;
        AccessibleRole = AccessibleRole.ToolTip;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        dismissTimer.Tick += (_, _) => Hide();
    }

    public void ApplyTheme(LauncherTheme palette)
    {
        BackColor = palette.Surface;
        ForeColor = palette.Ink;
        borderColor = palette.Border;
        Invalidate();
    }

    public void ShowFor(Control anchor)
    {
        if (!anchor.Visible || anchor.FindForm() is not Form owner || owner.WindowState == FormWindowState.Minimized)
            return;

        int dpi = GetDpiForWindow(anchor.Handle);
        if (textDpi != dpi)
        {
            textDpi = dpi;
            headingFont.Dispose();
            bodyFont.Dispose();
            headingFont = CreateFont(textDpi, FontStyle.Bold);
            bodyFont = CreateFont(textDpi, FontStyle.Regular);
        }

        Rectangle anchorBounds = anchor.RectangleToScreen(anchor.ClientRectangle);
        Rectangle workArea = Screen.FromRectangle(anchorBounds).WorkingArea;
        int horizontalPadding = ScalePixel(12);
        int verticalPadding = ScalePixel(8);
        var availableTextSize = new Size(Math.Max(1, workArea.Width - 2 * horizontalPadding), int.MaxValue);
        using Graphics graphics = anchor.CreateGraphics();
        Size headingSize = TextRenderer.MeasureText(graphics, Text, headingFont, availableTextSize, TextFlags);
        Size bodySize = TextRenderer.MeasureText(graphics, description, bodyFont, availableTextSize, TextFlags);
        int textWidth = Math.Min(Math.Max(headingSize.Width, bodySize.Width), availableTextSize.Width);
        headingBounds = new Rectangle(horizontalPadding, verticalPadding, textWidth, headingSize.Height);
        bodyBounds = new Rectangle(horizontalPadding, headingBounds.Bottom, textWidth, bodySize.Height);

        var popupSize = new Size(textWidth + 2 * horizontalPadding, bodyBounds.Bottom + verticalPadding);
        int x = Math.Max(workArea.Left, Math.Min(anchorBounds.Left, workArea.Right - popupSize.Width));
        int gap = ScalePixel(4);
        int y = anchorBounds.Bottom + gap;
        if (y + popupSize.Height > workArea.Bottom)
            y = Math.Max(workArea.Top, anchorBounds.Top - gap - popupSize.Height);

        Bounds = new Rectangle(new Point(x, y), popupSize);
        using GraphicsPath outline = DrawingPaths.RoundedRectangle(ClientRectangle, ScalePixel(8));
        Region = new Region(outline);
        Invalidate();
        if (!Visible)
            Show(owner);
        dismissTimer.Stop();
        dismissTimer.Start();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int stroke = ScalePixel(1);
        var borderBounds = new RectangleF(stroke / 2f, stroke / 2f, ClientSize.Width - stroke, ClientSize.Height - stroke);
        using GraphicsPath outline = DrawingPaths.RoundedRectangle(borderBounds, ScalePixel(8) - stroke / 2f);
        using var pen = new Pen(borderColor, stroke);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, outline);
        TextRenderer.DrawText(e.Graphics, Text, headingFont, headingBounds, ForeColor, TextFlags);
        TextRenderer.DrawText(e.Graphics, description, bodyFont, bodyBounds, ForeColor, TextFlags);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
            dismissTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            dismissTimer.Dispose();
            headingFont.Dispose();
            bodyFont.Dispose();
        }
    }

    private int ScalePixel(int logical) => (logical * textDpi + LogicalDpi / 2) / LogicalDpi;

    private static Font CreateFont(int dpi, FontStyle style) =>
        new("Microsoft YaHei UI", 12f * dpi / LogicalDpi, style, GraphicsUnit.Pixel);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetDpiForWindow(IntPtr window);
}

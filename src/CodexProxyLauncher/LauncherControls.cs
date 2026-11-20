using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexProxyLauncher;

internal sealed class ControlTransition : IDisposable
{
    private const uint SpiGetClientAreaAnimation = 0x1042;
    private const int DurationMilliseconds = 170;
    private readonly Control control;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 15 };
    private readonly Stopwatch clock = new();
    private float start;
    private float target;

    public ControlTransition(Control control)
    {
        this.control = control;
        timer.Tick += (_, _) => Advance();
    }

    public float Position { get; private set; }

    internal static bool AnimationsEnabled =>
        SystemParametersInfo(SpiGetClientAreaAnimation, 0, out bool enabled, 0) && enabled;

    public void MoveTo(float value, bool animate)
    {
        timer.Stop();
        target = value;
        if (animate && Position != target && control.IsHandleCreated && control.Visible &&
            control.Enabled && AnimationsEnabled)
        {
            start = Position;
            clock.Restart();
            timer.Start();
            return;
        }

        Position = target;
        control.Invalidate();
    }

    private void Advance()
    {
        float elapsed = control.Visible && control.Enabled
            ? Math.Min(1f, (float)(clock.Elapsed.TotalMilliseconds / DurationMilliseconds))
            : 1f;
        float remaining = 1f - elapsed;
        Position = elapsed == 1f
            ? target
            : start + (target - start) * (1f - remaining * remaining * remaining);
        if (elapsed == 1f)
            timer.Stop();
        control.Invalidate();
    }

    internal static Color Blend(Color from, Color to, float position) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * position),
        (int)Math.Round(from.G + (to.G - from.G) * position),
        (int)Math.Round(from.B + (to.B - from.B) * position));

    public void Dispose() => timer.Dispose();

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}

internal sealed class LaunchActionButton : Button
{
    private const string LaunchText = "使用代理启动 Codex";
    private readonly ControlTransition hover;
    private LauncherTheme theme = LauncherTheme.Light;
    private bool busy;

    public LaunchActionButton()
    {
        Text = LaunchText;
        DoubleBuffered = true;
        ResizeRedraw = true;
        hover = new ControlTransition(this);
        MouseEnter += (_, _) => hover.MoveTo(1f, animate: !theme.IsHighContrast);
        MouseLeave += (_, _) => hover.MoveTo(0f, animate: !theme.IsHighContrast);
        EnabledChanged += (_, _) =>
        {
            bool underPointer = Enabled && Visible && IsHandleCreated &&
                ClientRectangle.Contains(PointToClient(MousePosition));
            hover.MoveTo(underPointer ? 1f : 0f, animate: false);
        };
    }

    internal void SetBusy(bool value)
    {
        busy = value;
        Text = busy ? "正在启动 Codex…" : LaunchText;
    }

    internal void ApplyTheme(LauncherTheme palette)
    {
        theme = palette;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (theme.IsHighContrast)
        {
            base.OnPaint(e);
            return;
        }

        Rectangle bounds = ClientRectangle;
        bool pressed = Enabled && Capture && (MouseButtons & MouseButtons.Left) != 0 &&
            bounds.Contains(PointToClient(MousePosition));
        float shade = Enabled ? (pressed ? 1f : hover.Position) : 0f;
        Color fill = busy ? theme.AccentHover : ControlTransition.Blend(BackColor, FlatAppearance.MouseOverBackColor, shade);
        using var background = new SolidBrush(fill);
        e.Graphics.FillRectangle(background, bounds);
        if (!Enabled && !busy)
        {
            using var border = new Pen(FlatAppearance.BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, bounds.Width - 1, bounds.Height - 1);
        }

        Rectangle textBounds = bounds;
        if (pressed && ControlTransition.AnimationsEnabled)
            textBounds.Offset(0, LogicalToDeviceUnits(1));
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, busy ? theme.OnAccent : ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), ForeColor, fill);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            hover.Dispose();
    }
}

using SnapGit.App.Ui;
using SnapGit.Core.Localization;
using SnapGit.Platform.Capture;
using SnapGit.Platform.Shell;

namespace SnapGit.App.Forms;

/// <summary>Base for small always-on-top status windows that must never steal focus (an open menu would close) nor appear in captures.</summary>
public abstract class HudForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    protected HudForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 28, 32);
        ForeColor = Color.White;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowPlacement.ExcludeFromCapture(Handle); // not recorded even if it overlaps the area
    }

    /// <summary>Centres the window horizontally on the monitor that contains <paramref name="anchor"/>, <paramref name="offsetFromTop"/> px below its top.</summary>
    public void PlaceOnMonitorOf(Rectangle anchor, int offsetFromTop)
    {
        var monitor = Screen.FromRectangle(anchor).WorkingArea;
        Location = new Point(monitor.X + (monitor.Width - Width) / 2, monitor.Y + offsetFromTop);
    }
}

/// <summary>Big "3 … 2 … 1" countdown. Esc cancels. Shown without taking focus so an open menu stays open.</summary>
public sealed class CountdownForm : HudForm
{
    private int _remaining;

    private CountdownForm()
    {
        Size = new Size(150, 150);
        Opacity = 0.9;
    }

    /// <returns>true when the countdown ran to the end, false when the user pressed Esc.</returns>
    public static async Task<bool> RunAsync(int seconds, Rectangle monitorHint)
    {
        if (seconds <= 0) return true;

        using var form = new CountdownForm { _remaining = seconds };
        form.PlaceOnMonitorOf(monitorHint, 120);
        form.Show();

        while (form._remaining > 0)
        {
            form.Invalidate();
            for (var i = 0; i < 20; i++) // 20 × 50 ms = 1 s, polling Esc
            {
                if (ScrollInjector.IsKeyDown(Keys.Escape)) return false;
                await Task.Delay(50);
            }
            form._remaining--;
        }
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var font = new Font("Segoe UI", 72f, FontStyle.Bold, GraphicsUnit.Point);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(_remaining.ToString(), font, Brushes.White, ClientRectangle, format);
        using var small = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        e.Graphics.DrawString(Loc.T("Esc to cancel"), small, Brushes.Gray, new RectangleF(0, Height - 24, Width, 20), format);
    }
}

/// <summary>Progress strip shown while a scrolling capture runs.</summary>
public sealed class ScrollProgressForm : HudForm
{
    private readonly Label _label = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };

    public ScrollProgressForm(Rectangle area)
    {
        Size = new Size(420, 44);
        Controls.Add(_label);
        UpdateFrames(1);
        PlaceOnMonitorOf(area, 18);
    }

    public void UpdateFrames(int frames) =>
        _label.Text = Loc.T("Scrolling…  {0} frame(s) captured  —  press Esc to finish", frames);
}

/// <summary>Floating recording indicator with elapsed time and Stop / Cancel.</summary>
public sealed class RecordingBar : HudForm
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Func<TimeSpan> _elapsed;
    private readonly Label _time = new() { AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(6, 9, 10, 0) };
    private bool _blink;

    public RecordingBar(string kind, Func<TimeSpan> elapsed, Action onStop, Action onCancel, Rectangle area)
    {
        _elapsed = elapsed;
        Height = 44;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(10, 6, 8, 6), AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var dot = new Panel { Width = 14, Height = 14, Margin = new Padding(0, 11, 0, 0), BackColor = Color.Crimson };
        var title = new Label { Text = Loc.T("REC {0}", kind), AutoSize = true, ForeColor = Color.White, Margin = new Padding(8, 12, 0, 0) };
        var stop = new Button { Text = Loc.T("■ Stop"), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.Crimson, ForeColor = Color.White, Margin = new Padding(0, 3, 6, 0) };
        stop.FlatAppearance.BorderSize = 0;
        stop.Click += (_, _) => onStop();
        var cancel = new Button { Text = Loc.T("Cancel"), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 66), ForeColor = Color.White, Margin = new Padding(0, 3, 0, 0) };
        cancel.FlatAppearance.BorderSize = 0;
        cancel.Click += (_, _) => onCancel();

        flow.Controls.AddRange([dot, title, _time, stop, cancel]);
        Controls.Add(flow);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _timer.Tick += (_, _) =>
        {
            _blink = !_blink;
            dot.BackColor = _blink ? Color.Crimson : Color.FromArgb(90, 20, 30);
            _time.Text = _elapsed().ToString(@"mm\:ss");
        };
        _timer.Start();
        _time.Text = "00:00";

        // Above the recorded area when there is room, otherwise at the top of the monitor.
        Shown += (_, _) =>
        {
            var monitor = Screen.FromRectangle(area).WorkingArea;
            var y = area.Top - Height - 12;
            if (y < monitor.Top) y = monitor.Top + 12;
            var x = Math.Clamp(area.Left + (area.Width - Width) / 2, monitor.Left, Math.Max(monitor.Left, monitor.Right - Width));
            Location = new Point(x, y);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

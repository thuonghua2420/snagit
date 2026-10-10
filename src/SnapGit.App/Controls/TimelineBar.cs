using System.Drawing.Drawing2D;
using SnapGit.App.Ui;
using SnapGit.Core.Localization;
using SnapGit.Core.Recording;

namespace SnapGit.App.Controls;

/// <summary>
/// The video editor's timeline (source time). The ruler on top scrubs; dragging in the track below selects a range
/// (edges can be dragged, click clears). Removed parts are hatched, captions show as amber bars, the playhead is red.
/// </summary>
public sealed class TimelineBar : Control
{
    private const int RulerH = 24, Pad = 14, HandleGrab = 6;

    private enum Drag { None, Scrub, NewSelection, StartEdge, EndEdge }

    private static readonly Color Accent = Color.FromArgb(0, 120, 215);

    private TimeSpan _duration = TimeSpan.FromSeconds(1);
    private TimeSpan _position;
    private TimeRange? _selection;
    private VideoEditPlan? _plan;
    private Drag _drag;
    private TimeSpan _anchor;
    private Point _downPoint;
    private bool _moved;

    public event Action<TimeSpan>? SeekRequested;
    public event Action? SelectionChanged;

    public TimelineBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Height = 64;
        AccessibleName = Loc.T("Timeline");
        AccessibleRole = AccessibleRole.Slider;
    }

    public TimeSpan Duration
    {
        get => _duration;
        set { _duration = value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(1); Invalidate(); }
    }

    public TimeSpan Position
    {
        get => _position;
        set { if (_position == value) return; _position = value; Invalidate(); }
    }

    public VideoEditPlan? Plan
    {
        get => _plan;
        set { _plan = value; Invalidate(); }
    }

    public TimeRange? Selection
    {
        get => _selection;
        set
        {
            if (Equals(_selection, value)) return;
            _selection = value;
            Invalidate();
            SelectionChanged?.Invoke();
        }
    }

    private float K => DeviceDpi / 96f;

    private int S(int v) => (int)Math.Round(v * K);

    private int TrackLeft => S(Pad);

    private int TrackWidth => Math.Max(10, ClientSize.Width - S(Pad) * 2);

    private int XOf(TimeSpan t) => TrackLeft + (int)Math.Round(TrackWidth * (t.TotalSeconds / _duration.TotalSeconds));

    private TimeSpan TimeAt(int x) =>
        TimeSpan.FromSeconds(Math.Clamp((x - TrackLeft) / (double)TrackWidth, 0, 1) * _duration.TotalSeconds);

    private Rectangle TrackRect => new(TrackLeft, S(RulerH) + S(6), TrackWidth, Math.Max(10, ClientSize.Height - S(RulerH) - S(14)));

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var dark = Theme.IsDark;
        var border = dark ? Color.FromArgb(90, 90, 98) : Color.FromArgb(185, 185, 192);
        var trackBack = dark ? Color.FromArgb(48, 48, 54) : Color.FromArgb(232, 232, 236);
        var kept = dark ? Color.FromArgb(70, 130, 190) : Color.FromArgb(120, 175, 230);
        var track = TrackRect;

        DrawRuler(g, border);

        using (var back = new SolidBrush(trackBack)) g.FillRectangle(back, track);

        // kept parts (everything is kept when there is no plan yet)
        using (var keptBrush = new SolidBrush(kept))
        {
            if (_plan is null) g.FillRectangle(keptBrush, track);
            else foreach (var range in _plan.Keep)
                g.FillRectangle(keptBrush, XOf(range.Start), track.Y, Math.Max(1, XOf(range.End) - XOf(range.Start)), track.Height);
        }

        // removed parts: hatched over the dark track
        if (_plan is not null)
        {
            using var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(150, 150, 155), trackBack);
            var cursor = TimeSpan.Zero;
            foreach (var range in _plan.Keep.Append(new TimeRange(_duration, _duration)))
            {
                if (range.Start > cursor)
                    g.FillRectangle(hatch, XOf(cursor), track.Y, XOf(range.Start) - XOf(cursor), track.Height);
                cursor = range.End;
            }
        }

        // captions
        if (_plan is not null)
        {
            using var amber = new SolidBrush(Color.FromArgb(235, 255, 190, 40));
            foreach (var caption in _plan.Captions)
                g.FillRectangle(amber, XOf(caption.Start), track.Bottom - S(7), Math.Max(S(3), XOf(caption.End) - XOf(caption.Start)), S(5));
        }

        using (var outline = new Pen(border)) g.DrawRectangle(outline, track);

        // selection
        if (_selection is { } selection)
        {
            int x1 = XOf(selection.Start), x2 = XOf(selection.End);
            using (var fill = new SolidBrush(Color.FromArgb(70, Accent))) g.FillRectangle(fill, x1, track.Y, Math.Max(1, x2 - x1), track.Height);
            using var edge = new Pen(Accent, S(3));
            g.DrawLine(edge, x1, track.Y - S(3), x1, track.Bottom + S(3));
            g.DrawLine(edge, x2, track.Y - S(3), x2, track.Bottom + S(3));
        }

        // playhead
        var px = XOf(_position);
        using (var head = new Pen(Color.FromArgb(229, 57, 53), S(2))) g.DrawLine(head, px, S(RulerH) - S(4), px, track.Bottom + S(4));
        using (var flag = new SolidBrush(Color.FromArgb(229, 57, 53)))
            g.FillPolygon(flag, [new Point(px - S(6), S(RulerH) - S(14)), new Point(px + S(6), S(RulerH) - S(14)), new Point(px, S(RulerH) - S(3))]);

        if (Focused)
        {
            using var focus = new Pen(Accent) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focus, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    private void DrawRuler(Graphics g, Color border)
    {
        var steps = new double[] { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800 };
        var step = steps.FirstOrDefault(s => TrackWidth * s / _duration.TotalSeconds >= S(70), steps[^1]);

        using var pen = new Pen(border);
        using var small = new Font(Font.FontFamily, Font.Size - 1f);
        var muted = Theme.IsDark ? Color.FromArgb(170, 170, 178) : Color.FromArgb(95, 95, 102);
        g.DrawLine(pen, TrackLeft, S(RulerH) - 1, TrackLeft + TrackWidth, S(RulerH) - 1);

        for (var t = 0.0; t <= _duration.TotalSeconds + 1e-6; t += step)
        {
            var x = XOf(TimeSpan.FromSeconds(t));
            g.DrawLine(pen, x, S(RulerH) - S(7), x, S(RulerH) - 1);
            TextRenderer.DrawText(g, TimeText.Format(TimeSpan.FromSeconds(t)), small, new Point(x + S(2), 0), muted, TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;

        _downPoint = e.Location;
        _moved = false;
        var time = TimeAt(e.X);

        if (e.Y < S(RulerH))
        {
            _drag = Drag.Scrub;
            SeekRequested?.Invoke(time);
            return;
        }

        if (_selection is { } selection)
        {
            if (Math.Abs(e.X - XOf(selection.Start)) <= S(HandleGrab)) { _drag = Drag.StartEdge; return; }
            if (Math.Abs(e.X - XOf(selection.End)) <= S(HandleGrab)) { _drag = Drag.EndEdge; return; }
        }
        _drag = Drag.NewSelection;
        _anchor = time;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var time = TimeAt(e.X);

        switch (_drag)
        {
            case Drag.Scrub:
                SeekRequested?.Invoke(time);
                break;

            case Drag.NewSelection:
                if (!_moved && Math.Abs(e.X - _downPoint.X) < S(4)) break;
                _moved = true;
                Selection = Ordered(_anchor, time);
                break;

            case Drag.StartEdge when _selection is { } s:
                Selection = Ordered(time, s.End);
                _moved = true;
                break;

            case Drag.EndEdge when _selection is { } s:
                Selection = Ordered(s.Start, time);
                _moved = true;
                break;

            default:
                var overEdge = _selection is { } sel && e.Y >= S(RulerH) &&
                    (Math.Abs(e.X - XOf(sel.Start)) <= S(HandleGrab) || Math.Abs(e.X - XOf(sel.End)) <= S(HandleGrab));
                Cursor = overEdge ? Cursors.SizeWE : e.Y < S(RulerH) ? Cursors.Hand : Cursors.Default;
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        // a plain click in the track drops the selection and moves the playhead there
        if (_drag == Drag.NewSelection && !_moved)
        {
            Selection = null;
            SeekRequested?.Invoke(TimeAt(e.X));
        }
        _drag = Drag.None;
    }

    private static TimeRange Ordered(TimeSpan a, TimeSpan b) => a <= b ? new TimeRange(a, b) : new TimeRange(b, a);

    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}

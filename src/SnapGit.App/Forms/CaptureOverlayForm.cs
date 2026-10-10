using System.Drawing.Drawing2D;
using System.Drawing.Text;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;
using SnapGit.Platform.Shell;

namespace SnapGit.App.Forms;

public enum OverlayMode
{
    /// <summary>Snagit "All-in-One": hover a window/control, click to take it, or drag any rectangle.</summary>
    Region,

    /// <summary>Hover a top-level window and click.</summary>
    Window,

    /// <summary>Press-and-drag to draw any closed shape.</summary>
    FreeHand,

    /// <summary>Drag several rectangles, finish with Enter / right-click.</summary>
    MultiArea,
}

public enum OverlayOutcome
{
    Cancelled,
    Rectangle,
    Window,
    FreeHand,
    MultiArea,
}

/// <summary>What the user picked. Coordinates are virtual-screen pixels.</summary>
public sealed record OverlayResult(OverlayOutcome Outcome, Rectangle Bounds, WindowInfo? Window = null,
    IReadOnlyList<Point>? Path = null, IReadOnlyList<Rectangle>? Regions = null);

/// <summary>
/// Full-virtual-screen overlay over a frozen snapshot, used by every capture mode.
/// Mouse wheel / PageUp/PageDown walk through nested controls under the cursor; arrow keys move the cursor
/// pixel by pixel (Shift = 10 px); a magnifier shows the exact pixel. Esc cancels.
/// </summary>
public sealed class CaptureOverlayForm : Form
{
    private const int DragThreshold = 4;
    private const int WM_DPICHANGED = 0x02E0;
    private const int LoupeSize = 150;
    private const int LoupeZoom = 9;

    private static readonly Color Accent = Color.FromArgb(0, 150, 255);

    private readonly Bitmap _snapshot;
    private readonly Bitmap _dimmed;
    private readonly Rectangle _virtualScreen;
    private readonly IReadOnlyList<WindowInfo> _windows;
    private readonly Func<WindowInfo, Point, IReadOnlyList<WindowInfo>>? _chainResolver;
    private readonly OverlayMode _mode;
    private readonly Size? _fixedSize;

    // current candidate (client coordinates)
    private Rectangle _target;
    private string? _targetTitle;
    private IReadOnlyList<WindowInfo> _chain = [];
    private int _chainDepth;
    private WindowInfo? _hoverWindow;

    private Point _dragStart;
    private bool _mouseDown;
    private bool _dragging;
    private readonly List<Point> _freehand = new();
    private readonly List<Rectangle> _regions = new();
    private Point _cursor;

    public CaptureOverlayForm(Bitmap snapshot, Rectangle virtualScreen, IReadOnlyList<WindowInfo> windows, OverlayMode mode,
        Size? fixedSize = null, Func<WindowInfo, Point, IReadOnlyList<WindowInfo>>? chainResolver = null)
    {
        _snapshot = snapshot;
        _virtualScreen = virtualScreen;
        _windows = windows;
        _mode = mode;
        _fixedSize = mode is OverlayMode.Region or OverlayMode.MultiArea ? fixedSize : null;
        _chainResolver = mode == OverlayMode.Region ? chainResolver : null;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = Cursors.Cross;
        BackColor = Color.Black;
        Bounds = virtualScreen;

        _dimmed = new Bitmap(snapshot);
        using (var g = Graphics.FromImage(_dimmed))
        using (var shade = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
            g.FillRectangle(shade, 0, 0, _dimmed.Width, _dimmed.Height);
    }

    public OverlayResult Result { get; private set; } = new(OverlayOutcome.Cancelled, Rectangle.Empty);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        WindowPlacement.PlaceTopMost(Handle, _virtualScreen);
        Activate();
        WindowPlacement.BringToForeground(Handle);
        _cursor = PointToClient(Cursor.Position);
        UpdateHover(_cursor);
    }

    protected override void WndProc(ref Message m)
    {
        // We place this window with exact pixel bounds across monitors of different DPI; don't let WinForms re-scale it.
        if (m.Msg == WM_DPICHANGED) return;
        base.WndProc(ref m);
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImage(_dimmed, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);

        if (_mode == OverlayMode.FreeHand) DrawFreehand(g);
        else DrawRectangles(g);

        if (!_dragging && !_mouseDown && _mode != OverlayMode.FreeHand) DrawHint(g);
        DrawLoupe(g);
    }

    private void DrawRectangles(Graphics g)
    {
        using var border = new Pen(Accent, 2f) { Alignment = PenAlignment.Outset };

        for (var i = 0; i < _regions.Count; i++)
        {
            g.DrawImage(_snapshot, _regions[i], _regions[i], GraphicsUnit.Pixel);
            g.DrawRectangle(border, _regions[i]);
            DrawBadge(g, (i + 1).ToString(), new Point(_regions[i].Left + 4, _regions[i].Top + 4));
        }

        if (_target.Width > 0 && _target.Height > 0)
        {
            g.DrawImage(_snapshot, _target, _target, GraphicsUnit.Pixel);
            g.DrawRectangle(border, _target);
            DrawLabel(g);
        }
    }

    private void DrawFreehand(Graphics g)
    {
        if (_freehand.Count < 2) { DrawHint(g); return; }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var points = _freehand.Select(p => new PointF(p.X, p.Y)).ToArray();

        if (points.Length < 3) // a polygon needs 3 points; the first moments of a drag are just a line
        {
            using var line = new Pen(Accent, 2f);
            g.DrawLines(line, points);
            g.SmoothingMode = SmoothingMode.Default;
            return;
        }

        using var path = new GraphicsPath();
        path.AddPolygon(points);
        var state = g.Save();
        g.SetClip(path);
        g.DrawImage(_snapshot, ClientRectangle, ClientRectangle, GraphicsUnit.Pixel);
        g.Restore(state);

        using var pen = new Pen(Accent, 2f) { LineJoin = LineJoin.Round, DashStyle = DashStyle.Solid };
        g.DrawPolygon(pen, points);
        g.SmoothingMode = SmoothingMode.Default;
    }

    private void DrawLabel(Graphics g)
    {
        var size = $"{_target.Width} × {_target.Height}";
        var text = _targetTitle is { Length: > 0 } && !_dragging
            ? $"{Truncate(_targetTitle, 40)}   {size}"
            : size;
        if (_chain.Count > 1 && !_dragging) text += $"   ▲▼ {_chainDepth + 1}/{_chain.Count}";

        using var font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var measured = g.MeasureString(text, font);
        var box = new RectangleF(_target.Left, _target.Bottom + 8, measured.Width + 14, measured.Height + 6);

        // Keep the label on screen: flip above/inside when there is no room below.
        if (box.Bottom > ClientSize.Height) box.Y = Math.Max(0, _target.Top - box.Height - 8);
        if (box.Y < 0) box.Y = _target.Top + 8;
        if (box.Right > ClientSize.Width) box.X = ClientSize.Width - box.Width;

        using var back = new SolidBrush(Color.FromArgb(215, 20, 20, 20));
        g.FillRectangle(back, box);
        g.DrawString(text, font, Brushes.White, box.X + 7, box.Y + 3);
    }

    private static void DrawBadge(Graphics g, string text, Point at)
    {
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        var size = g.MeasureString(text, font);
        using var back = new SolidBrush(Accent);
        g.FillRectangle(back, at.X, at.Y, size.Width + 8, size.Height + 2);
        g.DrawString(text, font, Brushes.White, at.X + 4, at.Y + 1);
    }

    private void DrawHint(Graphics g)
    {
        var text = _mode switch
        {
            OverlayMode.Region when _fixedSize is not null => Loc.T("Click to capture a fixed-size region  •  Esc to cancel"),
            OverlayMode.Region => Loc.T("Click a window or drag to select a region  •  Wheel = parent/child  •  Esc to cancel"),
            OverlayMode.Window => Loc.T("Click a window to capture it  •  Esc to cancel"),
            OverlayMode.FreeHand => Loc.T("Hold the mouse button and draw around what you want  •  Esc to cancel"),
            _ => Loc.T("Drag to add regions  •  Enter / right-click = finish  •  Backspace = undo  •  Esc to cancel"),
        };

        using var font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
        var monitor = Screen.FromPoint(Cursor.Position).Bounds;
        var size = g.MeasureString(text, font);
        var x = monitor.X - _virtualScreen.X + (monitor.Width - size.Width) / 2;
        var y = monitor.Y - _virtualScreen.Y + 28;
        var box = new RectangleF(x - 14, y - 6, size.Width + 28, size.Height + 12);

        using var back = new SolidBrush(Color.FromArgb(200, 20, 20, 20));
        g.FillRectangle(back, box);
        g.DrawString(text, font, Brushes.White, x, y);
    }

    /// <summary>Magnifier next to the cursor: exact pixel, crosshair, coordinates and colour.</summary>
    private void DrawLoupe(Graphics g)
    {
        var rect = LoupeRect(_cursor);
        var sourceSize = LoupeSize / LoupeZoom;
        var source = new Rectangle(_cursor.X - sourceSize / 2, _cursor.Y - sourceSize / 2, sourceSize, sourceSize);

        var state = g.Save();
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SetClip(rect);
        using (var back = new SolidBrush(Color.Black)) g.FillRectangle(back, rect);
        g.DrawImage(_snapshot, rect, source, GraphicsUnit.Pixel);

        using var cross = new Pen(Color.FromArgb(200, 255, 80, 80), 1f);
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        g.DrawRectangle(cross, center.X - LoupeZoom / 2f, center.Y - LoupeZoom / 2f, LoupeZoom, LoupeZoom);
        g.Restore(state);

        using var frame = new Pen(Color.White, 2f);
        g.DrawRectangle(frame, rect);

        var screenPoint = new Point(_cursor.X + _virtualScreen.X, _cursor.Y + _virtualScreen.Y);
        var color = _cursor.X >= 0 && _cursor.Y >= 0 && _cursor.X < _snapshot.Width && _cursor.Y < _snapshot.Height
            ? _snapshot.GetPixel(_cursor.X, _cursor.Y)
            : Color.Black;
        var text = $"{screenPoint.X}, {screenPoint.Y}   #{color.R:X2}{color.G:X2}{color.B:X2}";

        using var font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        var size = g.MeasureString(text, font);
        var labelBox = new RectangleF(rect.X, rect.Bottom, Math.Max(rect.Width, size.Width + 10), size.Height + 4);
        using var labelBack = new SolidBrush(Color.FromArgb(220, 20, 20, 20));
        g.FillRectangle(labelBack, labelBox);
        g.DrawString(text, font, Brushes.White, labelBox.X + 5, labelBox.Y + 2);
    }

    private Rectangle LoupeRect(Point cursor)
    {
        var x = cursor.X + 28;
        var y = cursor.Y + 28;
        if (x + LoupeSize > ClientSize.Width) x = cursor.X - 28 - LoupeSize;
        if (y + LoupeSize + 24 > ClientSize.Height) y = cursor.Y - 28 - LoupeSize - 24;
        return new Rectangle(x, y, LoupeSize, LoupeSize);
    }

    private Rectangle LoupeArea(Point cursor)
    {
        var rect = LoupeRect(cursor);
        rect.Inflate(4, 4);
        rect.Height += 32;
        rect.Width = Math.Max(rect.Width, 220);
        return rect;
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            if (_mode == OverlayMode.MultiArea && _regions.Count > 0) FinishMultiArea();
            else Cancel();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        _mouseDown = true;
        _dragStart = e.Location;
        if (_mode == OverlayMode.FreeHand)
        {
            _freehand.Clear();
            _freehand.Add(e.Location);
            _dragging = true;
            Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var previousCursor = _cursor;
        _cursor = e.Location;
        InvalidateLoupe(previousCursor);

        if (_mode == OverlayMode.FreeHand)
        {
            if (_mouseDown && Geometry.Distance(_freehand[^1], e.Location) >= 3)
            {
                _freehand.Add(e.Location);
                Invalidate();
            }
            return;
        }

        if (_mouseDown && _fixedSize is null && _mode != OverlayMode.Window)
        {
            if (!_dragging && (Math.Abs(e.X - _dragStart.X) >= DragThreshold || Math.Abs(e.Y - _dragStart.Y) >= DragThreshold))
                _dragging = true;

            if (_dragging)
            {
                SetTarget(ClampToClient(Rectangle.FromLTRB(
                    Math.Min(_dragStart.X, e.X), Math.Min(_dragStart.Y, e.Y),
                    Math.Max(_dragStart.X, e.X), Math.Max(_dragStart.Y, e.Y))), null);
                return;
            }
        }

        if (!_mouseDown) UpdateHover(e.Location);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_mouseDown) return;
        _mouseDown = false;

        switch (_mode)
        {
            case OverlayMode.FreeHand:
                _dragging = false;
                if (_freehand.Count >= 3) AcceptFreehand();
                else { _freehand.Clear(); Invalidate(); }
                return;

            case OverlayMode.MultiArea:
                if (_target.Width >= 2 && _target.Height >= 2)
                {
                    _regions.Add(_target);
                    _target = Rectangle.Empty;
                    _targetTitle = null;
                    _dragging = false;
                    Invalidate();
                }
                else
                {
                    _dragging = false;
                }
                return;
        }

        if (_target.Width >= 1 && _target.Height >= 1) AcceptRectangle();
        else _dragging = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_chain.Count < 2 || _mouseDown) return;

        // Wheel up = enclosing parent, wheel down = nested child.
        StepChain(e.Delta > 0 ? -1 : 1);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_mode == OverlayMode.MultiArea && _regions.Count > 0) FinishMultiArea();
    }

    // ------------------------------------------------------------------ keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Shift ? 10 : 1;

        switch (e.KeyCode)
        {
            case Keys.Escape:
                Cancel();
                break;
            case Keys.Enter when _mode == OverlayMode.MultiArea && _regions.Count > 0:
                FinishMultiArea();
                break;
            case Keys.Back when _mode == OverlayMode.MultiArea && _regions.Count > 0:
                _regions.RemoveAt(_regions.Count - 1);
                Invalidate();
                break;
            case Keys.Left: NudgeCursor(-step, 0); e.Handled = true; break;
            case Keys.Right: NudgeCursor(step, 0); e.Handled = true; break;
            case Keys.Up: NudgeCursor(0, -step); e.Handled = true; break;
            case Keys.Down: NudgeCursor(0, step); e.Handled = true; break;
            case Keys.PageUp: StepChain(-1); break;
            case Keys.PageDown: StepChain(1); break;
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    private void NudgeCursor(int dx, int dy)
    {
        var position = Cursor.Position;
        Cursor.Position = new Point(position.X + dx, position.Y + dy);
    }

    // ------------------------------------------------------------------ selection

    private void UpdateHover(Point client)
    {
        if (_fixedSize is { } size)
        {
            var rect = new Rectangle(client.X - size.Width / 2, client.Y - size.Height / 2, size.Width, size.Height);
            SetTarget(ClampToClient(rect), null);
            return;
        }

        if (_mode == OverlayMode.FreeHand || _mode == OverlayMode.MultiArea && _mouseDown) return;

        var screenPoint = new Point(client.X + _virtualScreen.X, client.Y + _virtualScreen.Y);
        WindowInfo? hit = null;
        foreach (var window in _windows) // front-most first
        {
            if (!window.Bounds.Contains(screenPoint)) continue;
            hit = window;
            break;
        }

        if (hit is null)
        {
            _hoverWindow = null;
            _chain = [];
            SetTarget(Rectangle.Empty, null);
            return;
        }

        _hoverWindow = hit;
        _chain = _chainResolver is null ? [hit] : _chainResolver(hit, screenPoint);
        _chainDepth = _chain.Count - 1; // start with the innermost control (most specific)
        ApplyChain();
    }

    private void StepChain(int delta)
    {
        if (_chain.Count < 2) return;
        _chainDepth = Math.Clamp(_chainDepth + delta, 0, _chain.Count - 1);
        ApplyChain();
    }

    private void ApplyChain()
    {
        var info = _chain[_chainDepth];
        var local = ClampToClient(new Rectangle(info.Bounds.X - _virtualScreen.X, info.Bounds.Y - _virtualScreen.Y, info.Bounds.Width, info.Bounds.Height));
        var title = string.IsNullOrEmpty(info.Title) ? _hoverWindow?.Title : info.Title;
        SetTarget(local, title);
    }

    private Rectangle ClampToClient(Rectangle r) => Rectangle.Intersect(r, new Rectangle(Point.Empty, ClientSize));

    private void SetTarget(Rectangle target, string? title)
    {
        if (target == _target && title == _targetTitle) return;

        var dirty = _target.IsEmpty ? target : target.IsEmpty ? _target : Rectangle.Union(_target, target);
        dirty.Inflate(60, 60);

        _target = target;
        _targetTitle = title;
        Invalidate(dirty);
    }

    private void InvalidateLoupe(Point previous)
    {
        Invalidate(LoupeArea(previous));
        Invalidate(LoupeArea(_cursor));
    }

    private void AcceptRectangle()
    {
        var bounds = ToScreen(_target);

        if (_mode == OverlayMode.Region && !_dragging && _fixedSize is null && _chain.Count > 0)
        {
            // Name captures after the top-level window: a control's own "title" is its text (an edit box would
            // put its whole content into the file name).
            var picked = _chain[_chainDepth];
            Result = new OverlayResult(OverlayOutcome.Window, bounds, picked with { Bounds = bounds, Title = _hoverWindow?.Title ?? picked.Title });
        }
        else if (_mode == OverlayMode.Window && _hoverWindow is not null)
        {
            Result = new OverlayResult(OverlayOutcome.Window, bounds, _hoverWindow with { Bounds = bounds });
        }
        else
        {
            Result = new OverlayResult(OverlayOutcome.Rectangle, bounds);
        }
        DialogResult = DialogResult.OK;
    }

    private void AcceptFreehand()
    {
        var path = _freehand.Select(p => new Point(p.X + _virtualScreen.X, p.Y + _virtualScreen.Y)).ToList();
        var bounds = Rectangle.FromLTRB(path.Min(p => p.X), path.Min(p => p.Y), path.Max(p => p.X), path.Max(p => p.Y));
        Result = new OverlayResult(OverlayOutcome.FreeHand, bounds, Path: path);
        DialogResult = DialogResult.OK;
    }

    private void FinishMultiArea()
    {
        var regions = _regions.Select(ToScreen).ToList();
        Result = new OverlayResult(OverlayOutcome.MultiArea, regions.Aggregate(Rectangle.Union), Regions: regions);
        DialogResult = DialogResult.OK;
    }

    private Rectangle ToScreen(Rectangle client) => new(client.X + _virtualScreen.X, client.Y + _virtualScreen.Y, client.Width, client.Height);

    private void Cancel()
    {
        Result = new OverlayResult(OverlayOutcome.Cancelled, Rectangle.Empty);
        DialogResult = DialogResult.Cancel;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    protected override void Dispose(bool disposing)
    {
        if (disposing) _dimmed.Dispose();
        base.Dispose(disposing);
    }

    private static class Geometry
    {
        public static float Distance(Point a, Point b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}

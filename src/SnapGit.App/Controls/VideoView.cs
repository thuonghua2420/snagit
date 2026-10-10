using System.Drawing.Drawing2D;
using SnapGit.Core.Localization;
using SnapGit.Core.Recording;

namespace SnapGit.App.Controls;

/// <summary>
/// The video editor's picture: the current frame shown the way the export will look (rotated, flipped, cropped, with captions).
/// In crop mode it shows the whole rotated picture instead, with a draggable crop rectangle (in rotated-picture pixels).
/// </summary>
public sealed class VideoView : Control
{
    [Flags]
    private enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8, Move = 16, New = 32 }

    private const int Grab = 9, MinCrop = 16;

    private Bitmap? _frame;
    private Bitmap? _display;
    private TimeSpan _time;
    private VideoEditPlan? _plan;
    private bool _cropMode;
    private Rectangle _crop;
    private Edge _drag;
    private Point _dragStartScreen;
    private Rectangle _dragStartCrop;

    public event Action? CropChanged;

    public VideoView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.FromArgb(215, 215, 220);
        AccessibleName = Loc.T("Video preview");
        AccessibleRole = AccessibleRole.Graphic;
    }

    public VideoEditPlan? Plan
    {
        get => _plan;
        set { _plan = value; Rebuild(); }
    }

    /// <summary>True while the user draws the crop rectangle.</summary>
    public bool CropMode
    {
        get => _cropMode;
        set
        {
            if (_cropMode == value) return;
            _cropMode = value;
            if (value && _plan is not null) _crop = _plan.Crop is { } c ? _plan.EffectiveCrop : new Rectangle(Point.Empty, _plan.ViewSize);
            Rebuild();
        }
    }

    /// <summary>The crop being drawn, in pixels of the rotated + flipped picture.</summary>
    public Rectangle CropDraft
    {
        get => _crop;
        set { _crop = value; Invalidate(); CropChanged?.Invoke(); }
    }

    internal bool HasFrame => _frame is not null;

    /// <summary>Takes ownership of <paramref name="frame"/>.</summary>
    public void SetFrame(Bitmap frame, TimeSpan time)
    {
        var old = _frame;
        _frame = frame;
        _time = time;
        Rebuild();
        old?.Dispose();
    }

    private void Rebuild()
    {
        var old = _display;
        _display = _frame is not null && _plan is not null ? VideoFrameTransform.Apply(_frame, _plan, _time, includeCrop: !_cropMode) : null;
        old?.Dispose();
        Invalidate();
    }

    // ------------------------------------------------------------------ geometry

    private Size ViewSize => _plan?.ViewSize ?? Size.Empty;

    private Rectangle FitRect()
    {
        var content = _cropMode ? ViewSize : _plan?.OutputSize ?? Size.Empty;
        if (content.Width <= 0 || content.Height <= 0) return Rectangle.Empty;
        var margin = (int)(12 * DeviceDpi / 96f);
        var area = new Rectangle(margin, margin, Math.Max(10, ClientSize.Width - margin * 2), Math.Max(10, ClientSize.Height - margin * 2));
        var scale = Math.Min((float)area.Width / content.Width, (float)area.Height / content.Height);
        var w = Math.Max(1, (int)(content.Width * scale));
        var h = Math.Max(1, (int)(content.Height * scale));
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    private float UnitsToScreen => ViewSize.Width <= 0 ? 1 : (float)FitRect().Width / ViewSize.Width;

    private Rectangle CropOnScreen()
    {
        var fit = FitRect();
        var f = UnitsToScreen;
        return new Rectangle(fit.X + (int)(_crop.X * f), fit.Y + (int)(_crop.Y * f), (int)(_crop.Width * f), (int)(_crop.Height * f));
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var fit = FitRect();

        if (_display is null || fit.IsEmpty)
        {
            TextRenderer.DrawText(g, Loc.T("Loading video…"), Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // The frame is decoded at the video's own size and shrunk ONCE here with a proper filter (bicubic keeps small text readable);
        // when the picture is shown larger than its pixels, bilinear avoids ringing.
        g.InterpolationMode = fit.Width < _display.Width ? InterpolationMode.HighQualityBicubic : InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_display, fit);

        if (!_cropMode) return;

        var crop = CropOnScreen();
        using (var shade = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
        {
            g.FillRectangle(shade, fit.X, fit.Y, fit.Width, crop.Top - fit.Y);
            g.FillRectangle(shade, fit.X, crop.Bottom, fit.Width, fit.Bottom - crop.Bottom);
            g.FillRectangle(shade, fit.X, crop.Top, crop.Left - fit.X, crop.Height);
            g.FillRectangle(shade, crop.Right, crop.Top, fit.Right - crop.Right, crop.Height);
        }

        using (var thirds = new Pen(Color.FromArgb(90, 255, 255, 255)))
        {
            for (var i = 1; i < 3; i++)
            {
                g.DrawLine(thirds, crop.Left + crop.Width * i / 3, crop.Top, crop.Left + crop.Width * i / 3, crop.Bottom);
                g.DrawLine(thirds, crop.Left, crop.Top + crop.Height * i / 3, crop.Right, crop.Top + crop.Height * i / 3);
            }
        }
        using (var frame = new Pen(Color.White, 2)) g.DrawRectangle(frame, crop);

        var s = (int)(5 * DeviceDpi / 96f);
        foreach (var point in HandlePoints(crop))
        {
            var box = new Rectangle(point.X - s, point.Y - s, s * 2, s * 2);
            g.FillRectangle(Brushes.White, box);
            g.DrawRectangle(Pens.Black, box);
        }

        var label = $"{_crop.Width} × {_crop.Height}";
        TextRenderer.DrawText(g, label, Font, new Point(crop.Left + 6, crop.Top + 4), Color.White, Color.FromArgb(160, 0, 0, 0), TextFormatFlags.NoPadding);
    }

    private static Point[] HandlePoints(Rectangle r) =>
    [
        new(r.Left, r.Top), new(r.Right, r.Top), new(r.Left, r.Bottom), new(r.Right, r.Bottom),
        new(r.Left + r.Width / 2, r.Top), new(r.Left + r.Width / 2, r.Bottom), new(r.Left, r.Top + r.Height / 2), new(r.Right, r.Top + r.Height / 2),
    ];

    // ------------------------------------------------------------------ crop interaction

    private Edge HitTest(Point p)
    {
        var crop = CropOnScreen();
        var grab = (int)(Grab * DeviceDpi / 96f);
        var edge = Edge.None;
        if (p.Y >= crop.Top - grab && p.Y <= crop.Bottom + grab)
        {
            if (Math.Abs(p.X - crop.Left) <= grab) edge |= Edge.Left;
            else if (Math.Abs(p.X - crop.Right) <= grab) edge |= Edge.Right;
        }
        if (p.X >= crop.Left - grab && p.X <= crop.Right + grab)
        {
            if (Math.Abs(p.Y - crop.Top) <= grab) edge |= Edge.Top;
            else if (Math.Abs(p.Y - crop.Bottom) <= grab) edge |= Edge.Bottom;
        }
        if (edge != Edge.None) return edge;
        return crop.Contains(p) ? Edge.Move : Edge.New;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_cropMode || e.Button != MouseButtons.Left || FitRect().IsEmpty) return;

        _drag = HitTest(e.Location);
        _dragStartScreen = e.Location;
        _dragStartCrop = _crop;
        if (_drag == Edge.New)
        {
            var start = ToUnits(e.Location);
            _dragStartCrop = new Rectangle(start, Size.Empty);
            _crop = _dragStartCrop;
        }
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_cropMode) return;

        if (_drag == Edge.None)
        {
            var hit = HitTest(e.Location);
            Cursor = hit switch
            {
                Edge.Move => Cursors.SizeAll,
                Edge.New => Cursors.Cross,
                _ when (hit & (Edge.Left | Edge.Right)) != 0 && (hit & (Edge.Top | Edge.Bottom)) != 0 =>
                    (hit.HasFlag(Edge.Left) == hit.HasFlag(Edge.Top)) ? Cursors.SizeNWSE : Cursors.SizeNESW,
                _ when (hit & (Edge.Left | Edge.Right)) != 0 => Cursors.SizeWE,
                _ => Cursors.SizeNS,
            };
            return;
        }

        var f = UnitsToScreen;
        var dx = (int)Math.Round((e.X - _dragStartScreen.X) / f);
        var dy = (int)Math.Round((e.Y - _dragStartScreen.Y) / f);
        var view = new Rectangle(Point.Empty, ViewSize);
        var r = _dragStartCrop;

        if (_drag == Edge.Move)
        {
            r.X = Math.Clamp(r.X + dx, 0, view.Width - r.Width);
            r.Y = Math.Clamp(r.Y + dy, 0, view.Height - r.Height);
        }
        else if (_drag == Edge.New)
        {
            var now = ToUnits(e.Location);
            r = Rectangle.FromLTRB(Math.Min(_dragStartCrop.X, now.X), Math.Min(_dragStartCrop.Y, now.Y), Math.Max(_dragStartCrop.X, now.X), Math.Max(_dragStartCrop.Y, now.Y));
        }
        else
        {
            int left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
            if (_drag.HasFlag(Edge.Left)) left = Math.Clamp(left + dx, 0, right - MinCrop);
            if (_drag.HasFlag(Edge.Right)) right = Math.Clamp(right + dx, left + MinCrop, view.Width);
            if (_drag.HasFlag(Edge.Top)) top = Math.Clamp(top + dy, 0, bottom - MinCrop);
            if (_drag.HasFlag(Edge.Bottom)) bottom = Math.Clamp(bottom + dy, top + MinCrop, view.Height);
            r = Rectangle.FromLTRB(left, top, right, bottom);
        }
        r.Intersect(view);
        _crop = r;
        Invalidate();
        CropChanged?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag == Edge.None) return;
        _drag = Edge.None;
        Capture = false;
        // a click without a drag leaves a degenerate rectangle: fall back to the whole picture
        if (_crop.Width < MinCrop || _crop.Height < MinCrop)
        {
            _crop = new Rectangle(Point.Empty, ViewSize);
            Invalidate();
            CropChanged?.Invoke();
        }
    }

    private Point ToUnits(Point screen)
    {
        var fit = FitRect();
        var f = UnitsToScreen;
        return new Point(Math.Clamp((int)Math.Round((screen.X - fit.X) / f), 0, ViewSize.Width), Math.Clamp((int)Math.Round((screen.Y - fit.Y) / f), 0, ViewSize.Height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frame?.Dispose();
            _display?.Dispose();
        }
        base.Dispose(disposing);
    }
}

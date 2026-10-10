using System.Drawing.Drawing2D;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Tools;

namespace SnapGit.Editor.Controls;

/// <summary>
/// Renders an <see cref="EditorDocument"/> with zoom/pan, forwards mouse input to the active tool
/// and hosts the in-place text editor. All editing logic lives in tools/commands, not here.
/// </summary>
public sealed class EditorCanvas : Control
{
    private const float MinZoom = 0.05f;
    private const float MaxZoom = 16f;
    private const float ViewMargin = 40f;
    private const float HandleSizePx = 9f;

    private readonly EditorContext _context;

    private float _zoom = 1f;
    private PointF _offset;
    private bool _fitMode = true;
    private Bitmap? _lastImage;

    private bool _panning;
    private bool _spaceDown;
    private Point _panStartMouse;
    private PointF _panStartOffset;

    private TextBox? _textBox;
    private TextEditRequest? _editing;

    public EditorCanvas(EditorContext context)
    {
        _context = context;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Color.FromArgb(48, 48, 52);
        Cursor = context.Tool.Cursor;

        context.Invalidated += (_, _) => Invalidate();
        context.ToolChanged += (_, _) => Cursor = _context.Tool.Cursor;
        context.TextEditRequested += (_, request) => BeginTextEdit(request);
        context.Document.Changed += OnDocumentChanged;
    }

    public float Zoom => _zoom;

    /// <summary>True while the in-place text editor is open (the host should not steal keystrokes then).</summary>
    public bool IsEditingText => _textBox is not null;

    public event EventHandler? ZoomChanged;

    #region View

    public void ZoomToFit()
    {
        var image = _context.Document.Image;
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        var fit = Math.Min((ClientSize.Width - ViewMargin * 2) / image.Width, (ClientSize.Height - ViewMargin * 2) / image.Height);
        _fitMode = true;
        SetZoom(Math.Clamp(Math.Min(fit, 1f), MinZoom, MaxZoom), new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
    }

    public void ZoomActual() => ZoomAround(1f);

    public void ZoomIn() => ZoomAround(_zoom * 1.25f);

    public void ZoomOut() => ZoomAround(_zoom / 1.25f);

    private void ZoomAround(float zoom)
    {
        _fitMode = false;
        SetZoom(Math.Clamp(zoom, MinZoom, MaxZoom), new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f));
    }

    /// <summary>Changes zoom keeping the image point under <paramref name="anchor"/> (view coords) fixed.</summary>
    private void SetZoom(float zoom, PointF anchor)
    {
        var image = _context.Document.Image;
        var anchorImage = new PointF((anchor.X - _offset.X) / _zoom, (anchor.Y - _offset.Y) / _zoom);

        _zoom = zoom;
        _context.Zoom = zoom;
        _offset = new PointF(anchor.X - anchorImage.X * zoom, anchor.Y - anchorImage.Y * zoom);

        // In fit mode the image is simply centred.
        if (_fitMode)
            _offset = new PointF((ClientSize.Width - image.Width * zoom) / 2f, (ClientSize.Height - image.Height * zoom) / 2f);

        ClampOffset();
        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampOffset()
    {
        var image = _context.Document.Image;
        float w = image.Width * _zoom, h = image.Height * _zoom;

        float x = w + ViewMargin * 2 <= ClientSize.Width ? (ClientSize.Width - w) / 2f : Math.Clamp(_offset.X, ClientSize.Width - w - ViewMargin, ViewMargin);
        float y = h + ViewMargin * 2 <= ClientSize.Height ? (ClientSize.Height - h) / 2f : Math.Clamp(_offset.Y, ClientSize.Height - h - ViewMargin, ViewMargin);
        _offset = new PointF(x, y);
    }

    private PointF ViewToImage(Point p) => new((p.X - _offset.X) / _zoom, (p.Y - _offset.Y) / _zoom);

    /// <summary>Converts a point in this control's client coordinates to image coordinates.</summary>
    public PointF ClientToImage(Point clientPoint) => ViewToImage(clientPoint);

    private PointF ImageToView(PointF p) => new(p.X * _zoom + _offset.X, p.Y * _zoom + _offset.Y);

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        var image = _context.Document.Image;
        if (!ReferenceEquals(_lastImage, image))
        {
            _lastImage = image;
            if (_fitMode) ZoomToFit();
            else { ClampOffset(); ZoomChanged?.Invoke(this, EventArgs.Empty); }
        }
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _lastImage = _context.Document.Image;
        ZoomToFit();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (!IsHandleCreated) return;
        if (_fitMode) ZoomToFit();
        else { ClampOffset(); Invalidate(); }
    }

    #endregion

    #region Painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        var image = _context.Document.Image;
        var imageRect = new RectangleF(_offset.X, _offset.Y, image.Width * _zoom, image.Height * _zoom);

        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillRectangle(shadow, imageRect.X + 3, imageRect.Y + 4, imageRect.Width, imageRect.Height);

        var state = g.Save();
        g.TranslateTransform(_offset.X, _offset.Y);
        g.ScaleTransform(_zoom, _zoom);
        g.SetClip(new RectangleF(0, 0, image.Width, image.Height));

        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = _zoom < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
        g.DrawImage(image, 0, 0, image.Width, image.Height);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        _context.Document.Paint(g);
        _context.Tool.DrawOverlay(g, _context);
        g.Restore(state);

        DrawSelection(g);
    }

    private void DrawSelection(Graphics g)
    {
        var selection = _context.Selected;
        if (selection.Count == 0) return;

        g.SmoothingMode = SmoothingMode.None;
        using var outline = new Pen(Color.FromArgb(0, 120, 215), 1f) { DashStyle = DashStyle.Dash };

        foreach (var annotation in selection)
        {
            var bounds = annotation.Bounds;
            var topLeft = ImageToView(bounds.Location);
            var viewRect = new RectangleF(topLeft.X, topLeft.Y, bounds.Width * _zoom, bounds.Height * _zoom);
            viewRect.Inflate(3, 3);
            g.DrawRectangle(outline, viewRect.X, viewRect.Y, viewRect.Width, viewRect.Height);
        }

        if (selection.Count != 1) return; // handles only make sense for a single object

        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(Color.FromArgb(0, 120, 215), 1.5f);
        foreach (var handle in selection[0].HandlePoints)
        {
            var p = ImageToView(handle);
            var r = new RectangleF(p.X - HandleSizePx / 2, p.Y - HandleSizePx / 2, HandleSizePx, HandleSizePx);
            g.FillRectangle(fill, r);
            g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
        }
    }

    #endregion

    #region Input

    private ToolMouseEventArgs ToToolArgs(MouseEventArgs e) =>
        new(ViewToImage(e.Location), e.Button, ModifierKeys, e.Clicks);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // also commits a text edit in progress (its LostFocus handler)

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && _spaceDown))
        {
            _panning = true;
            _panStartMouse = e.Location;
            _panStartOffset = _offset;
            Cursor = Cursors.Hand;
            return;
        }

        _context.Tool.MouseDown(_context, ToToolArgs(e));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_panning)
        {
            _fitMode = false;
            _offset = new PointF(_panStartOffset.X + e.X - _panStartMouse.X, _panStartOffset.Y + e.Y - _panStartMouse.Y);
            ClampOffset();
            Invalidate();
            return;
        }

        _context.Tool.MouseMove(_context, ToToolArgs(e));
        if (e.Button == MouseButtons.None) UpdateHoverCursor(ViewToImage(e.Location));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_panning)
        {
            _panning = false;
            Cursor = _spaceDown ? Cursors.Hand : _context.Tool.Cursor;
            return;
        }

        _context.Tool.MouseUp(_context, ToToolArgs(e));
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (ModifierKeys.HasFlag(Keys.Control))
        {
            _fitMode = false;
            var factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
            SetZoom(Math.Clamp(_zoom * factor, MinZoom, MaxZoom), e.Location);
            return;
        }

        _fitMode = false;
        var step = e.Delta * 0.5f;
        _offset = ModifierKeys.HasFlag(Keys.Shift)
            ? new PointF(_offset.X + step, _offset.Y)
            : new PointF(_offset.X, _offset.Y + step);
        ClampOffset();
        Invalidate();
    }

    private void UpdateHoverCursor(PointF imagePoint)
    {
        if (_context.ActiveTool != ToolKind.Select) return;

        var reach = 8f / _zoom;
        if (_context.Selected.Count == 1 && _context.Selected[0].HandlePoints.Any(h => Geometry.Distance(h, imagePoint) <= reach))
            Cursor = Cursors.SizeAll;
        else if (_context.Document.HitTest(imagePoint, 5f / _zoom) is not null)
            Cursor = Cursors.SizeAll;
        else
            Cursor = Cursors.Default;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Enter or Keys.Escape or Keys.Delete or Keys.Space or Keys.Left or Keys.Right or Keys.Up or Keys.Down
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Space && !_spaceDown)
        {
            _spaceDown = true;
            Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (_context.Tool.KeyDown(_context, e.KeyData))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Invalidate();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            _spaceDown = false;
            if (!_panning) Cursor = _context.Tool.Cursor;
        }
    }

    #endregion

    #region In-place text editing

    private void BeginTextEdit(TextEditRequest request)
    {
        EndTextEdit(commit: true);

        var annotation = request.Annotation;
        var style = annotation.Style;
        var topLeft = ImageToView(annotation.Rect.Location);

        var box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(style.FontFamily, Math.Max(6f, style.FontSize * _zoom), style.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
            ForeColor = style.Stroke,
            BackColor = IsLight(style.Stroke) ? Color.FromArgb(70, 70, 74) : Color.White,
            Text = annotation.Text,
            Location = Point.Round(topLeft),
            Size = new Size(Math.Max(80, (int)(annotation.Rect.Width * _zoom)), Math.Max(24, (int)(annotation.Rect.Height * _zoom))),
        };

        box.TextChanged += (_, _) => GrowTextBox(box);
        box.LostFocus += (_, _) => EndTextEdit(commit: true);
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { EndTextEdit(commit: false); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && e.Control) { EndTextEdit(commit: true); e.Handled = true; e.SuppressKeyPress = true; }
        };

        _textBox = box;
        _editing = request;
        Controls.Add(box);
        GrowTextBox(box);
        box.Focus();
        box.SelectAll();
    }

    private static void GrowTextBox(TextBox box)
    {
        var measured = TextRenderer.MeasureText(box.Text + " ", box.Font, new Size(Math.Max(1, box.ClientSize.Width), 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        box.Height = Math.Max(box.Height, measured.Height + 8);
    }

    private static bool IsLight(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 150;

    private void EndTextEdit(bool commit)
    {
        if (_textBox is not { } box) return;

        var request = _editing;
        var text = box.Text;
        _textBox = null;
        _editing = null;

        box.Visible = false;
        Controls.Remove(box);
        BeginInvoke(() => box.Dispose()); // never dispose a control from inside its own event

        if (commit && request is not null)
        {
            using var g = CreateGraphics();
            _context.CommitText(request, text, g);
        }

        Invalidate();
        if (IsHandleCreated && !IsDisposed) Focus();
    }

    #endregion
}


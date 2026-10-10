using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;

namespace SnapGit.App.Controls;

/// <summary>
/// Filmstrip of the most recent captures, docked under the editor canvas. Click (or Enter) opens a tile in the editor;
/// it follows <see cref="ILibraryService.Changed"/>, so a new capture shows up immediately.
/// </summary>
public sealed class HistoryStrip : Control
{
    public const int MaxItems = 30;

    private const int HeaderH = 20, TileW = 92, TileH = 58, LabelH = 14, Gap = 6, Pad = 8, ScrollH = 14, TopGap = 3;

    // Tiles are shared by every editor window; entries are never disposed (a strip may still be painting one), the GC collects them.
    private static readonly ConcurrentDictionary<string, Bitmap> Thumbs = new();

    private readonly AppHost _host;
    private readonly Func<string?> _currentPath;
    private readonly HScrollBar _scroll = new() { Dock = DockStyle.Bottom, Visible = false };
    private readonly ToolTip _tip = new();
    private readonly EventHandler _libraryChanged;
    private IReadOnlyList<LibraryItem> _items = [];
    private int _hover = -1;
    private int _focus = -1;
    private bool _overLink;
    private int _loadVersion;
    private bool _collapsed;

    public HistoryStrip(AppHost host, Func<string?> currentPath)
    {
        _host = host;
        _currentPath = currentPath;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Dock = DockStyle.Bottom;
        ApplyHeight();
        AccessibleName = Loc.T("Recent captures");
        AccessibleRole = AccessibleRole.List;

        _scroll.Scroll += (_, _) => Invalidate();
        Controls.Add(_scroll);

        _libraryChanged = (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke(RefreshItems);
        };
        _host.Library.Changed += _libraryChanged;
        RefreshItems();
    }

    /// <summary>Only the 20 px header is shown (click the title to toggle): the picture area gets the room back.</summary>
    public bool Collapsed
    {
        get => _collapsed;
        set { if (_collapsed == value) return; _collapsed = value; UpdateScroll(); Invalidate(); }
    }

    private void ApplyHeight()
    {
        // header + gap + tiles + caption (+ a scroll bar row only when there are more tiles than fit)
        Height = S(HeaderH) + (_collapsed ? 0 : S(TopGap) + S(TileH) + S(LabelH) + S(3) + (_scroll.Visible ? S(ScrollH) : 0));
    }

    /// <summary>Number of tiles currently shown (test seam).</summary>
    internal int ItemCount => _items.Count;

    internal IReadOnlyList<LibraryItem> Items => _items;

    internal int ThumbnailsLoaded => _items.Count(item => Thumbs.ContainsKey(KeyOf(item)));

    public void RefreshItems()
    {
        if (IsDisposed) return;
        _items = _host.Library.GetItems(new LibraryQuery()).Take(MaxItems).ToList();
        if (_hover >= _items.Count) _hover = -1;
        if (_focus >= _items.Count) _focus = _items.Count - 1;
        UpdateScroll();
        Invalidate();
        _ = LoadThumbnailsAsync(++_loadVersion);
    }

    private async Task LoadThumbnailsAsync(int version)
    {
        foreach (var item in _items.ToList())
        {
            if (version != _loadVersion || IsDisposed) return;
            var key = KeyOf(item);
            if (Thumbs.ContainsKey(key)) continue;

            var tile = await ThumbnailLoader.LoadAsync(item);
            if (IsDisposed) return;
            if (tile is null) continue;
            if (Thumbs.Count > 150) Thumbs.Clear();
            Thumbs[key] = tile;
            Invalidate();
        }
    }

    private static string KeyOf(LibraryItem item) => $"{item.FilePath}|{item.SizeBytes}|{item.CreatedAt.Ticks}";

    // ------------------------------------------------------------------ geometry

    private float K => DeviceDpi / 96f;

    private int S(int value) => (int)Math.Round(value * K);

    private Rectangle TileRect(int index) =>
        new(S(Pad) + index * (S(TileW) + S(Gap)) - _scroll.Value, S(HeaderH) + S(TopGap), S(TileW), S(TileH));

    private int TotalWidth => _items.Count == 0 ? 0 : S(Pad) * 2 + _items.Count * (S(TileW) + S(Gap)) - S(Gap);

    private void UpdateScroll()
    {
        var total = TotalWidth;
        var view = ClientSize.Width;
        if (total > view && view > 0)
        {
            _scroll.Minimum = 0;
            _scroll.Maximum = total - 1;
            _scroll.LargeChange = view;
            _scroll.SmallChange = S(TileW + Gap);
            _scroll.Value = Math.Min(_scroll.Value, Math.Max(0, total - view));
            _scroll.Visible = !_collapsed;
        }
        else
        {
            _scroll.Value = 0;
            _scroll.Visible = false;
        }
        ApplyHeight();
    }

    private Rectangle LinkBounds()
    {
        var size = TextRenderer.MeasureText(LinkText, Font);
        return new Rectangle(ClientSize.Width - size.Width - S(Pad), 0, size.Width, S(HeaderH));
    }

    private static string LinkText => Loc.T("Open library") + " ›";

    private int IndexAt(Point point)
    {
        if (_collapsed || point.Y < S(HeaderH)) return -1;
        for (var i = 0; i < _items.Count; i++)
            if (TileRect(i).Contains(point) || new Rectangle(TileRect(i).X, TileRect(i).Y, TileRect(i).Width, TileRect(i).Height + S(LabelH)).Contains(point))
                return i;
        return -1;
    }

    private void EnsureVisible(int index)
    {
        if (!_scroll.Visible || index < 0) return;
        var tile = TileRect(index);
        var delta = tile.Left < S(Pad) ? tile.Left - S(Pad) : tile.Right > ClientSize.Width - S(Pad) ? tile.Right - (ClientSize.Width - S(Pad)) : 0;
        if (delta != 0) SetScroll(_scroll.Value + delta);
    }

    private void SetScroll(int value)
    {
        var max = Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);
        _scroll.Value = Math.Clamp(value, 0, max);
        Invalidate();
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var palette = Theme.Colors;
        g.Clear(palette.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var bold = new Font(Font, FontStyle.Bold))
            TextRenderer.DrawText(g, Loc.T("History"), bold,
                new Rectangle(S(Pad) + S(12), 0, ClientSize.Width / 2, S(HeaderH)), palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

        // item count next to the title
        using (var bold = new Font(Font, FontStyle.Bold))
        {
            var titleWidth = TextRenderer.MeasureText(Loc.T("History"), bold).Width;
            TextRenderer.DrawText(g, _items.Count.ToString(), Font,
                new Rectangle(S(Pad) + S(12) + titleWidth, 0, S(40), S(HeaderH)), palette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        using (var linkFont = new Font(Font, _overLink ? FontStyle.Underline : FontStyle.Regular))
            TextRenderer.DrawText(g, LinkText, linkFont, LinkBounds(), palette.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

        using (var line = new Pen(palette.Border))
            g.DrawLine(line, 0, 0, ClientSize.Width, 0);

        // fold / unfold chevron before the title
        using (var chevron = new Pen(palette.Muted, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var cx = S(Pad) + 3;
            var cy = S(HeaderH) / 2;
            if (_collapsed) g.DrawLines(chevron, [new Point(cx - 1, cy - 3), new Point(cx + 2, cy), new Point(cx - 1, cy + 3)]);
            else g.DrawLines(chevron, [new Point(cx - 3, cy - 1), new Point(cx, cy + 2), new Point(cx + 3, cy - 1)]);
        }

        if (_collapsed) return;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, Loc.T("Your captures will appear here."), Font,
                new Rectangle(S(Pad), S(HeaderH), ClientSize.Width - S(Pad) * 2, S(TileH)), palette.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            return;
        }

        var current = _currentPath();
        using var labelFont = new Font(Font.FontFamily, Font.Size - 0.75f);
        for (var i = 0; i < _items.Count; i++)
        {
            var rect = TileRect(i);
            if (rect.Right < 0 || rect.Left > ClientSize.Width) continue;
            var item = _items[i];
            var isCurrent = current is not null && string.Equals(Path.GetFullPath(item.FilePath), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase);
            var hot = i == _hover || (i == _focus && Focused);

            // rounded tile: thumbnail clipped to the corners, then a ring (accent for the open picture, soft for hover)
            using var tile = ModernRenderer.RoundedRectangle(rect, S(3));
            using (var back = new SolidBrush(palette.Window)) g.FillPath(back, tile);
            if (Thumbs.TryGetValue(KeyOf(item), out var thumb))
            {
                var state = g.Save();
                g.SetClip(tile, CombineMode.Intersect);
                g.DrawImage(thumb, rect);
                g.Restore(state);
            }

            var ring = isCurrent ? palette.Accent : hot ? Color.FromArgb(160, palette.Accent) : palette.Border;
            using (var pen = new Pen(ring, isCurrent ? S(2) + 0.5f : 1f))
                g.DrawPath(pen, tile);

            TextRenderer.DrawText(g, item.Name, labelFont, new Rectangle(rect.X, rect.Bottom + S(2), rect.Width, S(LabelH)),
                isCurrent ? palette.Accent : palette.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // ------------------------------------------------------------------ input

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        var overLink = e.Y < S(HeaderH) && LinkBounds().Contains(e.Location);
        if (index != _hover)
        {
            _hover = index;
            _tip.SetToolTip(this, index < 0 ? string.Empty : $"{_items[index].Name}\n{_items[index].CreatedAt:g}");
            Invalidate();
        }
        if (overLink != _overLink)
        {
            _overLink = overLink;
            Invalidate();
        }
        Cursor = index >= 0 || overLink ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1 || _overLink)
        {
            _hover = -1;
            _overLink = false;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        if (e.Y < S(HeaderH))
        {
            if (LinkBounds().Contains(e.Location)) _host.ShowLibrary();
            else Collapsed = !_collapsed;   // the title bar folds / unfolds the strip
            return;
        }
        var index = IndexAt(e.Location);
        if (index >= 0) Open(index);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_scroll.Visible) SetScroll(_scroll.Value - e.Delta);
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0) return;
        var next = _focus;
        switch (e.KeyCode)
        {
            case Keys.Left: next = Math.Max(0, (_focus < 0 ? 0 : _focus) - 1); break;
            case Keys.Right: next = Math.Min(_items.Count - 1, _focus + 1); break;
            case Keys.Home: next = 0; break;
            case Keys.End: next = _items.Count - 1; break;
            case Keys.Enter or Keys.Space when _focus >= 0:
                Open(_focus);
                e.Handled = true;
                return;
            default: return;
        }
        e.Handled = true;
        _focus = next;
        EnsureVisible(next);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (_focus < 0 && _items.Count > 0) _focus = 0; Invalidate(); }

    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void Open(int index)
    {
        var path = _items[index].FilePath;
        var current = _currentPath();
        if (current is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
            return; // already the picture in this window
        _host.OpenItem(path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Library.Changed -= _libraryChanged;
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }
}

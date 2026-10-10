using System.Collections.Concurrent;
using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Core.Models;

namespace SnapGit.App.Forms;

/// <summary>
/// The library page of the main window: capture menu, search/filter/sort, and a thumbnail gallery of everything captured so far.
/// Opening an item loads it into the editor in the same window.
/// </summary>
public sealed partial class LibraryView : UserControl
{
    private static readonly LibraryItemKind?[] KindFilters = [null, LibraryItemKind.Image, LibraryItemKind.Project, LibraryItemKind.Animation, LibraryItemKind.Video];

    private readonly AppHost _host;
    private readonly ListView _list;
    private readonly ImageList _thumbs = new() { ImageSize = ThumbnailLoader.Size, ColorDepth = ColorDepth.Depth32Bit };
    private readonly ConcurrentDictionary<string, Bitmap> _thumbCache = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _count = new();
    private readonly EventHandler _libraryChanged;

    private readonly ToolStripTextBox _search = new() { Width = 170, ToolTipText = "Search names and tags" };
    private readonly ToolStripComboBox _kindFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ToolStripComboBox _tagFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ToolStripComboBox _sortBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly ToolStripButton _descending = new() { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text, Text = "↓" };
    private readonly ToolStripButton _favoritesOnly = new() { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Image };
    private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 250 };

    private int _refreshVersion;
    private bool _updatingFilters;
    private bool _stale = true;

    public LibraryView(AppHost host)
    {
        _host = host;

        Dock = DockStyle.Fill;
        AllowDrop = true;

        _contextMenu = BuildContextMenu();

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.LargeIcon,
            LargeImageList = _thumbs,
            MultiSelect = true,
            BorderStyle = BorderStyle.None,
            HideSelection = false,
            ShowGroups = true,
            AllowDrop = true,
        };
        _list.ItemActivate += (_, _) => OpenSelected();
        _list.KeyDown += OnListKeyDown;
        _list.SelectedIndexChanged += (_, _) => UpdateSelectionStatus();
        _list.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) _contextMenu.Show(_list, e.Location); };
        _list.DragEnter += OnDragEnter;
        _list.DragDrop += OnDragDrop;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var status = new StatusStrip();
        status.Items.AddRange([_status, _count]);

        Controls.Add(_list);
        Controls.Add(status);
        Controls.Add(BuildFilterBar());
        Controls.Add(BuildToolbar());

        _searchDelay.Tick += (_, _) => { _searchDelay.Stop(); RefreshItems(); };
        _search.TextChanged += (_, _) => { _searchDelay.Stop(); _searchDelay.Start(); };

        // Changes while the page is hidden are picked up when it is shown again.
        _libraryChanged = (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed) return;
            if (Visible) BeginInvoke(RefreshItems);
            else _stale = true;
        };
        _host.Library.Changed += _libraryChanged;

        VisibleChanged += (_, _) => { if (Visible && _stale) RefreshItems(); };
        Theme.ApplyTo(this);
        RefreshItems();
    }

    /// <summary>The window this page is on (owner for dialogs).</summary>
    private Form? Window => FindForm();

    // Test seams (visible to SnapGit.Tests only).
    internal ListView List => _list;

    private LibraryQuery CurrentQuery() => new()
    {
        Text = _search.Text,
        Kind = _kindFilter.SelectedIndex is > 0 and var k && k < KindFilters.Length ? KindFilters[k] : null,
        Tag = _tagFilter.SelectedIndex > 0 ? _tagFilter.SelectedItem as string : null,
        FavoritesOnly = _favoritesOnly.Checked,
        Sort = (LibrarySort)Math.Max(0, _sortBox.SelectedIndex),
        Descending = _descending.Checked,
    };

    // ------------------------------------------------------------------ refresh & thumbnails

    internal void RefreshItems()
    {
        if (_updatingFilters || IsDisposed) return;
        _stale = false;
        var version = ++_refreshVersion;

        RefreshTagFilter();
        var query = CurrentQuery();
        var items = _host.Library.GetItems(query);
        var selectedPaths = _list.SelectedItems.Cast<ListViewItem>().Select(i => (i.Tag as LibraryItem)?.FilePath).ToHashSet();

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        _thumbs.Images.Clear();

        var placeholder = new Bitmap(ThumbnailLoader.Size.Width, ThumbnailLoader.Size.Height);
        using (var g = Graphics.FromImage(placeholder)) g.Clear(Color.FromArgb(232, 232, 236));
        _thumbs.Images.Add("placeholder", placeholder);

        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var item in items)
        {
            var row = new ListViewItem(item.Name)
            {
                Tag = item,
                ImageKey = "placeholder",
                ToolTipText = $"{item.FilePath}\n{item.CreatedAt:g}  •  {item.SizeBytes / 1024.0:0.#} KB" + (item.Tags.Count > 0 ? "\n#" + string.Join(" #", item.Tags) : string.Empty),
                Selected = selectedPaths.Contains(item.FilePath),
            };

            if (query.Sort == LibrarySort.Date)
            {
                var title = DateGroup(item.CreatedAt);
                if (!groups.TryGetValue(title, out var group)) groups[title] = group = _list.Groups.Add(title, title);
                row.Group = group;
            }
            _list.Items.Add(row);
        }
        _list.ShowGroups = query.Sort == LibrarySort.Date;
        _list.EndUpdate();

        _count.Text = Loc.T("{0} item(s)", items.Count);
        _status.Text = _host.Library.Folder;

        _ = LoadThumbnailsAsync(items, version);
    }

    private static string DateGroup(DateTime when)
    {
        var days = (DateTime.Today - when.Date).TotalDays;
        return days < 1 ? Loc.T("Today") : days < 2 ? Loc.T("Yesterday") : days < 7 ? Loc.T("Last 7 days") : days < 31 ? Loc.T("This month") : Loc.T("Earlier");
    }

    private void RefreshTagFilter()
    {
        _updatingFilters = true;
        try
        {
            var selected = _tagFilter.SelectedItem as string;
            _tagFilter.Items.Clear();
            _tagFilter.Items.Add(Loc.T("All tags"));
            foreach (var tag in _host.Library.AllTags()) _tagFilter.Items.Add(tag);
            _tagFilter.SelectedItem = selected is not null && _tagFilter.Items.Contains(selected) ? selected : _tagFilter.Items[0];
        }
        finally
        {
            _updatingFilters = false;
        }
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<LibraryItem> items, int version)
    {
        foreach (var item in items)
        {
            if (version != _refreshVersion || IsDisposed) return;

            var key = $"{item.FilePath}|{item.SizeBytes}|{item.CreatedAt.Ticks}";
            if (!_thumbCache.TryGetValue(key, out var thumb))
            {
                var loaded = await ThumbnailLoader.LoadAsync(item);
                if (loaded is null) continue;
                _thumbCache[key] = thumb = loaded;
            }

            if (version != _refreshVersion || IsDisposed) return;

            var imageKey = item.IsFavorite ? key + "|fav" : key;
            if (!_thumbs.Images.ContainsKey(imageKey))
            {
                using var shown = item.IsFavorite ? ThumbnailLoader.WithFavoriteBadge(thumb) : null;
                _thumbs.Images.Add(imageKey, shown ?? thumb);
            }

            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag is LibraryItem tagged && tagged.FilePath == item.FilePath)
                {
                    row.ImageKey = imageKey;
                    break;
                }
            }
        }
    }

    // ------------------------------------------------------------------ selection helpers

    private IReadOnlyList<LibraryItem> SelectedItems() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<LibraryItem>().ToList();

    private void UpdateSelectionStatus()
    {
        var selected = SelectedItems();
        _status.Text = selected.Count switch
        {
            0 => _host.Library.Folder,
            1 => selected[0].FilePath,
            _ => Loc.T("{0} selected", selected.Count),
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Library.Changed -= _libraryChanged;
            _searchDelay.Dispose();
            foreach (var bmp in _thumbCache.Values) bmp.Dispose();
            _thumbs.Dispose();
        }
        base.Dispose(disposing);
    }
}

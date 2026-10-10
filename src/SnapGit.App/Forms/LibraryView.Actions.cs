using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Core.Settings;
using SnapGit.Editor.Persistence;
using SnapGit.Platform.Shell;

namespace SnapGit.App.Forms;

/// <summary>Toolbar, context menu and every library command.</summary>
public sealed partial class LibraryView
{
    private readonly ContextMenuStrip _contextMenu;
    private ToolStripButton _backButton = new();

    // ================================================================== toolbar & menus

    private ToolStrip BuildToolbar()
    {
        var strip = Toolbars.NewBar(DockStyle.Top);

        _backButton = Toolbars.Button(UiIcon.Back, Loc.T("Back to what you were editing"), () => _host.MainWindow.GoBack(), Loc.T("Back"));
        strip.Items.Add(_backButton);
        strip.Items.Add(Toolbars.Separator());
        VisibleChanged += (_, _) => { if (Visible) _backButton.Enabled = _host.MainWindow.CanGoBack; };

        var capture = Toolbars.PrimaryMenu(UiIcon.Capture, Loc.T("Capture"), Loc.T("Take a capture"));
        CaptureMenu.Fill(capture.DropDownItems, _host);
        strip.Items.Add(capture);
        strip.Items.Add(Toolbars.Button(UiIcon.Import, Loc.T("Add image, GIF or video files to the library"), ImportFiles, Loc.T("Import")));
        strip.Items.Add(Toolbars.Separator());

        strip.Items.Add(Toolbars.Button(UiIcon.Edit, Loc.T("Open (Enter)"), OpenSelected, Loc.T("Open")));
        strip.Items.Add(Toolbars.Button(UiIcon.Copy, Loc.T("Copy (Ctrl+C)"), CopySelected));

        var share = Toolbars.Menu(UiIcon.Share, Loc.T("Share"));
        share.DropDownOpening += (_, _) => FillShareMenu(share.DropDownItems);
        share.DropDownItems.Add("…");
        strip.Items.Add(share);

        strip.Items.Add(Toolbars.Button(UiIcon.Tag, Loc.T("Tags… (Ctrl+T)"), EditTags));
        strip.Items.Add(Toolbars.Button(UiIcon.Star, Loc.T("Toggle favorite (Ctrl+D)"), ToggleFavorite));
        strip.Items.Add(Toolbars.Button(UiIcon.Delete, Loc.T("Delete (Del)"), DeleteSelected));
        strip.Items.Add(Toolbars.Separator());

        strip.Items.Add(Toolbars.Button(UiIcon.Combine, Loc.T("Combine selected images into one"), CombineSelected));
        strip.Items.Add(Toolbars.Button(UiIcon.Pdf, Loc.T("Export selected as PDF"), ExportPdf));
        strip.Items.Add(Toolbars.Button(UiIcon.Image, Loc.T("Convert / resize selected (batch)"), BatchConvert));

        var about = Toolbars.Button(UiIcon.Info, Loc.T("About"), () => _host.ShowAbout(Window));
        var settings = Toolbars.Button(UiIcon.Settings, Loc.T("Settings"), () => _host.ShowSettings(Window));
        var folder = Toolbars.Button(UiIcon.Folder, Loc.T("Open the library folder"), OpenFolder);
        foreach (var item in new[] { about, settings, folder }) item.Alignment = ToolStripItemAlignment.Right;
        strip.Items.AddRange([about, settings, folder]);
        return strip;
    }

    private ToolStrip BuildFilterBar()
    {
        var strip = Toolbars.NewBar(DockStyle.Top);
        strip.Padding = new Padding(10, 2, 10, 4);

        _search.Text = string.Empty;
        _search.ToolTipText = Loc.T("Search names and tags");
        strip.Items.Add(new ToolStripLabel(Icons.Get(UiIcon.Search)));
        strip.Items.Add(_search);

        _kindFilter.Items.AddRange([Loc.T("All types"), Loc.T("Images"), Loc.T("Projects"), Loc.T("GIFs"), Loc.T("Videos")]);
        _kindFilter.SelectedIndex = 0;
        _kindFilter.SelectedIndexChanged += (_, _) => RefreshItems();
        strip.Items.Add(_kindFilter);

        _tagFilter.Items.Add(Loc.T("All tags"));
        _tagFilter.SelectedIndex = 0;
        _tagFilter.SelectedIndexChanged += (_, _) => RefreshItems();
        strip.Items.Add(_tagFilter);

        _favoritesOnly.Image = Icons.Get(UiIcon.StarFilled);
        _favoritesOnly.ToolTipText = Loc.T("Show favorites only");
        _favoritesOnly.CheckedChanged += (_, _) => RefreshItems();
        strip.Items.Add(_favoritesOnly);

        _sortBox.Items.AddRange([Loc.T("Date"), Loc.T("Name"), Loc.T("Size"), Loc.T("Type")]);
        _sortBox.SelectedIndex = 0;
        _sortBox.SelectedIndexChanged += (_, _) => RefreshItems();
        strip.Items.Add(_sortBox);
        _descending.ToolTipText = Loc.T("Newest / largest first");
        _descending.CheckedChanged += (_, _) => { _descending.Text = _descending.Checked ? "↓" : "↑"; RefreshItems(); };
        strip.Items.Add(_descending);

        return strip;
    }

    private static ToolStripMenuItem MenuItem(string text, UiIcon? icon, Action onClick)
    {
        var item = new ToolStripMenuItem(text, icon is { } i ? Icons.Get(i) : null);
        item.Click += (_, _) => onClick();
        return item;
    }

    private static ToolStripButton Button(UiIcon icon, string? text, string tip, Action onClick)
    {
        var button = new ToolStripButton(text, Icons.Get(icon))
        {
            ToolTipText = tip,
            DisplayStyle = text is null ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip { ImageScalingSize = new Size(16, 16) };
        menu.Items.Add(MenuItem(Loc.T("Open"), UiIcon.Edit, OpenSelected));
        menu.Items.Add(MenuItem(Loc.T("Copy"), UiIcon.Copy, CopySelected));
        menu.Items.Add(MenuItem(Loc.T("Copy file"), null, CopyFile));
        var share = new ToolStripMenuItem(Loc.T("Share"), Icons.Get(UiIcon.Share));
        share.DropDownOpening += (_, _) => FillShareMenu(share.DropDownItems);
        share.DropDownItems.Add("…");
        menu.Items.Add(share);
        menu.Items.Add(MenuItem(Loc.T("Show in Explorer"), UiIcon.Folder, ShowInExplorer));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem(Loc.T("Rename…"), null, Rename));
        menu.Items.Add(MenuItem(Loc.T("Tags…"), UiIcon.Tag, EditTags));
        menu.Items.Add(MenuItem(Loc.T("Favorite"), UiIcon.StarFilled, ToggleFavorite));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem(Loc.T("Combine into one image…"), UiIcon.Combine, CombineSelected));
        menu.Items.Add(MenuItem(Loc.T("Create step-by-step document…"), null, StepDocument));
        menu.Items.Add(MenuItem(Loc.T("Export as PDF…"), UiIcon.Pdf, ExportPdf));
        menu.Items.Add(MenuItem(Loc.T("Convert / resize…"), UiIcon.Image, BatchConvert));
        menu.Items.Add(MenuItem(Loc.T("Edit video / trim GIF…"), UiIcon.Trim, TrimSelected));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem(Loc.T("Delete"), UiIcon.Delete, DeleteSelected));
        menu.Opening += (_, e) => e.Cancel = _list.SelectedItems.Count == 0;
        return menu;
    }

    private void FillShareMenu(ToolStripItemCollection items)
    {
        items.Clear();
        foreach (var destination in ShareService.BuiltIn.Concat(_host.Settings.Current.ShareDestinations))
        {
            var captured = destination;
            items.Add(MenuItem(Loc.T(destination.Name), null, () => ShareSelected(captured)));
        }
    }

    // ================================================================== open / copy / share

    /// <summary>Opens the selected item in this window (images/projects → editor, videos → video editor, GIFs → default app).</summary>
    private void OpenSelected()
    {
        if (SelectedItems().FirstOrDefault() is { } item) _host.OpenItem(item.FilePath);
    }

    /// <summary>A bitmap of the item as the user sees it: projects are flattened with their annotations.</summary>
    internal static Bitmap LoadFlattened(string path)
    {
        if (!SnapProjectFile.IsProject(path)) return ImageEncoder.LoadUnlocked(path);

        using var document = ProjectStorage.Load(path);
        return document.Render();
    }

    private void CopySelected()
    {
        if (SelectedItems().FirstOrDefault(i => i.Kind is LibraryItemKind.Image or LibraryItemKind.Project or LibraryItemKind.Animation) is { } item)
        {
            using var image = LoadFlattened(item.FilePath);
            ClipboardService.SetImage(image);
            _status.Text = Loc.T("Copied {0} to the clipboard.", item.Name);
        }
        else
        {
            CopyFile();
        }
    }

    private void CopyFile()
    {
        var paths = SelectedItems().Select(i => i.FilePath).ToArray();
        if (paths.Length == 0) return;
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(paths);
        Clipboard.SetFileDropList(list);
        _status.Text = Loc.T("File(s) copied to the clipboard.");
    }

    private void ShowInExplorer()
    {
        if (SelectedItems().FirstOrDefault() is { } item) ShareService.ShowInExplorer(item.FilePath);
    }

    private void ShareSelected(ShareDestination destination)
    {
        if (SelectedItems().FirstOrDefault() is not { } item) return;
        try
        {
            var path = item.FilePath;
            if (item.Kind == LibraryItemKind.Project)
            {
                // Share the picture, not the project container.
                path = Path.Combine(Path.GetTempPath(), item.Name + ".png");
                using var flat = LoadFlattened(item.FilePath);
                ImageEncoder.Save(flat, path, ImageFileFormat.Png);
            }
            _status.Text = _host.Sharing.Share((IWin32Window?)Window ?? this, path, destination);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window, ex.Message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ================================================================== organise

    private void TrimSelected()
    {
        if (SelectedItems().FirstOrDefault(i => i.Kind is LibraryItemKind.Animation or LibraryItemKind.Video) is not { } item)
        {
            MessageBox.Show(Window, Loc.T("Select a GIF or a video to trim."), "SnapGit");
            return;
        }
        if (item.Kind == LibraryItemKind.Video)
        {
            _host.OpenVideoEditor(item.FilePath); // the full video editor (cut, crop, rotate, text…) replaces the old two-slider dialog
            return;
        }
        using var form = new TrimForm(_host, item);
        form.ShowDialog(Window);
    }

    private void Rename()
    {
        if (SelectedItems().FirstOrDefault() is not { } item) return;
        var dialog = new ParameterDialog(Loc.T("Rename")).AddText("name", Loc.T("New name"), item.Name, 300);
        if (!dialog.Prompt(Window)) return;

        try { _host.Library.Rename(item, dialog.String("name")); }
        catch (Exception ex) { MessageBox.Show(Window, ex.Message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void EditTags()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;

        var existing = _host.Library.AllTags();
        var dialog = new ParameterDialog(Loc.T("Tags"))
            .AddLabel(items.Count == 1
                ? Loc.T("Comma-separated tags for this item.")
                : Loc.T("Tags to ADD to the {0} selected items (comma-separated).", items.Count))
            .AddText("tags", Loc.T("Tags"), items.Count == 1 ? string.Join(", ", items[0].Tags) : string.Empty, 300);
        if (existing.Count > 0) dialog.AddLabel(Loc.T("In use: {0}", string.Join(", ", existing)));
        if (!dialog.Prompt(Window)) return;

        var entered = dialog.String("tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var item in items)
            _host.Library.SetTags(item, items.Count == 1 ? entered : item.Tags.Concat(entered));
    }

    private void ToggleFavorite()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        var makeFavorite = items.Any(i => !i.IsFavorite);
        foreach (var item in items) _host.Library.SetFavorite(item, makeFavorite);
    }

    private void DeleteSelected()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;

        var answer = MessageBox.Show(Window, Loc.T("Delete {0} selected item(s)? This cannot be undone.", items.Count), "SnapGit",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        foreach (var item in items)
        {
            try { _host.Library.Delete(item); }
            catch (Exception ex) { MessageBox.Show(Window, ex.Message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private void OpenFolder()
    {
        Directory.CreateDirectory(_host.Library.Folder);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_host.Library.Folder) { UseShellExecute = true });
    }

    // ================================================================== import / paste / drop

    private void ImportFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = Loc.T("Images, GIFs and videos") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.avi;*.wmv;*.mkv;*.webm;*.snapgit|" + Loc.T("All files") + "|*.*",
        };
        if (dialog.ShowDialog(Window) == DialogResult.OK) Import(dialog.FileNames);
    }

    private void Import(IEnumerable<string> files)
    {
        var count = 0;
        foreach (var file in files.Where(LibraryItem.IsSupported))
        {
            try { _host.Library.Import(file); count++; }
            catch (Exception ex) { _status.Text = Loc.T("Could not import {0}: {1}", Path.GetFileName(file), ex.Message); }
        }
        if (count > 0) _status.Text = Loc.T("Imported {0} file(s).", count);
    }

    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsFileDropList())
        {
            Import(Clipboard.GetFileDropList().Cast<string>());
        }
        else if (Clipboard.ContainsImage())
        {
            using var image = Clipboard.GetImage();
            if (image is null) return;
            using var bitmap = new Bitmap(image);
            var item = _host.Library.Add(bitmap, Loc.T("Pasted image"));
            _host.OpenInEditor(item.FilePath);
        }
    }

    private static void OnDragEnter(object? sender, DragEventArgs e) =>
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true || e.Data?.GetDataPresent(DataFormats.Bitmap) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) Import(files);
        else if (e.Data?.GetData(DataFormats.Bitmap) is Bitmap bitmap) _host.Library.Add(bitmap, Loc.T("Dropped image"));
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyData)
        {
            case Keys.Delete: DeleteSelected(); e.Handled = true; break;
            case Keys.Enter: OpenSelected(); e.Handled = true; break;
            case Keys.F2: Rename(); e.Handled = true; break;
            case Keys.F5: RefreshItems(); e.Handled = true; break;
            case Keys.Control | Keys.C: CopySelected(); e.Handled = true; break;
            case Keys.Control | Keys.V: PasteFromClipboard(); e.Handled = true; break;
            case Keys.Control | Keys.T: EditTags(); e.Handled = true; break;
            case Keys.Control | Keys.D: ToggleFavorite(); e.Handled = true; break;
            case Keys.Control | Keys.F: _search.Focus(); e.Handled = true; break;
            case Keys.Control | Keys.A:
                foreach (ListViewItem row in _list.Items) row.Selected = true;
                e.Handled = true;
                break;
        }
    }

    // ================================================================== combine / PDF / batch

    private List<LibraryItem> PictureItems() =>
        SelectedItems().Where(i => i.Kind is LibraryItemKind.Image or LibraryItemKind.Project or LibraryItemKind.Animation)
            .OrderBy(i => i.CreatedAt).ToList();

    private void CombineSelected()
    {
        var items = PictureItems();
        if (items.Count < 2) { MessageBox.Show(Window, Loc.T("Select at least two images to combine."), "SnapGit"); return; }

        var dialog = new ParameterDialog(Loc.T("Combine images"))
            .AddLabel(Loc.T("{0} images, in the order they were created.", items.Count))
            .AddCombo("layout", Loc.T("Layout"), [Loc.T("Vertical"), Loc.T("Horizontal"), Loc.T("Grid")], 0)
            .AddNumber("cols", Loc.T("Grid columns"), 2, 1, 10)
            .AddNumber("gap", Loc.T("Spacing (px)"), 12, 0, 200)
            .AddColor("bg", Loc.T("Background"), Color.White);
        if (!dialog.Prompt(Window)) return;

        var bitmaps = new List<Bitmap>();
        try
        {
            foreach (var item in items) bitmaps.Add(LoadFlattened(item.FilePath));
            var combined = ImageComposer.Combine(bitmaps, (CombineLayout)dialog.Index("layout"), dialog.Int("gap"), dialog.ColorValue("bg"), dialog.Int("cols"));
            _host.OpenNewInEditor(combined);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window, ex.Message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach (var bitmap in bitmaps) bitmap.Dispose();
        }
    }

    private void StepDocument()
    {
        var items = PictureItems();
        if (items.Count == 0) { MessageBox.Show(Window, Loc.T("Select the images for the document first."), "SnapGit"); return; }

        var dialog = new ParameterDialog(Loc.T("Step-by-step document"))
            .AddLabel(Loc.T("One caption per line, one line per image (in creation order)."))
            .AddText("title", Loc.T("Title"), string.Empty, 300)
            .AddNumber("width", Loc.T("Page width (px)"), 1000, 400, 3000)
            .AddMultiline("captions", Loc.T("Captions"), string.Join(Environment.NewLine, items.Select((_, i) => Loc.T("Step {0}", i + 1))));
        if (!dialog.Prompt(Window)) return;

        var captions = dialog.String("captions").Split(["\r\n", "\n"], StringSplitOptions.None);
        var bitmaps = new List<Bitmap>();
        try
        {
            var steps = new List<(Bitmap, string)>();
            for (var i = 0; i < items.Count; i++)
            {
                var bitmap = LoadFlattened(items[i].FilePath);
                bitmaps.Add(bitmap);
                steps.Add((bitmap, i < captions.Length ? captions[i] : string.Empty));
            }
            var document = ImageComposer.BuildStepDocument(steps, dialog.String("title"), dialog.Int("width"));
            _host.OpenNewInEditor(document);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window, ex.Message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach (var bitmap in bitmaps) bitmap.Dispose();
        }
    }

    private void ExportPdf()
    {
        var items = PictureItems();
        if (items.Count == 0) { MessageBox.Show(Window, Loc.T("Select the images to export first."), "SnapGit"); return; }

        using var dialog = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = items.Count == 1 ? items[0].Name : Loc.T("SnapGit export"), AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(Window) != DialogResult.OK) return;

        var pages = new List<Bitmap>();
        try
        {
            foreach (var item in items) pages.Add(LoadFlattened(item.FilePath));
            PdfWriter.Write(dialog.FileName, pages, _host.Settings.Current.JpegQuality);
            _status.Text = Loc.T("PDF with {0} page(s) written.", pages.Count);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window, ex.Message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            foreach (var page in pages) page.Dispose();
        }
    }

    private async void BatchConvert()
    {
        var items = PictureItems();
        if (items.Count == 0) { MessageBox.Show(Window, Loc.T("Select the images to convert first."), "SnapGit"); return; }

        var settings = _host.Settings.Current;
        var dialog = new ParameterDialog(Loc.T("Convert / resize"))
            .AddLabel(Loc.T("{0} image(s). Projects are flattened with their annotations.", items.Count))
            .AddCombo("format", Loc.T("Format"), ["PNG", "JPEG", "BMP"], settings.DefaultFormat == ImageFileFormat.Jpeg ? 1 : 0)
            .AddNumber("quality", Loc.T("JPEG quality"), settings.JpegQuality, 10, 100)
            .AddNumber("width", Loc.T("Maximum width (0 = keep)"), 0, 0, 20000)
            .AddNumber("height", Loc.T("Maximum height (0 = keep)"), 0, 0, 20000)
            .AddText("folder", Loc.T("Output folder"), Path.Combine(_host.Library.Folder, "converted"), 300);
        if (!dialog.Prompt(Window)) return;

        var options = new BatchOptions(
            dialog.String("folder"),
            (ImageFileFormat)dialog.Index("format"),
            dialog.Int("quality"),
            dialog.Int("width") > 0 ? dialog.Int("width") : null,
            dialog.Int("height") > 0 ? dialog.Int("height") : null);

        UseWaitCursor = true;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p => _status.Text = Loc.T("Converting… {0}/{1}", p.Done, p.Total));
            var result = await Task.Run(() => BatchConverter.Run(items.Select(i => i.FilePath).ToList(), options, LoadFlattened, progress));

            _status.Text = Loc.T("Converted {0} image(s), {1} failed.", result.Converted.Count, result.Failed.Count);
            if (result.Failed.Count > 0)
                MessageBox.Show(Window, string.Join(Environment.NewLine, result.Failed.Select(f => $"{Path.GetFileName(f.File)}: {f.Error}")), "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (result.Converted.Count > 0)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(options.OutputFolder) { UseShellExecute = true });
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}

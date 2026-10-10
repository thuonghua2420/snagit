using System.Drawing.Printing;
using System.Globalization;
using SnapGit.App.Controls;
using SnapGit.App.Services;
using SnapGit.App.Ui;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Core.Settings;
using SnapGit.Editor;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Controls;
using SnapGit.Editor.Persistence;
using SnapGit.Editor.Tools;
using SnapGit.Platform.Shell;

namespace SnapGit.App.Forms;

/// <summary>The image editor window: menu, tool box, context-sensitive style bar, canvas, status bar.</summary>
public sealed partial class EditorForm : Form
{
    /// <summary>Which style controls make sense for the current tool / selection.</summary>
    [Flags]
    private enum StyleControl
    {
        None = 0, Color = 1, Fill = 2, Width = 4, Opacity = 8, Dash = 16, Corner = 32, Head = 64,
        Font = 128, Shadow = 256, Presets = 512, Stamp = 1024, Gradient = 2048, TextOutline = 4096,
    }

    private static readonly (ToolKind Kind, string Name, Keys Key)[] ToolDefinitions =
    [
        (ToolKind.Select, Loc.N("Select / move"), Keys.V),
        (ToolKind.Arrow, Loc.N("Arrow"), Keys.A),
        (ToolKind.Line, Loc.N("Line"), Keys.L),
        (ToolKind.Rectangle, Loc.N("Rectangle"), Keys.R),
        (ToolKind.Ellipse, Loc.N("Ellipse"), Keys.E),
        (ToolKind.Pen, Loc.N("Pen"), Keys.P),
        (ToolKind.Highlight, Loc.N("Highlight"), Keys.H),
        (ToolKind.Text, Loc.N("Text"), Keys.T),
        (ToolKind.Callout, Loc.N("Callout"), Keys.Q),
        (ToolKind.Step, Loc.N("Step number"), Keys.N),
        (ToolKind.Stamp, Loc.N("Stamp"), Keys.M),
        (ToolKind.Blur, Loc.N("Blur / pixelate"), Keys.B),
        (ToolKind.Spotlight, Loc.N("Spotlight"), Keys.S),
        (ToolKind.Magnify, Loc.N("Magnify"), Keys.G),
        (ToolKind.Eyedropper, Loc.N("Eyedropper"), Keys.I),
        (ToolKind.Fill, Loc.N("Fill (paint bucket)"), Keys.F),
        (ToolKind.Crop, Loc.N("Crop (Enter to apply)"), Keys.C),
        (ToolKind.CutOut, Loc.N("Cut out (Enter to apply)"), Keys.X),
        (ToolKind.SmartMove, Loc.N("Smart move"), Keys.W),
    ];

    private static readonly Dictionary<ToolKind, string> Hints = new()
    {
        [ToolKind.Select] = Loc.N("Click to select (Ctrl/Shift = several), drag to move, drag handles to resize, double-click text to edit, arrows nudge, Delete removes."),
        [ToolKind.Arrow] = Loc.N("Drag to draw an arrow (Shift = 45° angles). After drawing, drag the middle handle to curve it."),
        [ToolKind.Line] = Loc.N("Drag to draw a line. Hold Shift for 45° angles."),
        [ToolKind.Rectangle] = Loc.N("Drag to draw a rectangle. Hold Shift for a square."),
        [ToolKind.Ellipse] = Loc.N("Drag to draw an ellipse. Hold Shift for a circle."),
        [ToolKind.Pen] = Loc.N("Drag to draw freehand."),
        [ToolKind.Highlight] = Loc.N("Drag over text to highlight it."),
        [ToolKind.Text] = Loc.N("Click or drag to add text. Ctrl+Enter or click outside to finish, Esc to cancel."),
        [ToolKind.Callout] = Loc.N("Drag a box, type, then drag the pointer handle to aim the callout."),
        [ToolKind.Step] = Loc.N("Click to place the next numbered step."),
        [ToolKind.Stamp] = Loc.N("Click to place the chosen stamp (pick one in the Stamp menu)."),
        [ToolKind.Blur] = Loc.N("Drag over sensitive content to pixelate it."),
        [ToolKind.Spotlight] = Loc.N("Drag the area to keep bright; everything else is dimmed."),
        [ToolKind.Magnify] = Loc.N("Drag a lens over detail you want to enlarge."),
        [ToolKind.Eyedropper] = Loc.N("Click any pixel to take its colour (Shift or right-click = fill colour)."),
        [ToolKind.Fill] = Loc.N("Click a region to flood-fill it with the fill colour."),
        [ToolKind.Crop] = Loc.N("Drag the area to keep, then press Enter (or double-click). Esc resets."),
        [ToolKind.CutOut] = Loc.N("Drag a band across the image, then press Enter to remove it and join the rest."),
        [ToolKind.SmartMove] = Loc.N("Click an object (auto-detected) or drag around it, then move it with the Select tool."),
    };

    private static readonly int[] WidthChoices = [0, 1, 2, 3, 4, 6, 8, 10, 12, 16, 20];
    private static readonly int[] OpacityChoices = [100, 90, 75, 50, 25];
    private static readonly int[] FontSizeChoices = [12, 14, 16, 18, 20, 24, 28, 32, 40, 48, 64, 96];
    private static readonly int[] CornerChoices = [0, 4, 8, 12, 16, 24, 32];

    /// <summary>What the window is showing. The editor keeps its document while the library or the video editor is in front.</summary>
    internal enum MainPage { Editor, Library, Video }

    private readonly AppHost _host;
    private EditorDocument _document;
    private EditorContext _context;
    private EditorCanvas _canvas;
    private readonly HistoryStrip _history;
    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 30_000 };
    private string _recoveryPath = RecoveryService.NewPath();

    private readonly MenuStrip _menu;
    private readonly ToolStrip _actionBar;
    private readonly ToolStrip _styleBar;
    private readonly ToolPalette _toolBox;
    private readonly StatusStrip _statusBar;
    private readonly HashSet<Keys> _editorShortcuts = [];
    private List<ToolStripItem> _editorMenus = [];

    private MainPage _page = MainPage.Editor;
    private MainPage _lastWorkPage = MainPage.Editor;
    private bool _hasWork;
    private LibraryView? _library;
    private VideoEditorPage? _video;

    private string? _sourcePath;
    private string? _projectPath;
    private bool _syncing;

    /// <summary>
    /// The main window of the app: one window with three pages — the image editor, the library (every capture so far) and the video
    /// editor. <paramref name="document"/> is what the editor starts with (a blank canvas when nothing was opened yet).
    /// </summary>
    public EditorForm(AppHost host, EditorDocument document, string? filePath, bool isProject)
    {
        _host = host;
        _document = document;
        _sourcePath = filePath;
        _projectPath = isProject ? filePath : null;
        _hasWork = filePath is not null || document.IsModified;
        _context = new EditorContext(document);
        _canvas = new EditorCanvas(_context) { Dock = DockStyle.Fill, BackColor = Theme.Colors.Canvas };

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1240, 800);
        MinimumSize = new Size(860, 540);
        AllowDrop = true;

        _menu = BuildMenu();
        _actionBar = BuildActionBar();
        _styleBar = BuildStyleBar();
        _statusBar = BuildStatusBar();
        _toolBox = BuildToolBox();
        _history = new HistoryStrip(host, () => _projectPath ?? _sourcePath);

        // Dock order: Fill first, bars after (later added = docked earlier = closer to the edge).
        // The history strip goes in before the tool box so it spans only the width right of it.
        Controls.Add(_canvas);
        Controls.Add(_history);
        Controls.Add(_toolBox);
        Controls.Add(_statusBar);
        Controls.Add(_styleBar);
        Controls.Add(_actionBar);
        Controls.Add(_menu);
        MainMenuStrip = _menu;

        WireDocument();
        Activated += (_, _) => _history.Invalidate();

        DragEnter += (_, e) => e.Effect = _page == MainPage.Editor && (e.Data?.GetDataPresent(DataFormats.FileDrop) == true || e.Data?.GetDataPresent(DataFormats.Bitmap) == true) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += OnDragDrop;

        _autosave.Tick += (_, _) => Autosave();
        _autosave.Start();

        OnToolChanged();
        OnDocumentChanged();
        UpdateStatus();
        Theme.Apply(this);
        CollectEditorShortcuts(_editorMenus);
    }

    /// <summary>Hooks the events of the current context / document / canvas (again after the document is swapped).</summary>
    private void WireDocument()
    {
        _context.ToolChanged += (_, _) => OnToolChanged();
        _context.SelectionChanged += (_, _) => SyncToolbar();
        _context.StyleChanged += (_, _) => SyncToolbar();
        _document.Changed += (_, _) => OnDocumentChanged();
        _canvas.ZoomChanged += (_, _) => UpdateStatus();
    }

    // Test seams (visible to SnapGit.Tests only).
    internal EditorContext Context => _context;
    internal EditorCanvas Canvas => _canvas;
    internal HistoryStrip History => _history;
    internal MainPage Page => _page;
    internal LibraryView? Library => _library;
    internal VideoEditorPage? Video => _video;
    internal EditorDocument Document => _document;
    internal string? CurrentPath => _projectPath ?? _sourcePath;

    private string DisplayName => _projectPath is not null ? Path.GetFileNameWithoutExtension(_projectPath)
        : _sourcePath is not null ? Path.GetFileNameWithoutExtension(_sourcePath)
        : Loc.T("Untitled");

    // ================================================================== state sync

    private void OnToolChanged()
    {
        _toolBox.Selected = _context.ActiveTool.ToString();

        _hintLabel.Text = Loc.T(Hints.GetValueOrDefault(_context.ActiveTool, string.Empty));
        SyncToolbar();
    }

    private void OnDocumentChanged()
    {
        _undoButton.Enabled = _undoItem.Enabled = _document.History.CanUndo;
        _redoButton.Enabled = _redoItem.Enabled = _document.History.CanRedo;

        // Undo/redo may have removed selected annotations.
        var stale = _context.Selected.Where(s => !_document.Annotations.Contains(s)).ToList();
        if (stale.Count > 0) _context.SetSelection(_context.Selected.Except(stale));

        UpdateTitle();
        UpdateStatus();
    }

    private StyleControl CurrentRelevance()
    {
        var annotation = _context.Selection;
        var kind = annotation is null ? _context.ActiveTool : ToolKindOf(annotation);

        return kind switch
        {
            ToolKind.Arrow => StyleControl.Color | StyleControl.Width | StyleControl.Opacity | StyleControl.Dash | StyleControl.Head | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Line => StyleControl.Color | StyleControl.Width | StyleControl.Opacity | StyleControl.Dash | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Rectangle => StyleControl.Color | StyleControl.Fill | StyleControl.Gradient | StyleControl.Width | StyleControl.Opacity | StyleControl.Dash | StyleControl.Corner | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Ellipse => StyleControl.Color | StyleControl.Fill | StyleControl.Gradient | StyleControl.Width | StyleControl.Opacity | StyleControl.Dash | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Pen => StyleControl.Color | StyleControl.Width | StyleControl.Opacity | StyleControl.Dash | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Highlight => StyleControl.Fill | StyleControl.Opacity | StyleControl.Presets,
            ToolKind.Text => StyleControl.Color | StyleControl.Fill | StyleControl.Gradient | StyleControl.TextOutline | StyleControl.Opacity | StyleControl.Corner | StyleControl.Font | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Callout => StyleControl.Color | StyleControl.Fill | StyleControl.Gradient | StyleControl.TextOutline | StyleControl.Width | StyleControl.Opacity | StyleControl.Corner | StyleControl.Font | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Step => StyleControl.Color | StyleControl.Fill | StyleControl.Opacity | StyleControl.Shadow | StyleControl.Presets,
            ToolKind.Stamp => StyleControl.Color | StyleControl.Fill | StyleControl.Opacity | StyleControl.Shadow | StyleControl.Stamp,
            ToolKind.Spotlight => StyleControl.Opacity | StyleControl.Corner,
            ToolKind.Magnify => StyleControl.Color | StyleControl.Width | StyleControl.Shadow,
            ToolKind.Fill => StyleControl.Fill | StyleControl.Opacity,
            _ => StyleControl.None,
        };
    }

    private static ToolKind ToolKindOf(Annotation annotation) => annotation switch
    {
        ArrowAnnotation => ToolKind.Arrow,
        LineAnnotation => ToolKind.Line,
        RectangleAnnotation => ToolKind.Rectangle,
        EllipseAnnotation => ToolKind.Ellipse,
        PenAnnotation => ToolKind.Pen,
        HighlightAnnotation => ToolKind.Highlight,
        CalloutAnnotation => ToolKind.Callout,
        TextAnnotation => ToolKind.Text,
        StepAnnotation => ToolKind.Step,
        StampAnnotation => ToolKind.Stamp,
        BlurAnnotation => ToolKind.Blur,
        SpotlightAnnotation => ToolKind.Spotlight,
        MagnifyAnnotation => ToolKind.Magnify,
        _ => ToolKind.Select,
    };

    private void SyncToolbar()
    {
        _syncing = true;
        try
        {
            var style = _context.CurrentStyle;
            _strokeButton.SetColorSilently(style.Stroke);
            _fillButton.SetColorSilently(style.Fill);
            SelectNearest(_widthBox, WidthChoices, (int)Math.Round(style.StrokeWidth), v => v.ToString(CultureInfo.InvariantCulture));
            SelectNearest(_opacityBox, OpacityChoices, (int)Math.Round(style.Opacity * 100), v => v + "%");
            SelectNearest(_fontSizeBox, FontSizeChoices, (int)Math.Round(style.FontSize), v => v.ToString(CultureInfo.InvariantCulture));
            SelectNearest(_cornerBox, CornerChoices, (int)Math.Round(style.CornerRadius), v => v.ToString(CultureInfo.InvariantCulture));
            _dashBox.SelectedIndex = (int)style.Dash;
            _headBox.SelectedIndex = (int)style.Head;
            _alignBox.SelectedIndex = (int)style.Align;
            _doubleHeadButton.Checked = style.DoubleHeaded;
            _fontBox.Text = style.FontFamily;
            _gradientButton.SetColorSilently(style.GradientTo);
            SelectNearest(_gradientAngleBox, GradientAngles, (int)Math.Round(style.GradientAngle) % 360, v => v + "°");
            _outlineButton.SetColorSilently(style.OutlineColor);
            SelectNearest(_outlineWidthBox, OutlineWidths, (int)Math.Round(style.OutlineWidth), v => v.ToString(CultureInfo.InvariantCulture));
            _boldButton.Checked = style.Bold;
            _italicButton.Checked = style.Italic;
            _shadowButton.Checked = style.Shadow;

            var relevance = CurrentRelevance();
            bool Has(StyleControl c) => relevance.HasFlag(c);
            _strokeButton.Visible = Has(StyleControl.Color);
            _fillButton.Visible = Has(StyleControl.Fill);
            _gradientButton.Visible = _gradientLabel.Visible = _gradientAngleBox.Visible = Has(StyleControl.Gradient);
            _outlineButton.Visible = _outlineWidthBox.Visible = Has(StyleControl.TextOutline);
            _widthLabel.Visible = _widthBox.Visible = Has(StyleControl.Width);
            _opacityLabel.Visible = _opacityBox.Visible = Has(StyleControl.Opacity);
            _dashLabel.Visible = _dashBox.Visible = Has(StyleControl.Dash);
            _cornerLabel.Visible = _cornerBox.Visible = Has(StyleControl.Corner);
            _headLabel.Visible = _headBox.Visible = _doubleHeadButton.Visible = Has(StyleControl.Head);
            _fontBox.Visible = _fontSizeBox.Visible = _boldButton.Visible = _italicButton.Visible = _alignBox.Visible = _fontSeparator.Visible = Has(StyleControl.Font);
            _shadowButton.Visible = Has(StyleControl.Shadow);
            _presetsButton.Visible = Has(StyleControl.Presets);
            _stampButton.Visible = Has(StyleControl.Stamp);
            _arrangeSeparator.Visible = relevance != StyleControl.None;

            var count = _context.Selected.Count;
            _selectionLabel.Text = count == 0 ? string.Empty : Loc.T("{0} selected", count);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void SelectNearest(ToolStripComboBox combo, int[] values, int wanted, Func<int, string> format)
    {
        var nearest = values.OrderBy(v => Math.Abs(v - wanted)).First();
        combo.SelectedItem = format(nearest);
    }

    private void UpdateStatus()
    {
        _sizeLabel.Text = $"{_document.Image.Width} × {_document.Image.Height} px";
        _zoomLabel.Text = $"{_canvas.Zoom * 100:0}%";
    }

    private void UpdateTitle() =>
        Text = _page switch
        {
            MainPage.Library => $"SnapGit — {Loc.T("Library")}",
            MainPage.Video when _video is not null => $"{_video.DisplayName} — SnapGit {Loc.T("Video editor")}",
            _ => $"{DisplayName}{(_document.IsModified ? " *" : string.Empty)} — SnapGit",
        };

    private void SetStatus(string message) => _hintLabel.Text = message;

    // ================================================================== saving & export

    /// <summary>Ctrl+S: writes the editable project (annotations stay editable). Raster sources are never overwritten.</summary>
    private bool Save()
    {
        try
        {
            _projectPath ??= SuggestProjectPath();
            ProjectStorage.Save(_document, _projectPath);
            _host.Library.NotifyModified(_projectPath);
            DiscardRecovery();
            UpdateTitle();
            SetStatus(Loc.T("Saved {0}.", Path.GetFileName(_projectPath)));
            return true;
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("Could not save the project:\n\n{0}", ex.Message));
            return false;
        }
    }

    private string SuggestProjectPath()
    {
        if (_sourcePath is not null && !SnapProjectFile.IsProject(_sourcePath))
        {
            var beside = Path.ChangeExtension(_sourcePath, SnapProjectFile.Extension);
            for (var i = 2; File.Exists(beside); i++)
                beside = Path.Combine(Path.GetDirectoryName(_sourcePath)!, $"{Path.GetFileNameWithoutExtension(_sourcePath)} ({i}){SnapProjectFile.Extension}");
            return beside;
        }
        return _host.Library.ReservePath(SnapProjectFile.Extension, Loc.T("Image"));
    }

    private bool SaveAs()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = Loc.T("SnapGit project") + "|*.snapgit|PNG|*.png|JPEG|*.jpg|" + Loc.T("Bitmap") + "|*.bmp|GIF|*.gif|PDF|*.pdf",
            FileName = DisplayName,
            InitialDirectory = _host.Library.Folder,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (!Directory.Exists(dialog.InitialDirectory)) dialog.InitialDirectory = null;
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;

        if (SnapProjectFile.IsProject(dialog.FileName))
        {
            _projectPath = dialog.FileName;
            return Save();
        }
        return ExportTo(dialog.FileName);
    }

    /// <summary>Writes the flattened picture (any image format, or PDF) without touching the project.</summary>
    private bool ExportTo(string path)
    {
        try
        {
            using var rendered = _document.Render();
            if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                PdfWriter.Write(path, [rendered], _host.Settings.Current.JpegQuality);
            else
                ImageEncoder.Save(rendered, path, ImageEncoder.FromExtension(path), _host.Settings.Current.JpegQuality);

            SetStatus(Loc.T("Exported {0}.", Path.GetFileName(path)));
            return true;
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("Could not export the image:\n\n{0}", ex.Message));
            return false;
        }
    }

    private void CopyToClipboard()
    {
        using var rendered = _document.Render();
        ClipboardService.SetImage(rendered);
        SetStatus(Loc.T("Copied to clipboard."));
    }

    private const string ObjectsFormat = "SnapGit.Annotations.v1";
    private int _pasteCount;

    /// <summary>Ctrl+C: copies the selected objects, or the whole picture when nothing is selected.</summary>
    private void CopyOrImage()
    {
        if (_context.Selected.Count > 0) CopyObjects(cut: false);
        else CopyToClipboard();
    }

    /// <summary>
    /// Puts the selection on the clipboard twice over: as SnapGit objects (so pasting into any editor window keeps
    /// them editable) and as a picture of that area (so other apps can paste it).
    /// </summary>
    private void CopyObjects(bool cut)
    {
        var json = _context.SerializeSelection();
        if (json is null) return;

        var data = new DataObject();
        data.SetData(ObjectsFormat, json);

        var area = Rectangle.Intersect(Rectangle.Ceiling(_context.Selected.Select(a => a.Bounds).Aggregate(RectangleF.Union)),
            new Rectangle(Point.Empty, _document.Image.Size));
        if (area.Width > 0 && area.Height > 0)
        {
            using var flat = _document.Render();
            using var crop = flat.Clone(area, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            data.SetImage(new Bitmap(crop));
        }

        Clipboard.SetDataObject(data, copy: true);
        _pasteCount = 0;
        SetStatus(cut ? Loc.T("Cut {0} object(s).", _context.Selected.Count) : Loc.T("Copied {0} object(s).", _context.Selected.Count));
        if (cut) _context.DeleteSelection();
    }

    /// <summary>Ctrl+V: SnapGit objects first (stay editable), otherwise a picture becomes a layer.</summary>
    private void Paste()
    {
        var data = Clipboard.GetDataObject();
        if (data?.GetDataPresent(ObjectsFormat) == true && data.GetData(ObjectsFormat) is string json)
        {
            _context.SetTool(ToolKind.Select);
            _pasteCount++;
            _context.PasteSerialized(json, offset: 16f * _pasteCount);
            return;
        }
        PasteImage();
    }

    private void PasteImage()
    {
        if (!Clipboard.ContainsImage()) { SetStatus(Loc.T("The clipboard has no image.")); return; }
        using var image = Clipboard.GetImage();
        if (image is null) return;

        using var bitmap = new Bitmap(image);
        _context.SetTool(ToolKind.Select);
        _context.AddImageLayer(bitmap);
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (_page != MainPage.Editor) return; // the library page imports dropped files itself
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(f => LibraryItem.KindOf(f) == LibraryItemKind.Image || Path.GetExtension(f).Equals(".gif", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var image = ImageEncoder.LoadUnlocked(file);
                    _context.SetTool(ToolKind.Select);
                    _context.AddImageLayer(image, _canvas.PointToClient(new Point(e.X, e.Y)) is var p ? _canvas.ClientToImage(p) : null);
                }
                catch (Exception ex)
                {
                    SetStatus(Loc.T("Could not read {0}: {1}", Path.GetFileName(file), ex.Message));
                }
            }
        }
        else if (e.Data?.GetData(DataFormats.Bitmap) is Bitmap bitmap)
        {
            _context.SetTool(ToolKind.Select);
            _context.AddImageLayer(bitmap);
        }
    }

    private void PrintImage()
    {
        using var rendered = _document.Render();
        using var document = new PrintDocument { DocumentName = DisplayName };
        document.PrintPage += (_, e) =>
        {
            var area = e.MarginBounds;
            var scale = Math.Min((float)area.Width / rendered.Width, (float)area.Height / rendered.Height);
            var width = rendered.Width * Math.Min(scale, 1f);
            var height = rendered.Height * Math.Min(scale, 1f);
            e.Graphics!.DrawImage(rendered, area.Left, area.Top, width, height);
        };

        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) document.Print();
    }

    private void ShareTo(ShareDestination destination)
    {
        try
        {
            // Share what you see now: export the flattened picture to a temp file when there are unsaved edits.
            var path = Path.Combine(Path.GetTempPath(), NamePattern.Sanitize(DisplayName) + ".png");
            using (var rendered = _document.Render()) ImageEncoder.Save(rendered, path, ImageFileFormat.Png);
            SetStatus(_host.Sharing.Share(this, path, destination));
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Autosave()
    {
        if (!_document.IsModified || !IsHandleCreated) return;
        try
        {
            Directory.CreateDirectory(RecoveryService.Folder);
            ProjectStorage.Save(_document, _recoveryPath, markSaved: false);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Warn($"Autosave failed: {ex.Message}");
        }
    }

    private void DiscardRecovery() => RecoveryService.Discard(_recoveryPath);

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "SnapGit", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ================================================================== keyboard

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Another page is in front: the editor's menu shortcuts (Ctrl+C, Ctrl+S…) and tool letters must not fire behind it —
        // the page itself (list, timeline…) gets the key instead.
        if (_page != MainPage.Editor)
            return _editorShortcuts.Contains(keyData) ? false : base.ProcessCmdKey(ref msg, keyData);

        // While typing into the text editor, every key (incl. Ctrl+C/V/Z) belongs to it, not to menu shortcuts.
        if (_canvas.IsEditingText) return false;

        switch (keyData)
        {
            case Keys.Control | Keys.Shift | Keys.Z: _document.History.Redo(); return true;
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Add: _canvas.ZoomIn(); return true;
            case Keys.Control | Keys.OemMinus:
            case Keys.Control | Keys.Subtract: _canvas.ZoomOut(); return true;
        }

        // Single-letter tool shortcuts, only while a combo box isn't taking keystrokes.
        if (!AnyComboFocused())
        {
            foreach (var (kind, _, key) in ToolDefinitions)
            {
                if (keyData != key) continue;
                _context.SetTool(kind);
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool AnyComboFocused() =>
        _widthBox.Focused || _opacityBox.Focused || _fontSizeBox.Focused || _fontBox.Focused || _cornerBox.Focused || _dashBox.Focused || _headBox.Focused || _alignBox.Focused;

    // ================================================================== lifetime

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;

        if (e.CloseReason == CloseReason.UserClosing)
        {
            // X / Ctrl+W: ask about unsaved work, then just hide (the app stays in the tray).
            e.Cancel = true;
            HideInsteadOfClose();
            return;
        }

        // The app is exiting (tray "Exit", Windows shutting down…): no questions, but keep what is unsaved recoverable.
        Autosave();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _autosave.Stop();
        _autosave.Dispose();
        if (!_document.IsModified) DiscardRecovery(); // unsaved work stays on disk: it is offered back at the next start
        _document.Dispose();
    }
}

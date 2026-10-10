using System.Globalization;
using SnapGit.App.Controls;
using SnapGit.App.Ui;
using SnapGit.Core.Imaging;
using SnapGit.Core.Localization;
using SnapGit.Core.Settings;
using SnapGit.Editor;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Tools;

namespace SnapGit.App.Forms;

/// <summary>Construction of menus, tool boxes and bars. Behaviour lives in EditorForm.cs / EditorForm.ImageOps.cs.</summary>
public sealed partial class EditorForm
{
    private readonly ColorPickerButton _strokeButton = new(Loc.T("Color"), allowTransparent: false);
    private readonly ColorPickerButton _fillButton = new(Loc.T("Fill"), allowTransparent: true);

    private readonly ToolStripLabel _widthLabel = new(Loc.T("Width"));
    private readonly ToolStripComboBox _widthBox = new();
    private readonly ToolStripLabel _opacityLabel = new(Loc.T("Opacity"));
    private readonly ToolStripComboBox _opacityBox = new();
    private readonly ToolStripLabel _dashLabel = new(Loc.T("Line"));
    private readonly ToolStripComboBox _dashBox = new();
    private readonly ToolStripLabel _cornerLabel = new(Loc.T("Corners"));
    private readonly ToolStripComboBox _cornerBox = new();
    private readonly ToolStripLabel _headLabel = new(Loc.T("Head"));
    private readonly ToolStripComboBox _headBox = new();
    private readonly ToolStripButton _doubleHeadButton = new() { CheckOnClick = true, Text = "⇄", ToolTipText = "Heads at both ends" };

    private readonly ColorPickerButton _gradientButton = new(Loc.T("Fill 2"), allowTransparent: true);
    private readonly ToolStripLabel _gradientLabel = new(Loc.T("Angle"));
    private readonly ToolStripComboBox _gradientAngleBox = new();
    private readonly ColorPickerButton _outlineButton = new(Loc.T("Outline"), allowTransparent: true);
    private readonly ToolStripComboBox _outlineWidthBox = new();

    private static readonly int[] GradientAngles = [0, 45, 90, 135, 180, 225, 270, 315];
    private static readonly int[] OutlineWidths = [0, 1, 2, 3, 4, 6];

    private readonly ToolStripComboBox _fontBox = new();
    private readonly ToolStripComboBox _fontSizeBox = new();
    private readonly ToolStripButton _boldButton = new() { CheckOnClick = true };
    private readonly ToolStripButton _italicButton = new() { CheckOnClick = true };
    private readonly ToolStripComboBox _alignBox = new();
    private readonly ToolStripSeparator _fontSeparator = new();

    private readonly ToolStripButton _shadowButton = new() { CheckOnClick = true };
    private readonly ToolStripDropDownButton _presetsButton = new();
    private readonly ToolStripDropDownButton _stampButton = new();
    private readonly ToolStripSeparator _arrangeSeparator = new();

    private ToolStripButton _undoButton = new();
    private ToolStripButton _redoButton = new();
    private ToolStripMenuItem _undoItem = new();
    private ToolStripMenuItem _redoItem = new();

    private readonly ToolStripStatusLabel _hintLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _selectionLabel = new();
    private readonly ToolStripStatusLabel _sizeLabel = new();
    private readonly ToolStripStatusLabel _zoomLabel = new();

    // ------------------------------------------------------------------ menu

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { ImageScalingSize = new Size(16, 16) };

        var share = new ToolStripMenuItem(Loc.T("Share"), Icons.Get(UiIcon.Share));
        share.DropDownOpening += (_, _) => FillShareMenu(share);
        share.DropDownItems.Add("…");

        menu.Items.Add(Menu(Loc.T("&File"),
            Item(Loc.T("Library (all captures)"), UiIcon.Library, Keys.None, () => _host.ShowLibrary()),
            new ToolStripSeparator(),
            Item(Loc.T("Save project"), UiIcon.Save, Keys.Control | Keys.S, () => Save()),
            Item(Loc.T("Save as…"), UiIcon.SaveAs, Keys.Control | Keys.Shift | Keys.S, () => SaveAs()),
            Item(Loc.T("Export image / PDF…"), UiIcon.Image, Keys.Control | Keys.E, () => ExportDialog()),
            Item(Loc.T("Print…"), UiIcon.Print, Keys.Control | Keys.P, PrintImage),
            new ToolStripSeparator(),
            Item(Loc.T("Copy image"), UiIcon.Image, Keys.Control | Keys.Shift | Keys.C, CopyToClipboard),
            share,
            new ToolStripSeparator(),
            Item(Loc.T("Close"), null, Keys.Control | Keys.W, Close)));

        _undoItem = Item(Loc.T("Undo"), UiIcon.Undo, Keys.Control | Keys.Z, () => _document.History.Undo());
        _redoItem = Item(Loc.T("Redo"), UiIcon.Redo, Keys.Control | Keys.Y, () => _document.History.Redo());
        menu.Items.Add(Menu(Loc.T("&Edit"),
            _undoItem, _redoItem,
            new ToolStripSeparator(),
            Item(Loc.T("Select all"), null, Keys.Control | Keys.A, () => { _context.SetTool(ToolKind.Select); _context.SelectAll(); }),
            Item(Loc.T("Duplicate"), UiIcon.Duplicate, Keys.Control | Keys.D, () => _context.DuplicateSelection()),
            Item(Loc.T("Delete"), UiIcon.Delete, Keys.None, () => _context.DeleteSelection(), "Del"),
            Item(Loc.T("Cut"), UiIcon.Cut, Keys.Control | Keys.X, () => CopyObjects(cut: true)),
            Item(Loc.T("Copy"), UiIcon.Copy, Keys.Control | Keys.C, CopyOrImage),
            Item(Loc.T("Paste"), UiIcon.Paste, Keys.Control | Keys.V, Paste),
            new ToolStripSeparator(),
            Item(Loc.T("Bring to front"), UiIcon.BringFront, Keys.Control | Keys.Shift | Keys.OemCloseBrackets, () => _context.Arrange(int.MaxValue)),
            Item(Loc.T("Bring forward"), null, Keys.Control | Keys.OemCloseBrackets, () => _context.Arrange(1)),
            Item(Loc.T("Send backward"), null, Keys.Control | Keys.OemOpenBrackets, () => _context.Arrange(-1)),
            Item(Loc.T("Send to back"), UiIcon.SendBack, Keys.Control | Keys.Shift | Keys.OemOpenBrackets, () => _context.Arrange(int.MinValue)),
            new ToolStripSeparator(),
            Item(Loc.T("Group"), UiIcon.Group, Keys.Control | Keys.G, () => _context.GroupSelection()),
            Item(Loc.T("Ungroup"), UiIcon.Ungroup, Keys.Control | Keys.Shift | Keys.G, () => _context.UngroupSelection()),
            AlignMenu()));

        menu.Items.Add(Menu(Loc.T("&Image"),
            Item(Loc.T("Rotate left"), UiIcon.RotateLeft, Keys.None, () => RotateFlip(RotateFlipType.Rotate270FlipNone, Loc.T("Rotate left"))),
            Item(Loc.T("Rotate right"), UiIcon.RotateRight, Keys.None, () => RotateFlip(RotateFlipType.Rotate90FlipNone, Loc.T("Rotate right"))),
            Item(Loc.T("Rotate 180°"), null, Keys.None, () => RotateFlip(RotateFlipType.Rotate180FlipNone, Loc.T("Rotate 180°"))),
            Item(Loc.T("Flip horizontally"), UiIcon.FlipHorizontal, Keys.None, () => RotateFlip(RotateFlipType.RotateNoneFlipX, Loc.T("Flip horizontally"))),
            Item(Loc.T("Flip vertically"), UiIcon.FlipVertical, Keys.None, () => RotateFlip(RotateFlipType.RotateNoneFlipY, Loc.T("Flip vertically"))),
            new ToolStripSeparator(),
            Item(Loc.T("Canvas size…"), null, Keys.None, CanvasSizeDialog),
            Item(Loc.T("Resize image…"), null, Keys.None, ResizeDialog),
            Item(Loc.T("Trim empty borders"), UiIcon.Crop, Keys.None, AutoCrop),
            new ToolStripSeparator(),
            Item(Loc.T("Border…"), null, Keys.None, BorderDialog),
            Item(Loc.T("Drop shadow…"), UiIcon.Shadow, Keys.None, ShadowDialog),
            Item(Loc.T("Torn edges…"), null, Keys.None, TornEdgeDialog),
            new ToolStripSeparator(),
            Item(Loc.T("Grayscale"), null, Keys.None, () => Effect(Loc.T("Grayscale"), ImageOps.Grayscale)),
            Item(Loc.T("Invert colors"), null, Keys.None, () => Effect(Loc.T("Invert colors"), ImageOps.Invert)),
            Item(Loc.T("Sepia"), null, Keys.None, () => Effect(Loc.T("Sepia"), ImageOps.Sepia)),
            Item(Loc.T("Brightness / contrast…"), null, Keys.None, BrightnessDialog),
            Item(Loc.T("Replace color…"), null, Keys.None, ReplaceColorDialog),
            Item(Loc.T("Simplify (flat UI look)"), null, Keys.None, () => Effect(Loc.T("Simplify"), b => ImageOps.Simplify(b))),
            new ToolStripSeparator(),
            Item(Loc.T("Grab text from image (OCR)…"), UiIcon.GrabText, Keys.None, () => _ = GrabTextAsync())));

        menu.Items.Add(Menu(Loc.T("&View"),
            Item(Loc.T("Zoom in"), UiIcon.ZoomIn, Keys.None, () => _canvas.ZoomIn(), "Ctrl++"),
            Item(Loc.T("Zoom out"), UiIcon.ZoomOut, Keys.None, () => _canvas.ZoomOut(), "Ctrl+-"),
            Item(Loc.T("Fit to window"), UiIcon.Fit, Keys.Control | Keys.D0, () => _canvas.ZoomToFit()),
            Item(Loc.T("Actual size"), null, Keys.Control | Keys.D1, () => _canvas.ZoomActual())));

        // File / Edit / Image / View belong to the picture editor and hide while the library or the video editor is in front.
        _editorMenus = menu.Items.Cast<ToolStripItem>().ToList();

        var capture = new ToolStripMenuItem(Loc.T("&Capture"));
        CaptureMenu.Fill(capture.DropDownItems, _host);
        menu.Items.Add(capture);

        menu.Items.Add(Menu(Loc.T("&Settings"),
            Item(Loc.T("Capture hotkeys…"), UiIcon.Capture, Keys.None, () => _host.ShowSettings(this, SettingsTab.Hotkeys)),
            Item(Loc.T("Recording options…"), UiIcon.Record, Keys.None, () => _host.ShowSettings(this, SettingsTab.Recording)),
            Item(Loc.T("Capture profiles…"), UiIcon.Profile, Keys.None, () => _host.ShowSettings(this, SettingsTab.Profiles)),
            new ToolStripSeparator(),
            Item(Loc.T("All settings…"), UiIcon.Settings, Keys.None, () => _host.ShowSettings(this))));

        menu.Items.Add(Menu(Loc.T("&Help"),
            Item(Loc.T("Keyboard shortcuts"), null, Keys.F1, ShowShortcuts),
            Item(Loc.T("About SnapGit"), UiIcon.Info, Keys.None, () => _host.ShowAbout(this))));

        return menu;
    }

    private ToolStripMenuItem AlignMenu()
    {
        var menu = new ToolStripMenuItem(Loc.T("Align and distribute"), Icons.Get(UiIcon.AlignLeft));
        foreach (var (text, icon, mode) in AlignChoices)
            menu.DropDownItems.Add(Item(Loc.T(text), icon, Keys.None, () => _context.Align(mode)));
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(Item(Loc.T("Distribute horizontally"), UiIcon.DistributeH, Keys.None, () => _context.Distribute(horizontal: true)));
        menu.DropDownItems.Add(Item(Loc.T("Distribute vertically"), UiIcon.DistributeV, Keys.None, () => _context.Distribute(horizontal: false)));
        return menu;
    }

    private static readonly (string Text, UiIcon Icon, AlignMode Mode)[] AlignChoices =
    [
        (Loc.N("Align left"), UiIcon.AlignLeft, AlignMode.Left),
        (Loc.N("Align centre"), UiIcon.AlignCenter, AlignMode.Center),
        (Loc.N("Align right"), UiIcon.AlignRight, AlignMode.Right),
        (Loc.N("Align top"), UiIcon.AlignTop, AlignMode.Top),
        (Loc.N("Align middle"), UiIcon.AlignMiddle, AlignMode.Middle),
        (Loc.N("Align bottom"), UiIcon.AlignBottom, AlignMode.Bottom),
    ];

    private static ToolStripMenuItem Menu(string text, params ToolStripItem[] items)
    {
        var menu = new ToolStripMenuItem(text);
        menu.DropDownItems.AddRange(items);
        return menu;
    }

    private static ToolStripMenuItem Item(string text, UiIcon? icon, Keys shortcut, Action onClick, string? shortcutText = null)
    {
        var item = new ToolStripMenuItem(text, icon is { } i ? Icons.Get(i) : null);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        if (shortcutText is not null) item.ShortcutKeyDisplayString = shortcutText;
        item.Click += (_, _) => onClick();
        return item;
    }

    private void FillShareMenu(ToolStripMenuItem root)
    {
        root.DropDownItems.Clear();
        var destinations = ShareService_BuiltIn().Concat(_host.Settings.Current.ShareDestinations);
        foreach (var destination in destinations)
        {
            var captured = destination;
            var item = new ToolStripMenuItem(Loc.T(destination.Name));
            item.Click += (_, _) => ShareTo(captured);
            root.DropDownItems.Add(item);
        }
    }

    private static IEnumerable<ShareDestination> ShareService_BuiltIn() => Services.ShareService.BuiltIn;

    private void ShowShortcuts()
    {
        var lines = ToolDefinitions.Select(t => $"{t.Key,-3} {Loc.T(t.Name)}");
        var text = string.Join(Environment.NewLine, lines) + Environment.NewLine + Environment.NewLine +
                   Loc.T("Ctrl+S save • Ctrl+E export • Ctrl+Z/Y undo/redo • Ctrl+D duplicate • Ctrl+A select all") + Environment.NewLine +
                   Loc.T("Space/middle-drag = pan • Ctrl+wheel = zoom • arrows = nudge (Shift = 10 px)");
        MessageBox.Show(this, text, Loc.T("Keyboard shortcuts"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ tool box (left)

    private ToolPalette BuildToolBox()
    {
        // Tools grouped by purpose; the palette draws dividers between the groups.
        var palette = new ToolPalette();
        void Group(params ToolKind[] kinds) => palette.AddGroup(kinds.Select(kind =>
        {
            var definition = ToolDefinitions.First(d => d.Kind == kind);
            return new PaletteTool(kind.ToString(), Enum.Parse<UiIcon>(kind.ToString()), Loc.T(definition.Name), definition.Key.ToString());
        }));

        Group(ToolKind.Select, ToolKind.SmartMove);
        Group(ToolKind.Arrow, ToolKind.Line, ToolKind.Rectangle, ToolKind.Ellipse, ToolKind.Pen, ToolKind.Highlight);
        Group(ToolKind.Text, ToolKind.Callout, ToolKind.Step, ToolKind.Stamp);
        Group(ToolKind.Blur, ToolKind.Spotlight, ToolKind.Magnify);
        Group(ToolKind.Eyedropper, ToolKind.Fill);
        Group(ToolKind.Crop, ToolKind.CutOut);

        palette.ToolChosen += id => _context.SetTool(Enum.Parse<ToolKind>(id));
        return palette;
    }

    // ------------------------------------------------------------------ command bar (top)

    private ToolStrip BuildActionBar()
    {
        // One row of icon buttons (names in the tooltips); only the primary action and the page switch carry a word.
        // Groups: capture | file | history | object and picture | text | settings.
        var strip = Toolbars.NewBar(DockStyle.Top);

        var capture = Toolbars.PrimaryMenu(UiIcon.Capture, Loc.T("Capture"), Loc.T("Take a capture"));
        CaptureMenu.Fill(capture.DropDownItems, _host);
        strip.Items.Add(capture);
        strip.Items.Add(Toolbars.Button(UiIcon.Library, Loc.T("Show every capture so far"), () => _host.ShowLibrary(), Loc.T("Library")));
        strip.Items.Add(Toolbars.Separator());

        strip.Items.Add(Toolbars.Button(UiIcon.Save, Loc.T("Save project (Ctrl+S)"), () => Save()));
        strip.Items.Add(Toolbars.Button(UiIcon.Export, Loc.T("Export image / PDF (Ctrl+E)"), ExportDialog));
        strip.Items.Add(Toolbars.Button(UiIcon.Copy, Loc.T("Copy image to clipboard (Ctrl+C)"), CopyToClipboard));
        strip.Items.Add(Toolbars.Separator());

        _undoButton = Toolbars.Button(UiIcon.Undo, Loc.T("Undo (Ctrl+Z)"), () => _document.History.Undo());
        _redoButton = Toolbars.Button(UiIcon.Redo, Loc.T("Redo (Ctrl+Y)"), () => _document.History.Redo());
        strip.Items.Add(_undoButton);
        strip.Items.Add(_redoButton);
        strip.Items.Add(Toolbars.Separator());

        strip.Items.Add(Toolbars.Button(UiIcon.Delete, Loc.T("Delete selected (Del)"), () => _context.DeleteSelection()));
        strip.Items.Add(Toolbars.Button(UiIcon.Duplicate, Loc.T("Duplicate (Ctrl+D)"), () => _context.DuplicateSelection()));

        var arrange = Toolbars.Menu(UiIcon.Arrange, Loc.T("Arrange: order, group, align"));
        arrange.DropDownItems.AddRange([
            Item(Loc.T("Bring to front"), UiIcon.BringFront, Keys.None, () => _context.Arrange(int.MaxValue)),
            Item(Loc.T("Send to back"), UiIcon.SendBack, Keys.None, () => _context.Arrange(int.MinValue)),
            new ToolStripSeparator(),
            Item(Loc.T("Group (Ctrl+G)"), UiIcon.Group, Keys.None, () => _context.GroupSelection()),
            Item(Loc.T("Ungroup (Ctrl+Shift+G)"), UiIcon.Ungroup, Keys.None, () => _context.UngroupSelection()),
            new ToolStripSeparator(),
            AlignMenu(),
        ]);
        strip.Items.Add(arrange);

        var transform = Toolbars.Menu(UiIcon.Transform, Loc.T("Picture: rotate, flip, resize"));
        transform.DropDownItems.AddRange([
            Item(Loc.T("Rotate left"), UiIcon.RotateLeft, Keys.None, () => RotateFlip(RotateFlipType.Rotate270FlipNone, Loc.T("Rotate left"))),
            Item(Loc.T("Rotate right"), UiIcon.RotateRight, Keys.None, () => RotateFlip(RotateFlipType.Rotate90FlipNone, Loc.T("Rotate right"))),
            Item(Loc.T("Flip horizontally"), UiIcon.FlipHorizontal, Keys.None, () => RotateFlip(RotateFlipType.RotateNoneFlipX, Loc.T("Flip horizontally"))),
            Item(Loc.T("Flip vertically"), UiIcon.FlipVertical, Keys.None, () => RotateFlip(RotateFlipType.RotateNoneFlipY, Loc.T("Flip vertically"))),
            new ToolStripSeparator(),
            Item(Loc.T("Canvas size…"), UiIcon.Fit, Keys.None, CanvasSizeDialog),
            Item(Loc.T("Resize image…"), UiIcon.ActualSize, Keys.None, ResizeDialog),
            Item(Loc.T("Trim empty borders"), UiIcon.Crop, Keys.None, AutoCrop),
        ]);
        strip.Items.Add(transform);
        strip.Items.Add(Toolbars.Separator());

        strip.Items.Add(Toolbars.Button(UiIcon.GrabText, Loc.T("Grab text (OCR)"), () => _ = GrabTextAsync()));

        var settings = Toolbars.Button(UiIcon.Settings, Loc.T("Settings and capture hotkeys"), () => _host.ShowSettings(this, SettingsTab.Hotkeys));
        settings.Alignment = ToolStripItemAlignment.Right;
        strip.Items.Add(settings);
        return strip;
    }

    // ------------------------------------------------------------------ style bar (top, context-sensitive)

    private ToolStrip BuildStyleBar()
    {
        var strip = Toolbars.NewBar(DockStyle.Top);
        strip.Padding = new Padding(4, 1, 4, 1);

        _strokeButton.ColorChanged += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { Stroke = _strokeButton.Color }); };
        _fillButton.ColorChanged += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { Fill = _fillButton.Color }); };
        strip.Items.Add(_strokeButton);
        strip.Items.Add(_fillButton);

        _gradientButton.ToolTipText = Loc.T("Second fill colour (makes the fill a gradient)");
        _gradientButton.ColorChanged += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { GradientTo = _gradientButton.Color }); };
        strip.Items.Add(_gradientButton);
        FillCombo(_gradientAngleBox, GradientAngles.Select(v => v + "°"), 64);
        _gradientAngleBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _gradientAngleBox.SelectedItem is string t) _context.UpdateStyle(s => s with { GradientAngle = float.Parse(t.TrimEnd('°'), CultureInfo.InvariantCulture) }); };
        strip.Items.Add(_gradientLabel);
        strip.Items.Add(_gradientAngleBox);

        strip.Items.Add(_widthLabel);
        FillCombo(_widthBox, WidthChoices.Select(v => v.ToString(CultureInfo.InvariantCulture)), 54);
        _widthBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _widthBox.SelectedItem is string t) _context.UpdateStyle(s => s with { StrokeWidth = float.Parse(t, CultureInfo.InvariantCulture) }); };
        strip.Items.Add(_widthBox);

        strip.Items.Add(_opacityLabel);
        FillCombo(_opacityBox, OpacityChoices.Select(v => v + "%"), 64);
        _opacityBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _opacityBox.SelectedItem is string t) _context.UpdateStyle(s => s with { Opacity = int.Parse(t.TrimEnd('%'), CultureInfo.InvariantCulture) / 100f }); };
        strip.Items.Add(_opacityBox);

        strip.Items.Add(_dashLabel);
        FillCombo(_dashBox, [Loc.T("Solid"), Loc.T("Dashed"), Loc.T("Dotted")], 80);
        _dashBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _dashBox.SelectedIndex >= 0) _context.UpdateStyle(s => s with { Dash = (LineDash)_dashBox.SelectedIndex }); };
        strip.Items.Add(_dashBox);

        strip.Items.Add(_cornerLabel);
        FillCombo(_cornerBox, CornerChoices.Select(v => v.ToString(CultureInfo.InvariantCulture)), 54);
        _cornerBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _cornerBox.SelectedItem is string t) _context.UpdateStyle(s => s with { CornerRadius = float.Parse(t, CultureInfo.InvariantCulture) }); };
        strip.Items.Add(_cornerBox);

        strip.Items.Add(_headLabel);
        FillCombo(_headBox, [Loc.T("Triangle"), Loc.T("Open"), Loc.T("Circle"), Loc.T("Diamond"), Loc.T("None")], 84);
        _headBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _headBox.SelectedIndex >= 0) _context.UpdateStyle(s => s with { Head = (ArrowHead)_headBox.SelectedIndex }); };
        strip.Items.Add(_headBox);
        MakeToggle(_doubleHeadButton, UiIcon.ArrowBoth, Loc.T("Heads at both ends"));
        _doubleHeadButton.Click += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { DoubleHeaded = _doubleHeadButton.Checked }); };
        strip.Items.Add(_doubleHeadButton);

        strip.Items.Add(_fontSeparator);
        _outlineButton.ToolTipText = Loc.T("Text outline colour");
        _outlineButton.ColorChanged += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { OutlineColor = _outlineButton.Color, OutlineWidth = s.OutlineWidth == 0 && _outlineButton.Color.A > 0 ? 2 : s.OutlineWidth }); };
        strip.Items.Add(_outlineButton);
        FillCombo(_outlineWidthBox, OutlineWidths.Select(v => v.ToString(CultureInfo.InvariantCulture)), 50);
        _outlineWidthBox.ToolTipText = Loc.T("Text outline width");
        _outlineWidthBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _outlineWidthBox.SelectedItem is string t) _context.UpdateStyle(s => s with { OutlineWidth = float.Parse(t, CultureInfo.InvariantCulture) }); };
        strip.Items.Add(_outlineWidthBox);

        _fontBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _fontBox.AutoSize = false;
        _fontBox.Width = 150;
        _fontBox.Items.AddRange(FontFamily.Families.Select(f => f.Name).Cast<object>().ToArray());
        _fontBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _fontBox.SelectedItem is string name) _context.UpdateStyle(s => s with { FontFamily = name }); };
        strip.Items.Add(_fontBox);

        FillCombo(_fontSizeBox, FontSizeChoices.Select(v => v.ToString(CultureInfo.InvariantCulture)), 54);
        _fontSizeBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _fontSizeBox.SelectedItem is string t) _context.UpdateStyle(s => s with { FontSize = float.Parse(t, CultureInfo.InvariantCulture) }); };
        strip.Items.Add(_fontSizeBox);

        MakeToggle(_boldButton, UiIcon.Bold, Loc.T("Bold"));
        _boldButton.Click += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { Bold = _boldButton.Checked }); };
        MakeToggle(_italicButton, UiIcon.Italic, Loc.T("Italic"));
        _italicButton.Click += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { Italic = _italicButton.Checked }); };
        strip.Items.Add(_boldButton);
        strip.Items.Add(_italicButton);

        FillCombo(_alignBox, [Loc.T("Left"), Loc.T("Center"), Loc.T("Right")], 70);
        _alignBox.SelectedIndexChanged += (_, _) => { if (!_syncing && _alignBox.SelectedIndex >= 0) _context.UpdateStyle(s => s with { Align = (TextAlign)_alignBox.SelectedIndex }); };
        strip.Items.Add(_alignBox);

        strip.Items.Add(_arrangeSeparator);
        MakeToggle(_shadowButton, UiIcon.Shadow, Loc.T("Drop shadow"));
        _shadowButton.Click += (_, _) => { if (!_syncing) _context.UpdateStyle(s => s with { Shadow = _shadowButton.Checked }); };
        strip.Items.Add(_shadowButton);

        _presetsButton.Text = Loc.T("Styles");
        _presetsButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        _presetsButton.Image = Icons.Get(UiIcon.Star);
        foreach (var preset in StylePresets.All)
        {
            var captured = preset;
            var item = new ToolStripMenuItem(Loc.T(preset.Name));
            item.Click += (_, _) => _context.UpdateStyle(captured.Apply);
            _presetsButton.DropDownItems.Add(item);
        }
        strip.Items.Add(_presetsButton);

        _stampButton.Text = Loc.T("Stamp");
        _stampButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        _stampButton.Image = StampPreview.Render(_context.CurrentStamp, 40);
        foreach (var kind in Enum.GetValues<StampKind>())
        {
            var captured = kind;
            var item = new ToolStripMenuItem(Loc.T(kind.ToString()), StampPreview.Render(kind, 40));
            item.Click += (_, _) =>
            {
                _context.CurrentStamp = captured;
                _stampButton.Image = StampPreview.Render(captured, 40);
                if (_context.Selection is StampAnnotation selected)
                {
                    // Restamp the selected one: undoable like any other edit.
                    var before = selected.Clone();
                    selected.Kind = captured;
                    _document.History.Execute(new Editor.Commands.ModifyAnnotationCommand(selected, before));
                }
                else
                {
                    _context.SetTool(ToolKind.Stamp);
                }
            };
            _stampButton.DropDownItems.Add(item);
        }
        strip.Items.Add(_stampButton);
        return strip;
    }

    private StatusStrip BuildStatusBar()
    {
        // Left: what the tool does and what is selected. Right: picture size and the zoom controls (where every editor has them).
        var strip = new StatusStrip { SizingGrip = false, ImageScalingSize = new Size(16, 16), Padding = new Padding(6, 0, 6, 0) };

        ToolStripButton Zoom(UiIcon icon, string tip, Action action)
        {
            var button = Toolbars.Button(icon, tip, action);
            button.Padding = new Padding(3);
            return button;
        }

        _zoomLabel.AutoSize = false;
        _zoomLabel.Width = 52;
        _zoomLabel.TextAlign = ContentAlignment.MiddleCenter;
        _selectionLabel.Margin = new Padding(0, 3, 12, 2);
        _sizeLabel.Margin = new Padding(0, 3, 12, 2);

        strip.Items.AddRange([
            _hintLabel, _selectionLabel, _sizeLabel,
            Zoom(UiIcon.ZoomOut, Loc.T("Zoom out (Ctrl+-)"), () => _canvas.ZoomOut()),
            _zoomLabel,
            Zoom(UiIcon.ZoomIn, Loc.T("Zoom in (Ctrl++)"), () => _canvas.ZoomIn()),
            Zoom(UiIcon.Fit, Loc.T("Fit to window (Ctrl+0)"), () => _canvas.ZoomToFit()),
            Zoom(UiIcon.ActualSize, Loc.T("Actual size (Ctrl+1)"), () => _canvas.ZoomActual()),
        ]);
        return strip;
    }

    /// <summary>A toggle (bold, italic, shadow…): 24 px icon button whose pressed state is the tinted, outlined button.</summary>
    private static void MakeToggle(ToolStripButton button, UiIcon icon, string tip)
    {
        button.Image = Icons.Get(icon);
        button.ToolTipText = tip;
        button.AccessibleName = tip;
        button.DisplayStyle = ToolStripItemDisplayStyle.Image;
        button.Padding = new Padding(4);
        button.Margin = new Padding(0, 1, 0, 1);

    }

    private static void FillCombo(ToolStripComboBox combo, IEnumerable<string> values, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.AutoSize = false;
        combo.Width = width;
        combo.Items.AddRange(values.Cast<object>().ToArray());
    }
}

using SnapGit.Core.Imaging;
using SnapGit.Core.Localization;
using SnapGit.Editor.Commands;
using SnapGit.Editor.Tools;

namespace SnapGit.App.Forms;

/// <summary>Whole-image operations (menu: Image) and exports. Each one is a single undoable step.</summary>
public sealed partial class EditorForm
{
    private const string MergedNote = " — annotations were merged into the image (Undo restores them).";

    private void Apply(string name, Func<Bitmap, Bitmap> operation, bool flatten = true, Point shift = default)
    {
        try
        {
            _context.SetSelection([]);
            _document.ApplyImageOperation(name, operation, flatten, shift);
            var hadAnnotations = flatten && _document.History.CanUndo;
            SetStatus(name + (flatten ? Loc.T(MergedNote) : string.Empty));
        }
        catch (Exception ex)
        {
            ShowError(Loc.T("The operation failed:\n\n{0}", ex.Message));
        }
    }

    private void RotateFlip(RotateFlipType type, string name) => Apply(name, b => ImageOps.RotateFlip(b, type));

    private void Effect(string name, Func<Bitmap, Bitmap> effect) => Apply(name, effect);

    private void AutoCrop()
    {
        var bounds = ImageOps.FindContentBounds(_document.Image);
        if (bounds.Size == _document.Image.Size) { SetStatus(Loc.T("There are no empty borders to trim.")); return; }
        _context.SetSelection([]);
        _document.History.Execute(new CropCommand(_document, bounds));
        SetStatus(Loc.T("Trimmed empty borders."));
    }

    private void CanvasSizeDialog()
    {
        var image = _document.Image;
        var dialog = new ParameterDialog(Loc.T("Canvas size"))
            .AddLabel(Loc.T("Changes the size of the canvas without scaling the picture. Annotations keep their place."))
            .AddNumber("w", Loc.T("Width (px)"), image.Width, 1, 20000)
            .AddNumber("h", Loc.T("Height (px)"), image.Height, 1, 20000)
            .AddCombo("anchor", Loc.T("Anchor"),
            [
                Loc.T("Top left"), Loc.T("Top"), Loc.T("Top right"),
                Loc.T("Left"), Loc.T("Center"), Loc.T("Right"),
                Loc.T("Bottom left"), Loc.T("Bottom"), Loc.T("Bottom right"),
            ], 4)
            .AddColor("bg", Loc.T("New area color"), Color.White)
            .AddCheck("transparent", Loc.T("Transparent new area"), false);
        if (!dialog.Prompt(this)) return;

        int w = dialog.Int("w"), h = dialog.Int("h"), anchor = dialog.Index("anchor");
        var offset = new Point((anchor % 3) * (w - image.Width) / 2, (anchor / 3) * (h - image.Height) / 2);
        var background = dialog.Bool("transparent") ? Color.Transparent : dialog.ColorValue("bg");

        // The picture moves by +offset, so annotations must move the same way: shift is the NEGATIVE offset.
        Apply(Loc.T("Canvas size"), b => ImageOps.ResizeCanvas(b, w, h, offset, background), flatten: false, shift: new Point(-offset.X, -offset.Y));
    }

    private void ResizeDialog()
    {
        var image = _document.Image;
        var ratio = (double)image.Height / image.Width;
        var dialog = new ParameterDialog(Loc.T("Resize image"))
            .AddNumber("w", Loc.T("Width (px)"), image.Width, 1, 20000)
            .AddNumber("h", Loc.T("Height (px)"), image.Height, 1, 20000)
            .AddCheck("keep", Loc.T("Keep aspect ratio (height follows width)"), true);
        if (!dialog.Prompt(this)) return;

        var w = dialog.Int("w");
        var h = dialog.Bool("keep") ? Math.Max(1, (int)Math.Round(w * ratio)) : dialog.Int("h");
        Apply(Loc.T("Resize image"), b => ImageOps.Scale(b, w, h));
    }

    private void BorderDialog()
    {
        var dialog = new ParameterDialog(Loc.T("Border"))
            .AddNumber("t", Loc.T("Thickness (px)"), 4, 1, 500)
            .AddColor("c", Loc.T("Color"), Color.FromArgb(60, 60, 60));
        if (!dialog.Prompt(this)) return;

        int thickness = dialog.Int("t");
        var color = dialog.ColorValue("c");
        Apply(Loc.T("Border"), b => ImageOps.AddBorder(b, thickness, color));
    }

    private void ShadowDialog()
    {
        var dialog = new ParameterDialog(Loc.T("Drop shadow"))
            .AddNumber("o", Loc.T("Offset (px)"), 6, 0, 100)
            .AddNumber("b", Loc.T("Blur (px)"), 10, 1, 100)
            .AddColor("c", Loc.T("Shadow color"), Color.FromArgb(150, 0, 0, 0))
            .AddCheck("transparent", Loc.T("Transparent background"), true);
        if (!dialog.Prompt(this)) return;

        int offset = dialog.Int("o"), blur = dialog.Int("b");
        var color = dialog.ColorValue("c");
        var background = dialog.Bool("transparent") ? Color.Transparent : Color.White;
        Apply(Loc.T("Drop shadow"), b => ImageOps.AddShadow(b, offset, blur, color, background));
    }

    private void TornEdgeDialog()
    {
        var dialog = new ParameterDialog(Loc.T("Torn edges"))
            .AddNumber("a", Loc.T("Depth (px)"), 8, 1, 100)
            .AddNumber("t", Loc.T("Tooth width (px)"), 8, 2, 100)
            .AddCheck("top", Loc.T("Top"), false)
            .AddCheck("right", Loc.T("Right"), false)
            .AddCheck("bottom", Loc.T("Bottom"), true)
            .AddCheck("left", Loc.T("Left"), false);
        if (!dialog.Prompt(this)) return;

        var sides = EdgeSides.None;
        if (dialog.Bool("top")) sides |= EdgeSides.Top;
        if (dialog.Bool("right")) sides |= EdgeSides.Right;
        if (dialog.Bool("bottom")) sides |= EdgeSides.Bottom;
        if (dialog.Bool("left")) sides |= EdgeSides.Left;
        if (sides == EdgeSides.None) return;

        int amplitude = dialog.Int("a"), tooth = dialog.Int("t");
        Apply(Loc.T("Torn edges"), b => ImageOps.TornEdge(b, amplitude, tooth, sides));
    }

    private void BrightnessDialog()
    {
        var dialog = new ParameterDialog(Loc.T("Brightness / contrast"))
            .AddNumber("b", Loc.T("Brightness (-100..100)"), 0, -100, 100)
            .AddNumber("c", Loc.T("Contrast (-100..100)"), 0, -100, 100);
        if (!dialog.Prompt(this)) return;

        int brightness = dialog.Int("b"), contrast = dialog.Int("c");
        Apply(Loc.T("Brightness / contrast"), b => ImageOps.BrightnessContrast(b, brightness, contrast));
    }

    private void ReplaceColorDialog()
    {
        var dialog = new ParameterDialog(Loc.T("Replace color"))
            .AddLabel(Loc.T("Tip: pick the exact color first with the Eyedropper tool (I), it then appears in the color menus."))
            .AddColor("from", Loc.T("Replace this color"), _context.GetToolStyle(ToolKind.Arrow).Stroke)
            .AddColor("to", Loc.T("With"), Color.FromArgb(30, 136, 229))
            .AddNumber("tol", Loc.T("Tolerance (0..255)"), 24, 0, 255);
        if (!dialog.Prompt(this)) return;

        var from = dialog.ColorValue("from");
        var to = dialog.ColorValue("to");
        int tolerance = dialog.Int("tol");
        Apply(Loc.T("Replace color"), b => ImageOps.ReplaceColor(b, from, to, tolerance));
    }

    private void ExportDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg|" + Loc.T("Bitmap") + "|*.bmp|GIF|*.gif|PDF|*.pdf",
            FileName = DisplayName,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) ExportTo(dialog.FileName);
    }

    private async Task GrabTextAsync()
    {
        SetStatus(Loc.T("Reading…"));
        var copy = _document.Render();
        var form = new GrabTextForm(_host, copy, initial: null);
        form.Show(this);
        await form.ScanAsync();
    }
}

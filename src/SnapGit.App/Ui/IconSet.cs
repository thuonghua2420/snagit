using System.Globalization;

namespace SnapGit.App.Ui;

/// <summary>One drawn element of an icon: a filled and/or outlined path, or a piece of text. Colours are resolved at render time (the theme can change them).</summary>
internal sealed record Piece(string? Path, Func<Color>? Fill, Func<Color>? Stroke, float Width = 1f, bool Gloss = false, bool Dashed = false, float FillAlpha = 1f, IconLabel? Text = null);

internal sealed record IconLabel(string Text, RectangleF Box, float Size, Func<Color> Color, bool Bold = true, bool Italic = false);

/// <summary>
/// The icon artwork: small colour icons on a 16 × 16 grid in the classic desktop-toolbar style — 1 px outlines aligned to the pixel grid,
/// soft top-to-bottom gloss on filled shapes, a blue family for the drawing tools (light-blue body, darker outline, white handles),
/// and one hue per kind of thing (yellow pencil/folder/callout, red step badge/record, green play/refresh/import, brown clipboard/crop).
/// Outline coordinates sit on .5 so a 1 px line fills exactly one pixel row at 16 px (and exactly two at 32 px).
/// </summary>
internal static class IconSet
{
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static Func<Color> Rgb(int r, int g, int b) => () => Color.FromArgb(r, g, b);

    // ---------------------------------------------------------------- palette
    private static readonly Func<Color> Ink = () => Theme.IsDark ? Color.FromArgb(222, 226, 234) : Color.FromArgb(58, 64, 78);
    private static readonly Func<Color> Steel = Rgb(116, 128, 146);
    private static readonly Func<Color> Silver = Rgb(206, 211, 220);
    private static readonly Func<Color> Paper = Rgb(251, 251, 253);
    private static readonly Func<Color> Navy = Rgb(44, 62, 100);
    private static readonly Func<Color> Blue = Rgb(40, 112, 208);
    private static readonly Func<Color> BlueDeep = Rgb(26, 76, 156);
    private static readonly Func<Color> BlueLine = Rgb(96, 138, 200);
    private static readonly Func<Color> BlueFill = Rgb(200, 220, 245);
    private static readonly Func<Color> Sky = Rgb(128, 184, 240);
    private static readonly Func<Color> Red = Rgb(218, 66, 58);
    private static readonly Func<Color> RedDark = Rgb(146, 32, 32);
    private static readonly Func<Color> Orange = Rgb(240, 150, 46);
    private static readonly Func<Color> Brown = Rgb(150, 92, 44);
    private static readonly Func<Color> Tan = Rgb(220, 174, 112);
    private static readonly Func<Color> Yellow = Rgb(255, 222, 66);
    private static readonly Func<Color> YellowFill = Rgb(255, 241, 176);
    private static readonly Func<Color> Gold = Rgb(190, 142, 22);
    private static readonly Func<Color> Green = Rgb(62, 166, 86);
    private static readonly Func<Color> GreenDark = Rgb(30, 110, 52);
    private static readonly Func<Color> White = () => Color.White;

    // ---------------------------------------------------------------- piece builders (grid units)
    /// <summary>Outlined box: the path runs along the pixel centres of the outline.</summary>
    private static string Box(double x, double y, double w, double h, double r = 0)
    {
        x += .5; y += .5; w -= 1; h -= 1;
        return r <= 0
            ? $"M{F(x)} {F(y)}h{F(w)}v{F(h)}h{F(-w)}z"
            : $"M{F(x + r)} {F(y)}h{F(w - 2 * r)}a{F(r)} {F(r)} 0 0 1 {F(r)} {F(r)}v{F(h - 2 * r)}a{F(r)} {F(r)} 0 0 1 {F(-r)} {F(r)}h{F(-(w - 2 * r))}a{F(r)} {F(r)} 0 0 1 {F(-r)} {F(-r)}v{F(-(h - 2 * r))}a{F(r)} {F(r)} 0 0 1 {F(r)} {F(-r)}z";
    }

    private static string Circle(double cx, double cy, double r) => $"M{F(cx - r)} {F(cy)}a{F(r)} {F(r)} 0 1 0 {F(2 * r)} 0a{F(r)} {F(r)} 0 1 0 {F(-2 * r)} 0z";

    private static Piece Solid(string path, Func<Color> fill, Func<Color>? stroke = null, bool gloss = true, float alpha = 1f) => new(path, fill, stroke, 1f, gloss, false, alpha);

    private static Piece Line(string path, Func<Color> stroke, float width = 1f, bool dashed = false) => new(path, null, stroke, width, false, dashed);

    private static Piece Text(string text, double x, double y, double w, double h, float size, Func<Color> color, bool bold = true, bool italic = false) =>
        new(null, null, null, Text: new IconLabel(text, new RectangleF((float)x, (float)y, (float)w, (float)h), size, color, bold, italic));

    /// <summary>A small white round handle (the corner/end marks of the drawing tools).</summary>
    private static Piece Handle(double cx, double cy) => Solid(Circle(cx, cy, 1.45), White, BlueLine, gloss: false);

    private static readonly string[] Brackets = ["M1.5 5.5V3a1.5 1.5 0 0 1 1.5-1.5h2.5", "M10.5 1.5H13a1.5 1.5 0 0 1 1.5 1.5v2.5", "M14.5 10.5V13a1.5 1.5 0 0 1-1.5 1.5h-2.5", "M5.5 14.5H3A1.5 1.5 0 0 1 1.5 13v-2.5"];

    private static Piece[] ViewfinderPieces(Func<Color> colour) => [.. Brackets.Select(b => Line(b, colour, 1.6f))];

    // ---------------------------------------------------------------- lookup
    public static IReadOnlyList<Piece> Of(UiIcon icon) => Definitions.TryGetValue(icon, out var pieces) ? pieces : Definitions[UiIcon.Info];

    public static bool IsDefined(UiIcon icon) => Definitions.ContainsKey(icon) || Derived(icon) is not null;

    /// <summary>How a derived icon is made from another: mirrored horizontally, or turned a quarter clockwise.</summary>
    public static (UiIcon Source, bool Mirror, bool Turn)? Derived(UiIcon icon) => icon switch
    {
        UiIcon.Redo => (UiIcon.Undo, true, false),
        UiIcon.RotateRight => (UiIcon.RotateLeft, true, false),
        UiIcon.FlipVertical => (UiIcon.FlipHorizontal, false, true),
        UiIcon.AlignTop => (UiIcon.AlignLeft, false, true),
        UiIcon.AlignMiddle => (UiIcon.AlignCenter, false, true),
        UiIcon.AlignBottom => (UiIcon.AlignRight, false, true),
        UiIcon.DistributeV => (UiIcon.DistributeH, false, true),
        _ => null,
    };

    private static string Gear()
    {
        const double outer = 7.2, root = 5.5;
        var points = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var centre = i * Math.PI / 4;
            foreach (var (angle, radius) in new[] { (centre - 0.32, root), (centre - 0.2, outer), (centre + 0.2, outer), (centre + 0.32, root) })
                points.Add($"{F(8 + radius * Math.Cos(angle))} {F(8 + radius * Math.Sin(angle))}");
        }
        return "M" + string.Join("L", points) + "z";
    }

    private static string Star()
    {
        var points = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? 7 : 3.1;
            var angle = -Math.PI / 2 + i * Math.PI / 5;
            points.Add($"{F(8 + radius * Math.Cos(angle))} {F(8.6 + radius * Math.Sin(angle))}");
        }
        return "M" + string.Join("L", points) + "z";
    }

    private static Piece[] Pixelate()
    {
        var cells = new List<Piece>();
        Func<Color>[] tones = [Sky, BlueLine, BlueFill, Blue, Sky, BlueFill, BlueLine, Sky, Blue];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                cells.Add(Solid($"M{F(1.5 + col * 4.4)} {F(1.5 + row * 4.4)}h3.8v3.8h-3.8z", tones[row * 3 + col], null, gloss: false));
        return [.. cells];
    }

    private static Piece[] Pencil() =>
    [
        Solid("M11.2 2.6L13.4 4.8L5.4 12.8L2.6 13.4L3.2 10.6Z", Yellow, Gold),
        Solid("M11.2 2.6L12.5 1.3Q13.3 .7 14 1.4L14.6 2Q15.3 2.7 14.7 3.5L13.4 4.8Z", Red, RedDark, gloss: false),
        Solid("M3.2 10.6L5.4 12.8L2.6 13.4Z", Tan, Brown, gloss: false),
        Line("M3 13l.5-1.2", Ink, 1.2f),
    ];

    private static readonly Dictionary<UiIcon, Piece[]> Definitions = new()
    {
        // ---------------------------------------------------------------- editor tools
        [UiIcon.Select] = [Solid("M3.5 1.5V13.2L6.4 10.4L8.4 14.7L10.5 13.8L8.5 9.5H12.4Z", Paper, Steel, gloss: false)],
        [UiIcon.Arrow] = [Line("M3.6 3.6L10 10", BlueLine, 1.2f), Solid("M13.4 13.4L8.4 11.8L11.8 8.4Z", Blue, BlueDeep, gloss: false), Handle(3.6, 3.6)],
        [UiIcon.Line] = [Line("M3.6 12.4L12.4 3.6", BlueLine, 1.2f), Handle(3.6, 12.4), Handle(12.4, 3.6)],
        [UiIcon.Rectangle] = [Solid(Box(3, 4, 10, 8, 1), BlueFill, BlueLine), Handle(3.5, 4.5), Handle(12.5, 4.5), Handle(3.5, 11.5), Handle(12.5, 11.5)],
        [UiIcon.Ellipse] = [Solid("M2.6 8a5.4 4.4 0 1 0 10.8 0a5.4 4.4 0 1 0-10.8 0z", BlueFill, BlueLine), Handle(8, 3.6), Handle(8, 12.4), Handle(2.6, 8), Handle(13.4, 8)],
        [UiIcon.Pen] = [Line("M2 9.5C2.6 4.8 5.6 4.4 6.2 7.2S9 11.6 10.2 8", BlueLine, 1.1f), .. Pencil()],
        [UiIcon.Highlight] = [Solid(Box(1, 3, 14, 8, 1.5), Rgb(255, 240, 60), Gold, gloss: false), Text("abc", 1, 2.6, 14, 8.6, 7.4f, Navy), Line("M2 13.5h12", Yellow, 1.6f)],
        [UiIcon.Text] = [Solid("M2.5 2.5H13.5V5.5H11.5V4H9V12H10.5V13.5H5.5V12H7V4H4.5V5.5H2.5Z", BlueFill, BlueLine, gloss: false), Handle(2.5, 13.5)],
        [UiIcon.Callout] = [Solid("M3 2.5H13A1.5 1.5 0 0 1 14.5 4V8.5A1.5 1.5 0 0 1 13 10H8.5L5.5 13.5V10H3A1.5 1.5 0 0 1 1.5 8.5V4A1.5 1.5 0 0 1 3 2.5Z", YellowFill, Gold), Line("M4 5.3h8", Gold, 1f), Line("M4 7.5h5.5", Gold, 1f)],
        [UiIcon.Step] = [Solid(Box(1.5, 1.5, 13, 13, 3), Red, RedDark), Text("1", 1.5, 1.2, 13, 13, 9.5f, White)],
        [UiIcon.Stamp] = [Solid(Box(1.5, 1.5, 13, 13, 6.5), Green, GreenDark), Line("M4.8 8.3L7 10.5L11.4 5.8", White, 1.7f)],
        [UiIcon.Blur] = Pixelate(),
        [UiIcon.Spotlight] = [Solid(Box(1, 2, 14, 12, 1) + Circle(8, 8, 3.8), Rgb(48, 56, 76), null, gloss: false, alpha: .82f), Line(Circle(8, 8, 3.8), Yellow, 1.3f)],
        [UiIcon.Magnify] = [Solid(Circle(6.4, 6.4, 4.4), Rgb(228, 242, 255), Steel), Line("M9.7 9.7L14 14", Brown, 2.3f), Line("M4.4 6.4h4M6.4 4.4v4", Blue, 1.1f)],
        [UiIcon.Eyedropper] = [Solid("M10 4.6L11.4 6L5.2 12.2L3 13.3L2.7 13L3.8 10.8Z", Silver, Steel), Solid("M10.6 5.2L12.2 3.6Q13.5 2.3 14.6 3.4Q15.7 4.5 14.4 5.8L12.8 7.4Z", Blue, BlueDeep, gloss: false), Line("M3.4 12.6l.9-.9", Ink, 1.2f)],
        [UiIcon.Fill] = [Solid("M8.4 2.4L13.6 7.6L9.4 11.8Q8.3 12.8 7.3 11.8L3.4 7.9Q2.5 7 3.4 6Z", Blue, BlueDeep), Line("M3.8 7.4L12.8 7.4", BlueFill, 1f), Solid("M13.5 10Q15.4 12.4 13.5 14Q11.6 12.4 13.5 10Z", Sky, Blue, gloss: false)],
        [UiIcon.Crop] = [Solid(Box(3, 3, 10, 10), Tan, Brown), Line("M5.5 1V10.5H15", Blue, 1.7f), Line("M1 5.5H10.5V15", Blue, 1.7f)],
        [UiIcon.CutOut] = [Solid(Box(1, 3, 5, 10, 1), BlueFill, BlueLine), Solid(Box(10, 3, 5, 10, 1), BlueFill, BlueLine), Line("M8 2.5v2.2M8 6.9v2.2M8 11.3v2.2", Red, 1.4f)],
        [UiIcon.SmartMove] = [Line("M8 1.8V14.2M1.8 8H14.2", Steel, 1.1f), Line("M6 3.8L8 1.8L10 3.8M6 12.2L8 14.2L10 12.2M3.8 6L1.8 8L3.8 10M12.2 6L14.2 8L12.2 10", Steel, 1.2f), Solid(Box(5.5, 5.5, 5, 5), BlueFill, BlueLine, gloss: false)],

        // ---------------------------------------------------------------- editing actions
        [UiIcon.Undo] = [Line("M4.2 6.4H9.4A3.3 3.3 0 0 1 9.4 13H6", Steel, 1.7f), Line("M6.8 3.4L3.6 6.4L6.8 9.4", Steel, 1.7f)],
        [UiIcon.Copy] = [Solid(Box(2, 1, 8, 10, 1), Paper, Steel, gloss: false), Solid(Box(6, 5, 8, 10, 1), Paper, Steel, gloss: false), Line("M8 8h4M8 10h4M8 12h2.5", BlueLine, 1f)],
        [UiIcon.Paste] = [Solid(Box(2, 2.5, 12, 12.5, 1.5), Tan, Brown), Solid(Box(5, 1, 6, 3.2, 1), Silver, Steel, gloss: false), Solid(Box(5, 6, 7.5, 7.5), Paper, Steel, gloss: false), Line("M6.8 8.5h4M6.8 10.5h4", BlueLine, 1f)],
        [UiIcon.Save] = [Solid(Box(1.5, 1.5, 13, 13, 1.5), Navy, Ink), Solid(Box(4, 2, 8, 5), Rgb(214, 238, 250), null, gloss: false), Solid(Box(4.5, 9, 7, 5.5), Silver, Steel, gloss: false), Line("M9.5 10v3", Steel, 1.3f)],
        [UiIcon.SaveAs] = [Solid(Box(1.5, 1.5, 12, 12, 1.5), Navy, Ink), Solid(Box(4, 2, 7, 4.5), Rgb(214, 238, 250), null, gloss: false), Solid(Box(4, 8.5, 6.5, 5), Silver, Steel, gloss: false), Solid(Circle(12.4, 12.4, 3), Green, GreenDark), Line("M12.4 10.7v3.4M10.7 12.4h3.4", White, 1.3f)],
        [UiIcon.Delete] = [Line("M4 4L12 12M12 4L4 12", Red, 2.3f)],
        [UiIcon.ZoomIn] = [Solid(Circle(6.4, 6.4, 4.4), Rgb(228, 242, 255), Steel), Line("M9.7 9.7L14 14", Brown, 2.3f), Line("M4.4 6.4h4M6.4 4.4v4", Blue, 1.1f)],
        [UiIcon.ZoomOut] = [Solid(Circle(6.4, 6.4, 4.4), Rgb(228, 242, 255), Steel), Line("M9.7 9.7L14 14", Brown, 2.3f), Line("M4.4 6.4h4", Blue, 1.1f)],
        [UiIcon.Fit] = [Line("M1.5 5.5V3A1.5 1.5 0 0 1 3 1.5H5.5M10.5 1.5H13A1.5 1.5 0 0 1 14.5 3V5.5M14.5 10.5V13A1.5 1.5 0 0 1 13 14.5H10.5M5.5 14.5H3A1.5 1.5 0 0 1 1.5 13V10.5", Blue, 1.5f), Solid(Box(5, 5, 6, 6), BlueFill, BlueLine, gloss: false)],
        [UiIcon.ActualSize] = [Solid(Box(1, 3, 14, 10, 1.5), Paper, Steel, gloss: false), Text("1:1", 1, 2.6, 14, 10, 6.6f, Navy)],
        [UiIcon.Back] = [Solid(Box(1.5, 1.5, 13, 13, 6.5), Blue, BlueDeep), Line("M11.4 8H5M7.8 5.2L5 8L7.8 10.8", White, 1.7f)],
        [UiIcon.Library] = [Solid(Box(1, 1, 6.4, 6.4, 1), Sky, Blue), Solid(Box(8.6, 1, 6.4, 6.4, 1), Rgb(160, 214, 160), Green), Solid(Box(1, 8.6, 6.4, 6.4, 1), Rgb(250, 208, 140), Orange), Solid(Box(8.6, 8.6, 6.4, 6.4, 1), Rgb(212, 180, 232), Rgb(136, 84, 160))],
        [UiIcon.Play] = [Solid(Box(1.5, 1.5, 13, 13, 6.5), Green, GreenDark), Solid("M6.3 4.8L11.6 8L6.3 11.2Z", White, null, gloss: false)],
        [UiIcon.Pause] = [Solid(Box(1.5, 1.5, 13, 13, 6.5), Blue, BlueDeep), Solid("M5.4 5h1.9v6H5.4zM8.7 5h1.9v6H8.7z", White, null, gloss: false)],
        [UiIcon.Volume] = [Solid("M1.5 6H4.6L8.6 2.8V13.2L4.6 10H1.5Z", Steel, Ink), Line("M10.8 5.7A3.2 3.2 0 0 1 10.8 10.3", Blue, 1.3f), Line("M12.8 3.9A5.6 5.6 0 0 1 12.8 12.1", Blue, 1.3f)],
        [UiIcon.Muted] = [Solid("M1.5 6H4.6L8.6 2.8V13.2L4.6 10H1.5Z", Steel, Ink), Line("M10.8 5.8L14.8 9.8M14.8 5.8L10.8 9.8", Red, 1.6f)],
        [UiIcon.Camera] = [Solid("M1.5 5.5Q1.5 4.5 2.5 4.5H4.8L5.8 2.6H10.2L11.2 4.5H13.5Q14.5 4.5 14.5 5.5V12.5Q14.5 13.5 13.5 13.5H2.5Q1.5 13.5 1.5 12.5Z", Steel, Ink), Solid(Circle(8, 8.6, 3.2), Sky, Navy), Solid(Circle(8, 8.6, 1.2), White, null, gloss: false)],
        [UiIcon.Shadow] = [Solid("M5 5h9v9H5z", Rgb(0, 0, 0), null, gloss: false, alpha: .3f), Solid(Box(1.5, 1.5, 9, 9, 1), Paper, Steel, gloss: false)],
        [UiIcon.Bold] = [Text("B", 0, 0, 16, 16, 14.5f, Ink)],
        [UiIcon.Italic] = [Text("I", 0, 0, 16, 16, 14.5f, Ink, bold: false, italic: true)],
        [UiIcon.Duplicate] = [Solid(Box(2, 1, 8, 10, 1), Paper, Steel, gloss: false), Solid(Box(6, 5, 8, 10, 1), Paper, Steel, gloss: false), Solid(Circle(11.6, 11.8, 2.8), Green, GreenDark), Line("M11.6 10.4v2.8M10.2 11.8h2.8", White, 1.2f)],
        [UiIcon.BringFront] = [Solid(Box(1.5, 1.5, 8, 8, 1), BlueFill, BlueLine, gloss: false), Solid(Box(6.5, 6.5, 8, 8, 1), Blue, BlueDeep)],
        [UiIcon.SendBack] = [Solid(Box(1.5, 1.5, 8, 8, 1), Blue, BlueDeep), Solid(Box(6.5, 6.5, 8, 8, 1), BlueFill, BlueLine, gloss: false, alpha: .92f)],
        [UiIcon.RotateLeft] = [Line("M3 8.2A5.2 5.2 0 1 0 4.9 4.2", Blue, 1.7f), Line("M2.6 2.2V6H6.4", Blue, 1.7f)],
        [UiIcon.FlipHorizontal] = [Line("M8 1.5v2M8 5.8v2.4M8 10.6v2M8 14v.5", Steel, 1.2f), Solid("M6.6 3.5V13H1.6Z", BlueFill, BlueLine), Solid("M9.4 3.5V13H14.4Z", Blue, BlueDeep)],
        [UiIcon.Image] = [Solid(Box(1, 2, 14, 12, 1), Rgb(214, 234, 252), Steel, gloss: false), Solid(Circle(5, 6, 1.6), Yellow, Gold, gloss: false), Solid("M1.6 13.4L5.8 8.6L8.8 11.6L11.2 9L14.4 13.4Z", Green, GreenDark, gloss: false)],
        [UiIcon.Print] = [Solid(Box(4, 1, 8, 5), Paper, Steel, gloss: false), Solid(Box(1, 5.5, 14, 6, 1.5), Rgb(150, 160, 176), Ink), Solid(Box(4, 9.5, 8, 5.5), Paper, Steel, gloss: false), Line("M5.8 11.6h4.4M5.8 13.3h3", BlueLine, 1f), Solid(Circle(12.4, 7.6, .7), Green, null, gloss: false)],
        [UiIcon.Cut] = [Line("M4.6 1.8L10 9.6M11.4 1.8L6 9.6", Steel, 1.3f), Solid(Circle(4.8, 12, 2.2), Paper, Red), Solid(Circle(11.2, 12, 2.2), Paper, Red)],
        [UiIcon.Group] = [.. Brackets.Select(b => Line(b, BlueLine, 1.2f)), Solid(Box(4, 4, 5, 5), BlueFill, BlueLine, gloss: false), Solid(Box(7, 7, 5, 5), Sky, Blue, gloss: false)],
        [UiIcon.Ungroup] = [.. Brackets.Select(b => Line(b, BlueLine, 1.2f, dashed: true)), Solid(Box(3, 3, 4.5, 4.5), BlueFill, BlueLine, gloss: false), Solid(Box(8.5, 8.5, 4.5, 4.5), Sky, Blue, gloss: false)],
        [UiIcon.AlignLeft] = [Line("M2.5 1.5V14.5", Blue, 1.4f), Solid(Box(4.5, 3, 9, 3.4, 1), BlueFill, BlueLine, gloss: false), Solid(Box(4.5, 9.4, 6, 3.4, 1), BlueFill, BlueLine, gloss: false)],
        [UiIcon.AlignCenter] = [Line("M8 1.5V14.5", Blue, 1.4f), Solid(Box(2.5, 3, 11, 3.4, 1), BlueFill, BlueLine, gloss: false), Solid(Box(4.5, 9.4, 7, 3.4, 1), BlueFill, BlueLine, gloss: false)],
        [UiIcon.AlignRight] = [Line("M13.5 1.5V14.5", Blue, 1.4f), Solid(Box(2.5, 3, 9, 3.4, 1), BlueFill, BlueLine, gloss: false), Solid(Box(5.5, 9.4, 6, 3.4, 1), BlueFill, BlueLine, gloss: false)],
        [UiIcon.DistributeH] = [Line("M2.5 1.5V14.5M13.5 1.5V14.5", Blue, 1.4f), Solid(Box(6, 4, 4, 8, 1), BlueFill, BlueLine, gloss: false)],
        [UiIcon.Trim] = [Line("M5 2.5H2.5V13.5H5M11 2.5H13.5V13.5H11", Blue, 1.5f), Line("M6.4 8h3.2", Red, 1.4f)],
        [UiIcon.Profile] = [Solid(Circle(8, 5.2, 2.9), Sky, Blue), Solid("M2.6 14.4Q2.6 9.2 8 9.2Q13.4 9.2 13.4 14.4Z", Blue, BlueDeep)],
        [UiIcon.Export] = [Line("M8 10V2.6M4.8 5.6L8 2.4L11.2 5.6", Blue, 1.7f), Line("M2.5 9.5V13a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V9.5", Steel, 1.4f)],
        [UiIcon.Arrange] = [Solid("M8 1.8L14.4 5.4L8 9L1.6 5.4Z", Blue, BlueDeep), Line("M1.8 8.4L8 11.9L14.2 8.4", BlueLine, 1.4f), Line("M1.8 11.4L8 14.9L14.2 11.4", Sky, 1.4f)],
        [UiIcon.Transform] = [Line("M2.8 8A5.2 5.2 0 0 1 11.9 4.6", Blue, 1.6f), Line("M12.4 2.2V5.6H9", Blue, 1.6f), Line("M13.2 8A5.2 5.2 0 0 1 4.1 11.4", Green, 1.6f), Line("M3.6 13.8V10.4H7", Green, 1.6f)],
        [UiIcon.Keyboard] = [Solid(Box(1, 3.5, 14, 9, 1.5), Silver, Steel), Line("M3.4 6.3h1M5.9 6.3h1M8.4 6.3h1M10.9 6.3h1.6M3.4 8.6h1M5.9 8.6h1M8.4 8.6h1M10.9 8.6h1.6", Ink, 1.2f), Line("M4.8 10.6h6.4", Ink, 1.2f)],
        [UiIcon.ArrowBoth] = [Line("M4.4 11.6L11.6 4.4", BlueLine, 1.2f), Solid("M13.6 2.4L13.6 8L8 2.4Z", Blue, BlueDeep, gloss: false), Solid("M2.4 13.6L2.4 8L8 13.6Z", Blue, BlueDeep, gloss: false)],

        // ---------------------------------------------------------------- capture / library
        [UiIcon.Capture] = [.. ViewfinderPieces(Blue), Solid(Circle(8, 8, 2.6), Red, RedDark)],
        [UiIcon.Window] = [Solid(Box(1, 2, 14, 12, 1.5), Paper, Steel, gloss: false), Solid("M1.5 5.2V3.5A1 1 0 0 1 2.5 2.5H13.5A1 1 0 0 1 14.5 3.5V5.2Z", Blue, BlueDeep, gloss: false), Solid(Circle(3.4, 4, .6), White, null, gloss: false)],
        [UiIcon.FullScreen] = [Solid(Box(1, 2, 14, 9.5, 1.2), Rgb(70, 82, 104), Ink), Solid(Box(2.5, 3.5, 11, 6.5), Sky, null), Line("M8 11.5V13.6M5 14h6", Steel, 1.4f)],
        [UiIcon.Scroll] = [Solid(Box(3, 1, 10, 14, 1.5), Paper, Steel, gloss: false), Line("M8 4.5V10.8M5.4 8.4L8 11L10.6 8.4", Blue, 1.6f), Line("M11.2 3.5v9", Silver, 1.2f)],
        [UiIcon.FreeHand] = [Solid("M8 2.8C12.6 2.8 15 4.6 15 7S12.4 11.2 8 11.2C3.6 11.2 1 9.4 1 7S3.4 2.8 8 2.8Z", BlueFill, BlueLine, alpha: .55f), Line("M5.4 11C5.2 13 4.4 13.8 2.8 14.4", Steel, 1.2f)],
        [UiIcon.MultiArea] = [Solid(Box(1, 1, 9, 8, 1), BlueFill, BlueLine, alpha: .8f), Solid(Box(6, 7, 9, 8, 1), Sky, Blue, alpha: .8f)],
        [UiIcon.GrabText] = [.. ViewfinderPieces(Blue), Text("T", 4, 3.5, 8, 9, 8.6f, Navy)],
        [UiIcon.Repeat] = [Line("M13.4 6.4A5.4 5.4 0 0 0 4 4.6", Green, 1.6f), Line("M3.6 1.8V5.2H7", Green, 1.6f), Line("M2.6 9.6A5.4 5.4 0 0 0 12 11.4", Green, 1.6f), Line("M12.4 14.2V10.8H9", Green, 1.6f)],
        [UiIcon.Record] = [Solid(Circle(8, 8, 6.5), Red, RedDark), Line(Circle(8, 8, 4.3), Rgb(255, 190, 186), 1f)],
        [UiIcon.Gif] = [Solid(Box(1, 3, 14, 10, 2), Rgb(46, 150, 120), GreenDark), Text("GIF", 1, 2.8, 14, 10, 6.6f, White)],
        [UiIcon.Video] = [Solid(Box(1, 4, 9.5, 8, 1.5), Steel, Ink), Solid("M10.8 6.6L14.4 4.8Q15 4.5 15 5.2V10.8Q15 11.5 14.4 11.2L10.8 9.4Z", Blue, BlueDeep, gloss: false), Solid(Circle(5.8, 8, 1.7), Sky, Navy, gloss: false)],
        [UiIcon.Folder] = [Solid("M1.5 3.5H6L7.4 5H14.5V13.5H1.5Z", Yellow, Gold), Solid(Box(1.5, 6.5, 13, 7), Rgb(255, 232, 120), Gold)],
        [UiIcon.Settings] = [Solid(Gear(), Steel, Ink), Solid(Circle(8, 8, 2.3), Paper, Ink, gloss: false)],
        [UiIcon.Refresh] = [Line("M13.5 8A5.5 5.5 0 1 1 11.9 4.1", Green, 1.7f), Line("M13.6 1.8V5.4H10", Green, 1.7f)],
        [UiIcon.Edit] = [.. Pencil()],
        [UiIcon.Share] = [Line("M4.6 8L11.4 4.2M4.6 8L11.4 11.8", Steel, 1.2f), Solid(Circle(3.8, 8, 2.2), Blue, BlueDeep), Solid(Circle(12.2, 3.8, 2.2), Blue, BlueDeep), Solid(Circle(12.2, 12.2, 2.2), Blue, BlueDeep)],
        [UiIcon.Combine] = [Solid(Box(1, 1, 8, 6.5, 1), Sky, Blue), Solid(Box(7, 8.5, 8, 6.5, 1), Rgb(160, 214, 160), Green), Line("M12.4 2.2v4M10.4 4.2h4", Green, 1.4f)],
        [UiIcon.Star] = [Solid(Star(), YellowFill, Gold, gloss: false, alpha: .5f)],
        [UiIcon.StarFilled] = [Solid(Star(), Yellow, Gold)],
        [UiIcon.Tag] = [Solid("M14.4 8.8L8.8 14.4Q8 15.2 7.2 14.4L1.6 8.8V1.8H8.6L14.4 7.6Q15.2 8.4 14.4 8.8Z", Orange, Brown), Solid(Circle(5, 5, 1.2), White, Brown, gloss: false)],
        [UiIcon.Search] = [Solid(Circle(6.4, 6.4, 4.4), Rgb(228, 242, 255), Steel), Line("M9.7 9.7L14 14", Steel, 2.3f)],
        [UiIcon.Import] = [Line("M8 2.6V10M4.8 7L8 10.2L11.2 7", Green, 1.8f), Line("M2.5 9.5V13a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V9.5", Steel, 1.4f)],
        [UiIcon.Info] = [Solid(Box(1.5, 1.5, 13, 13, 6.5), Blue, BlueDeep), Solid(Circle(8, 4.8, 1.05), White, null, gloss: false), Line("M8 7.3v4.6", White, 1.7f)],
        [UiIcon.Pdf] = [Solid("M3.5 1.5H10L13.5 5V14.5H3.5Z", Paper, Steel, gloss: false), Line("M10 1.6V5H13.4", Steel, 1f), Text("PDF", 2.6, 7.4, 11.4, 7.4, 5.4f, Red)],
    };
}

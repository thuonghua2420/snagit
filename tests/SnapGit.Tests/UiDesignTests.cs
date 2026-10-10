using System.Drawing.Drawing2D;
using SnapGit.App.Controls;
using SnapGit.App.Ui;
using SnapGit.Core.Settings;
using Xunit;

namespace SnapGit.Tests;

public class SvgPathTests
{
    private static RectangleF Bounds(string data)
    {
        using var path = SvgPath.Parse(data);
        return path.GetBounds();
    }

    [Fact]
    public void LinesRelativeCommandsAndImplicitRepeatsAreUnderstood()
    {
        Assert.Equal(new RectangleF(2, 3, 10, 5), Bounds("M2 3L12 3 12 8z"));        // implicit L after the first pair
        Assert.Equal(new RectangleF(2, 3, 10, 5), Bounds("M2 3h10v5z"));              // H / V relative
        Assert.Equal(new RectangleF(2, 3, 10, 5), Bounds("m2 3 10 0 0 5z"));          // relative M, implicit relative l
        Assert.Equal(new RectangleF(1, 1, 4, 4), Bounds("M1,1 L5,1 L5,5 L1,5Z"));     // commas
    }

    [Fact]
    public void NumbersMayTouchEachOther()
    {
        Assert.Equal(new RectangleF(0, 0, 1.5f, 1.5f), Bounds("M0 0L1.5.5L.5 1.5"));      // "1.5.5" = 1.5 and .5
        Assert.Equal(new RectangleF(-1, -2, 3, 4), Bounds("M-1-2L2-2L2 2z"));                // "-1-2" = -1 and -2
    }

    [Fact]
    public void ACircleMadeOfTwoArcsHasTheRightBounds()
    {
        var bounds = Bounds("M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0z");
        Assert.Equal(3, bounds.Left, 0.2);
        Assert.Equal(21, bounds.Right, 0.2);
        Assert.Equal(3, bounds.Top, 0.2);
        Assert.Equal(21, bounds.Bottom, 0.2);
    }

    [Fact]
    public void ArcFlagsMayBeWrittenWithoutSeparators()
    {
        // "a2 2 0 011-2 2": large-arc 0, sweep 1, then -2 2 — the flags are glued to the number that follows
        var glued = Bounds("M5 5h4a2 2 0 011 2");
        var spaced = Bounds("M5 5h4a2 2 0 0 1 1 2");
        Assert.Equal(spaced.Right, glued.Right, 3);
        Assert.Equal(spaced.Bottom, glued.Bottom, 3);
    }

    [Fact]
    public void ARoundedRectangleStaysInsideItsBoxAndIsFilledInside()
    {
        using var path = SvgPath.Parse("M6 3h12a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3z");
        var bounds = path.GetBounds();
        Assert.Equal(3, bounds.Left, 0.1);
        Assert.Equal(21, bounds.Right, 0.1);
        Assert.True(path.IsVisible(12, 12));
        Assert.False(path.IsVisible(3.2f, 3.2f), "the corner is rounded off");
    }

    [Fact]
    public void ArcSweepDirectionPicksTheRightHalf()
    {
        // from (0,0) to (10,0), radius 5. sweep=1 turns in the positive-angle direction, which on screen (y down) is clockwise:
        // from the left end that goes over the TOP; sweep=0 goes under.
        var over = Bounds("M0 0a5 5 0 0 1 10 0");
        var under = Bounds("M0 0a5 5 0 0 0 10 0");
        Assert.True(over.Top < -4.9 && over.Bottom < 0.1, $"sweep 1 arc {over}");
        Assert.True(under.Bottom > 4.9 && under.Top > -0.1, $"sweep 0 arc {under}");
    }

    [Fact]
    public void CubicQuadraticAndSmoothCurvesParse()
    {
        Assert.False(Bounds("M0 0C0 10 10 10 10 0S20-10 20 0").IsEmpty);
        Assert.False(Bounds("M0 0Q5 10 10 0T20 0").IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("L5 5")]       // no starting M
    [InlineData("M1 1X5")]     // unknown command
    public void BadDataIsRejectedLoudlyOrIsEmpty(string data)
    {
        if (data.Length == 0) { Assert.True(Bounds(data).IsEmpty); return; }
        Assert.Throws<FormatException>(() => SvgPath.Parse(data));
    }
}

public class IconAndThemeTests
{
    [Fact]
    public void EveryIconHasItsOwnArtworkAndNoneFallsBackToTheInfoGlyph()
    {
        var missing = Enum.GetValues<UiIcon>().Where(icon => !IconSet.IsDefined(icon)).ToList();
        Assert.True(missing.Count == 0, "Icons without artwork: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryIconStatementParses()
    {
        foreach (var icon in Enum.GetValues<UiIcon>().Where(i => IconSet.Derived(i) is null))
            foreach (var piece in IconSet.Of(icon))
            {
                if (piece.Path is null) { Assert.NotNull(piece.Text); continue; }
                using var path = SvgPath.Parse(piece.Path);
                var bounds = path.GetBounds();
                Assert.True(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 16.01 && bounds.Bottom <= 16.01, $"{icon}: '{piece.Path}' leaves the 16-px grid ({bounds})");
            }
    }

    [Fact]
    public void EveryIconDrawsAReadableAmountOfInkAtTheToolbarSize()
    {
        // at 16 px every icon must be a real picture (not a speck) and fit its box; coloured icons need coloured ink, not just grey
        foreach (var icon in Enum.GetValues<UiIcon>())
        {
            using var bmp = new Bitmap(Icons.Get(icon, null, 16));
            int ink = 0, minX = 16, maxX = -1, minY = 16, maxY = -1;
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    if (bmp.GetPixel(x, y).A > 80) { ink++; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            Assert.True(ink >= 12,$"{icon} is nearly empty at 16 px ({ink} pixels)");
            Assert.True(maxX - minX >= 7 || maxY - minY >= 7, $"{icon} is tiny at 16 px ({maxX - minX + 1}×{maxY - minY + 1})");
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void IconsRenderAtEveryDpiSizeAndDisabledOnesAreGrey(int size)
    {
        using var colour = new Bitmap(Icons.Get(UiIcon.Folder, null, size));
        Assert.Equal(new Size(size, size), colour.Size);
        var coloured = false;
        for (var y = 0; y < size && !coloured; y++)
            for (var x = 0; x < size && !coloured; x++)
            {
                var p = colour.GetPixel(x, y);
                if (p.A > 200 && Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 60) coloured = true;   // the yellow folder
            }
        Assert.True(coloured, "a colour icon has saturated pixels");

        using var grey = Icons.Disabled(colour);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var p = grey.GetPixel(x, y);
                Assert.True(Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) <= 6, "disabled icons carry no colour");
            }
    }

    [Fact]
    public void ARendererRedrawsIconsAtTheExactSlotSize()
    {
        var image = Icons.Get(UiIcon.Save);                   // the 32 px reference bitmap a button holds
        Assert.True(Icons.TryDescribe(image, out var icon, out var tint));
        Assert.Equal(UiIcon.Save, icon);
        Assert.Null(tint);
        Assert.False(Icons.TryDescribe(new Bitmap(4, 4), out _, out _));
    }

    [Fact]
    public void IconsAreRenderedInTheRequestedColourAndAccentIsAvailable()
    {
        using var red = new Bitmap(Icons.Get(UiIcon.Save, Color.Red, 96));
        var found = false;
        for (var y = 0; y < red.Height && !found; y++)
            for (var x = 0; x < red.Width && !found; x++)
            {
                var p = red.GetPixel(x, y);
                if (p.A > 200 && p.R > 200 && p.G < 60) found = true;
            }
        Assert.True(found, "the icon is drawn in the colour it was asked for");
    }

    private static double Luminance(Color c)
    {
        static double Channel(int v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThemesMeetWcagContrast(bool dark)
    {
        var p = dark ? Theme.Dark : Theme.Light;
        Assert.True(Contrast(p.Text, p.Surface) >= 7, "text on toolbars (AAA)");
        Assert.True(Contrast(p.Text, p.Window) >= 7, "text on the window (AAA)");
        Assert.True(Contrast(p.Muted, p.Surface) >= 4.5, "secondary text must still be readable (AA)");
        Assert.True(Contrast(p.Accent, p.Surface) >= 4.5, "accent-coloured text/icons on toolbars (AA)");
        Assert.True(Contrast(p.OnAccent, p.Accent) >= 4.5, "label of the accent button (AA)");
        Assert.True(Contrast(dark ? Color.FromArgb(232, 232, 236) : Color.FromArgb(50, 50, 55), p.Surface) >= 7, "icons on toolbars");
    }
}

[Collection("UI")]
public class VideoViewSharpnessTests
{
    private static byte[] Pixels(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { bmp.UnlockBits(data); }
    }

    private static double Edge(Bitmap bmp)
    {
        var p = Pixels(bmp);
        var stride = bmp.Width * 4;
        double sum = 0;
        long n = 0;
        for (var y = 0; y < bmp.Height; y += 2)
            for (var x = 1; x < bmp.Width; x++) { var i = y * stride + x * 4; sum += Math.Abs(p[i + 1] - p[i - 3]); n++; }
        return sum / n;
    }

    [Fact]
    public void AFullSizeFrameShownSmallerKeepsItsSharpness()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Theme.Configure(AppTheme.Light);
                // a page-like picture full of small text and 1-px rules
                using var source = new Bitmap(1600, 900);
                using (var g = Graphics.FromImage(source))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    using var font = new Font("Segoe UI", 9f);
                    for (var line = 0; line < 52; line++) g.DrawString($"Row {line:00} customer_id first_name last_name 0123456789 example.com", font, Brushes.Black, 12, 6 + line * 17);
                    using var pen = new Pen(Color.Gray, 1);
                    for (var x = 0; x < 1600; x += 160) g.DrawLine(pen, x, 0, x, 900);
                }

                using var view = new VideoView { Size = new Size(1469, 837) };
                var plan = SnapGit.Core.Recording.VideoEditPlan.For(TimeSpan.FromSeconds(1), source.Size);
                view.Plan = plan;
                view.SetFrame(new Bitmap(source), TimeSpan.Zero);

                using var shot = new Bitmap(view.Width, view.Height);
                view.DrawToBitmap(shot, new Rectangle(Point.Empty, view.Size));
                var inner = new Rectangle(12, 12, view.Width - 24, view.Height - 24);       // the picture area (12 px margin)
                var width = inner.Width;
                var height = (int)Math.Round(source.Height * (double)width / source.Width);
                using var shown = shot.Clone(new Rectangle(inner.X, inner.Y + (inner.Height - height) / 2, width, height), shot.PixelFormat);

                using var reference = new Bitmap(width, height);
                using (var g = Graphics.FromImage(reference))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                }

                var ratio = Edge(shown) / Edge(reference);
                Assert.InRange(ratio, 0.9, 1.15);   // the old 1280-px decode stretched up measured 0.61
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

[Collection("UI")]
public class ToolPaletteTests
{
    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("UI thread hung");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static ToolPalette Build()
    {
        var palette = new ToolPalette { Height = 600 };
        palette.AddGroup([new PaletteTool("a", UiIcon.Select, "Select", "V"), new PaletteTool("b", UiIcon.Arrow, "Arrow", "A")]);
        palette.AddGroup([new PaletteTool("c", UiIcon.Line, "Line", "L"), new PaletteTool("d", UiIcon.Rectangle, "Rectangle", "R"), new PaletteTool("e", UiIcon.Ellipse, "Ellipse", "E")]);
        return palette;
    }

    [Fact]
    public void ToolsAreChosenByClickAndTheSelectionIsDrawnInTheAccentColour()
    {
        RunSta(() =>
        {
            Theme.Configure(AppTheme.Light);
            using var form = new Form { ClientSize = new Size(300, 600) };
            var palette = Build();
            form.Controls.Add(palette);
            form.Show();
            Application.DoEvents();

            var chosen = new List<string>();
            palette.ToolChosen += chosen.Add;
            palette.Selected = "b";

            // click the first button of the second group (after a group gap)
            var method = typeof(ToolPalette).GetMethod("ButtonRect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var rect = (Rectangle)method.Invoke(palette, [2])!;
            var down = typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            down.Invoke(palette, [new MouseEventArgs(MouseButtons.Left, 1, rect.X + 5, rect.Y + 5, 0)]);
            Assert.Equal(["c"], chosen);

            // the selected button (b) carries the accent tint
            using var shot = new Bitmap(palette.Width, palette.Height);
            palette.DrawToBitmap(shot, new Rectangle(Point.Empty, palette.Size));
            var selectedRect = (Rectangle)method.Invoke(palette, [1])!;
            var tinted = shot.GetPixel(selectedRect.Left + 3, selectedRect.Top + 3);
            var plain = shot.GetPixel(selectedRect.Left + 3, selectedRect.Bottom + 12);   // the gap below: plain surface
            Assert.NotEqual(plain, tinted);
            Assert.True(tinted.B > tinted.R, $"accent tint is bluish in the light theme: {tinted}");
        });
    }

    [Fact]
    public void ButtonsDoNotOverlapAndStayInsideAndGroupsAreSeparated()
    {
        RunSta(() =>
        {
            Theme.Configure(AppTheme.Light);
            var palette = Build();
            var method = typeof(ToolPalette).GetMethod("ButtonRect", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var rects = Enumerable.Range(0, palette.Tools.Count).Select(i => (Rectangle)method.Invoke(palette, [i])!).ToList();

            for (var i = 0; i < rects.Count; i++)
            {
                Assert.True(rects[i].Left >= 0 && rects[i].Right <= palette.Width, $"button {i} inside the palette width");
                for (var j = i + 1; j < rects.Count; j++) Assert.False(rects[i].IntersectsWith(rects[j]), $"buttons {i} and {j} overlap");
            }
            Assert.Equal(1, palette.Columns);                               // one slim column
            Assert.True(rects[1].Top >= rects[0].Bottom, "buttons stack");
            Assert.True(rects[2].Top - rects[1].Bottom >= 6, "a group starts a visibly new block");
            Assert.True(rects[4].Top >= rects[3].Bottom && rects[3].Top >= rects[2].Bottom);
            Assert.InRange(palette.Width, 24, 30);                          // ≈ 26 px like the reference tool strip
        });
    }

    [Fact]
    public void AShortWindowMakesThePaletteUseTwoColumnsInsteadOfClipping()
    {
        RunSta(() =>
        {
            Theme.Configure(AppTheme.Light);
            var palette = Build();
            Assert.Equal(1, palette.Columns);
            var oneColumnWidth = palette.Width;
            palette.Height = 60;                                            // far too short for five buttons and a gap
            Assert.Equal(2, palette.Columns);
            Assert.True(palette.Width > oneColumnWidth);
            palette.Height = 600;
            Assert.Equal(1, palette.Columns);
            Assert.Equal(oneColumnWidth, palette.Width);
        });
    }

    [Fact]
    public void KeyboardMovesBetweenToolsAndEnterChoosesAndEveryToolIsAnAccessibleRadioButton()
    {
        RunSta(() =>
        {
            Theme.Configure(AppTheme.Light);
            using var form = new Form { ClientSize = new Size(300, 600) };
            var palette = Build();
            form.Controls.Add(palette);
            form.Show();
            palette.Focus();

            var chosen = new List<string>();
            palette.ToolChosen += chosen.Add;
            var keyDown = typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            void Press(Keys key) => keyDown.Invoke(palette, [new KeyEventArgs(key)]);

            palette.Selected = "a";
            Press(Keys.Right); Press(Keys.Down); Press(Keys.Enter);   // a → b → c (one column: down = next) → choose
            Assert.Equal(["c"], chosen);
            Press(Keys.Home); Press(Keys.Space);
            Assert.Equal(["c", "a"], chosen);

            var accessible = palette.AccessibilityObject;
            Assert.Equal(5, accessible.GetChildCount());
            for (var i = 0; i < 5; i++)
            {
                var child = accessible.GetChild(i)!;
                Assert.False(string.IsNullOrEmpty(child.Name));
                Assert.Equal(AccessibleRole.RadioButton, child.Role);
                Assert.False(string.IsNullOrEmpty(child.KeyboardShortcut));
            }
            palette.Selected = "c";
            Assert.True(accessible.GetChild(2)!.State.HasFlag(AccessibleStates.Checked));
            Assert.False(accessible.GetChild(0)!.State.HasFlag(AccessibleStates.Checked));
        });
    }

    [Fact]
    public void ThePrimaryButtonIsPaintedInTheAccentColourWithReadableText()
    {
        RunSta(() =>
        {
            foreach (var dark in new[] { false, true })
            {
                Theme.Configure(dark ? AppTheme.Dark : AppTheme.Light);
                var strip = Toolbars.NewBar(DockStyle.Top);
                var primary = Toolbars.Primary(UiIcon.Export, "Export", "Export", () => { });
                strip.Items.Add(primary);
                using var form = new Form { ClientSize = new Size(300, 80) };
                form.Controls.Add(strip);
                Theme.Apply(form);
                form.Show();
                Application.DoEvents();

                using var shot = new Bitmap(strip.Width, strip.Height);
                strip.DrawToBitmap(shot, new Rectangle(Point.Empty, strip.Size));
                var inside = shot.GetPixel(primary.Bounds.Left + 4, primary.Bounds.Top + primary.Bounds.Height / 2);
                var accent = Theme.Colors.Accent;
                Assert.True(Math.Abs(inside.R - accent.R) < 12 && Math.Abs(inside.G - accent.G) < 12 && Math.Abs(inside.B - accent.B) < 12,
                    $"primary button background should be the accent colour ({(dark ? "dark" : "light")}): got {inside}, wanted {accent}");
            }
            Theme.Configure(AppTheme.Light);
        });
    }
}

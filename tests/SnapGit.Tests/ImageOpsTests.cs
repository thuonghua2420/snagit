using SnapGit.Core.Imaging;
using Xunit;

namespace SnapGit.Tests;

public class ImageOpsTests
{
    private static Bitmap Solid(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    [Fact]
    public void RotateSwapsDimensions()
    {
        using var src = Solid(30, 10, Color.Red);
        using var rotated = ImageOps.RotateFlip(src, RotateFlipType.Rotate90FlipNone);
        Assert.Equal(new Size(10, 30), rotated.Size);
        Assert.Equal(new Size(30, 10), src.Size); // input untouched
    }

    [Fact]
    public void ResizeCanvasPlacesImageAtOffset()
    {
        using var src = Solid(10, 10, Color.Red);
        using var result = ImageOps.ResizeCanvas(src, 30, 20, new Point(5, 3), Color.White);
        Assert.Equal(new Size(30, 20), result.Size);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(6, 4).ToArgb());
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void BorderGrowsEachSide()
    {
        using var src = Solid(10, 10, Color.White);
        using var result = ImageOps.AddBorder(src, 4, Color.Black);
        Assert.Equal(new Size(18, 18), result.Size);
        Assert.Equal(Color.Black.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(9, 9).ToArgb());
    }

    [Fact]
    public void ShadowGrowsCanvasAndKeepsImageIntact()
    {
        using var src = Solid(20, 20, Color.Blue);
        using var result = ImageOps.AddShadow(src, 4, 6, Color.FromArgb(160, 0, 0, 0), Color.White);
        Assert.True(result.Width > src.Width && result.Height > src.Height);
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.Contains(Enumerable.Range(0, result.Width).Select(x => result.GetPixel(x, result.Height / 2)), c => c.ToArgb() == Color.Blue.ToArgb());
    }

    [Fact]
    public void TornEdgeMakesCornersTransparentOnlyOnChosenSides()
    {
        using var src = Solid(120, 80, Color.Red);
        using var result = ImageOps.TornEdge(src, 10, 6, EdgeSides.Bottom);
        Assert.Equal(src.Size, result.Size);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(60, 5).ToArgb());                       // untouched top
        Assert.Contains(Enumerable.Range(0, 120).Select(x => result.GetPixel(x, 79)), c => c.A < 255); // ragged bottom
    }

    [Fact]
    public void CutOutVerticalStripJoinsHalves()
    {
        using var src = new Bitmap(10, 4);
        for (var x = 0; x < 10; x++)
            for (var y = 0; y < 4; y++)
                src.SetPixel(x, y, Color.FromArgb(x * 20, 0, 0));

        using var result = ImageOps.CutOut(src, 3, 6, horizontalStrip: false);
        Assert.Equal(new Size(7, 4), result.Size);
        Assert.Equal(Color.FromArgb(40, 0, 0).ToArgb(), result.GetPixel(2, 0).ToArgb());   // column 2 kept
        Assert.Equal(Color.FromArgb(120, 0, 0).ToArgb(), result.GetPixel(3, 0).ToArgb());  // old column 6 follows
    }

    [Fact]
    public void CutOutHorizontalStripAndReversedRange()
    {
        using var src = Solid(10, 20, Color.Green);
        using var result = ImageOps.CutOut(src, 12, 5, horizontalStrip: true); // reversed on purpose
        Assert.Equal(new Size(10, 13), result.Size);
    }

    [Fact]
    public void CutOutEverythingIsRejected()
    {
        using var src = Solid(10, 10, Color.Green);
        Assert.Throws<ArgumentException>(() => ImageOps.CutOut(src, 0, 10, true));
    }

    [Fact]
    public void ReplaceColorHonoursTolerance()
    {
        using var src = Solid(4, 1, Color.FromArgb(200, 10, 10));
        src.SetPixel(3, 0, Color.FromArgb(10, 200, 10));

        using var result = ImageOps.ReplaceColor(src, Color.FromArgb(205, 12, 8), Color.Blue, tolerance: 10);
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.FromArgb(10, 200, 10).ToArgb(), result.GetPixel(3, 0).ToArgb());
    }

    [Fact]
    public void FloodFillStopsAtBarrier()
    {
        using var src = Solid(10, 10, Color.White);
        for (var y = 0; y < 10; y++) src.SetPixel(5, y, Color.Black); // vertical wall

        using var result = ImageOps.FloodFill(src, new Point(1, 1), Color.Red, 0);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(0, 9).ToArgb());
        Assert.Equal(Color.Black.ToArgb(), result.GetPixel(5, 4).ToArgb());
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(8, 8).ToArgb()); // other side untouched
    }

    [Fact]
    public void GrayscaleInvertAndContrast()
    {
        using var src = Solid(2, 2, Color.FromArgb(200, 100, 50));

        using var gray = ImageOps.Grayscale(src);
        var g = gray.GetPixel(0, 0);
        Assert.True(g.R == g.G && g.G == g.B);

        using var inverted = ImageOps.Invert(src);
        Assert.Equal(Color.FromArgb(55, 155, 205).ToArgb(), inverted.GetPixel(1, 1).ToArgb());

        using var brighter = ImageOps.BrightnessContrast(src, 20, 0);
        Assert.True(brighter.GetPixel(0, 0).R > 200);
    }

    [Fact]
    public void SimplifyReducesDistinctColours()
    {
        var rng = new Random(1);
        using var noisy = new Bitmap(40, 40);
        for (var x = 0; x < 40; x++)
            for (var y = 0; y < 40; y++)
                noisy.SetPixel(x, y, Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)));

        using var simple = ImageOps.Simplify(noisy, 3);
        var colours = Enumerable.Range(0, 40).SelectMany(x => Enumerable.Range(0, 40).Select(y => simple.GetPixel(x, y).ToArgb())).Distinct().Count();
        Assert.True(colours <= 27, $"expected <= 27 colours, got {colours}");
    }

    [Fact]
    public void DetectObjectBoundsFindsTheBox()
    {
        using var src = Solid(100, 80, Color.White);
        using (var g = Graphics.FromImage(src))
            g.FillRectangle(Brushes.Navy, 30, 20, 25, 15);

        var bounds = ImageOps.DetectObjectBounds(src, new Point(40, 25));
        Assert.Equal(new Rectangle(30, 20, 25, 15), bounds);
        Assert.Equal(Rectangle.Empty, ImageOps.DetectObjectBounds(src, new Point(5, 5))); // clicked background
    }

    [Fact]
    public void SurroundingColourIsBackground()
    {
        using var src = Solid(50, 50, Color.Beige);
        using (var g = Graphics.FromImage(src)) g.FillRectangle(Brushes.Black, 20, 20, 10, 10);
        Assert.Equal(Color.Beige.ToArgb(), ImageOps.SampleSurroundingColor(src, new Rectangle(20, 20, 10, 10)).ToArgb());
    }

    [Fact]
    public void ScaleToFitNeverEnlarges()
    {
        using var src = Solid(200, 100, Color.Red);
        using var smaller = ImageOps.ScaleToFit(src, 100, 100);
        Assert.Equal(new Size(100, 50), smaller.Size);
        using var same = ImageOps.ScaleToFit(src, 1000, 1000);
        Assert.Equal(src.Size, same.Size);
    }
}

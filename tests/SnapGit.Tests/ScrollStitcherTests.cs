using SnapGit.Core.Imaging;
using Xunit;

namespace SnapGit.Tests;

public class ScrollStitcherTests
{
    /// <summary>A "page" of unique content: every row has its own pseudo-random pattern.</summary>
    private static Bitmap MakePage(int width, int height, int seed = 3)
    {
        var rng = new Random(seed);
        var page = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            var baseColor = Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256));
            for (var x = 0; x < width; x++)
                page.SetPixel(x, y, x % 7 == 0 ? Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256)) : baseColor);
        }
        return page;
    }

    private static Bitmap Window(Bitmap page, int top, int height, int headerRows = 0, int footerRows = 0, Bitmap? headerSource = null)
    {
        var frame = new Bitmap(page.Width, height);
        using var g = Graphics.FromImage(frame);
        g.DrawImage(page, new Rectangle(0, 0, page.Width, height), new Rectangle(0, top, page.Width, height), GraphicsUnit.Pixel);
        if (headerRows > 0 && headerSource is not null)
        {
            g.DrawImage(headerSource, new Rectangle(0, 0, page.Width, headerRows), new Rectangle(0, 0, page.Width, headerRows), GraphicsUnit.Pixel);
            g.DrawImage(headerSource, new Rectangle(0, height - footerRows, page.Width, footerRows), new Rectangle(0, 0, page.Width, footerRows), GraphicsUnit.Pixel);
        }
        return frame;
    }

    private static bool SameRows(Bitmap a, Bitmap b, int ay, int by, int rows)
    {
        for (var r = 0; r < rows; r++)
            for (var x = 0; x < a.Width; x++)
                if (a.GetPixel(x, ay + r).ToArgb() != b.GetPixel(x, by + r).ToArgb()) return false;
        return true;
    }

    [Fact]
    public void ReconstructsAPageFromOverlappingFrames()
    {
        using var page = MakePage(60, 900);
        using var stitcher = new ScrollStitcher();

        foreach (var top in new[] { 0, 120, 240, 360, 480, 500 }) // last step scrolls less (end of page)
        {
            using var frame = Window(page, top, 400);
            var outcome = stitcher.TryAdd(frame);
            Assert.Equal(StitchOutcome.Appended, outcome);
        }

        using var result = stitcher.Build();
        Assert.Equal(900, result.Height);
        Assert.True(SameRows(result, page, 0, 0, 900));
    }

    [Fact]
    public void IdenticalFrameMeansTheEndWasReached()
    {
        using var page = MakePage(40, 600);
        using var stitcher = new ScrollStitcher();
        using var f1 = Window(page, 0, 300);
        using var f2 = Window(page, 100, 300);
        using var f3 = Window(page, 100, 300); // did not scroll

        Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(f1));
        Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(f2));
        Assert.Equal(StitchOutcome.NoChange, stitcher.TryAdd(f3));
        Assert.Equal(2, stitcher.FrameCount);
    }

    [Fact]
    public void UnrelatedFrameIsRejectedNotGlued()
    {
        using var pageA = MakePage(40, 600, seed: 1);
        using var pageB = MakePage(40, 600, seed: 2);
        using var stitcher = new ScrollStitcher();
        using var f1 = Window(pageA, 0, 300);
        using var f2 = Window(pageB, 0, 300);

        stitcher.TryAdd(f1);
        Assert.Equal(StitchOutcome.NoMatch, stitcher.TryAdd(f2));
        using var result = stitcher.Build();
        Assert.Equal(300, result.Height);
    }

    [Fact]
    public void ScrollbarColumnOnTheRightDoesNotBreakAlignment()
    {
        using var page = MakePage(80, 700);
        using var stitcher = new ScrollStitcher { IgnoreTrailingPixels = 16 };

        var thumbTop = 0;
        foreach (var top in new[] { 0, 150, 300 })
        {
            using var frame = Window(page, top, 400);
            using (var g = Graphics.FromImage(frame)) g.FillRectangle(Brushes.Gray, 70, thumbTop, 10, 60); // moving "scrollbar thumb"
            thumbTop += 90;
            Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(frame));
        }

        using var result = stitcher.Build();
        Assert.Equal(700, result.Height);
    }

    [Fact]
    public void ABlinkingCaretInTheOverlapDoesNotBreakAlignment()
    {
        // Regression (found by the live e2e run): a text caret appeared in one frame, 19 rows tall, in the overlap.
        using var page = MakePage(80, 900);
        using var stitcher = new ScrollStitcher();

        var frames = new[] { 0, 150, 300, 450 };
        for (var i = 0; i < frames.Length; i++)
        {
            using var frame = Window(page, frames[i], 400);
            if (i > 0)
                using (var g = Graphics.FromImage(frame)) g.FillRectangle(Brushes.Black, 40, 100, 2, 24); // caret: one text line (24 rows) tall
            Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(frame));
        }

        using var result = stitcher.Build();
        Assert.Equal(850, result.Height);
        Assert.True(SameRows(result, page, 0, 0, 90)); // the first, caret-free frame is intact
    }

    [Fact]
    public void AnEdgeColumnThatChangesInEveryFrameDoesNotBreakAlignment()
    {
        // Regression (found by the live e2e run): the window's left border column changed shade between frames
        // because the window's activation state flipped. Every single row differed in column 1.
        using var page = MakePage(200, 900);
        using var stitcher = new ScrollStitcher();

        var shades = new[] { Color.FromArgb(250, 250, 250), Color.FromArgb(237, 237, 237), Color.FromArgb(120, 120, 120), Color.FromArgb(250, 250, 250) };
        for (var i = 0; i < 4; i++)
        {
            using var frame = Window(page, i * 150, 400);
            using (var g = Graphics.FromImage(frame)) { using var pen = new Pen(shades[i]); g.DrawLine(pen, 1, 0, 1, 399); }
            Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(frame));
        }

        using var result = stitcher.Build();
        Assert.Equal(850, result.Height);
        // away from the changing column the stitched content equals the page
        for (var y = 0; y < 850; y += 13)
            for (var x = 20; x < 150; x += 17)
                Assert.Equal(page.GetPixel(x, y).ToArgb(), result.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void SparseTextWithRepeatingPrefixesIsStitchedWithoutSkippingOrRepeatingLines()
    {
        // Regression (found by the live e2e run): numbered text lines occupy only the left half and share the
        // prefix "Line 2", so wrong offsets used to look "good enough" and whole lines went missing (286 -> 290).
        const int lineHeight = 24, lines = 120, width = 600, viewHeight = 500;
        using var page = new Bitmap(width, lines * lineHeight);
        using (var g = Graphics.FromImage(page))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Consolas", 16, FontStyle.Regular, GraphicsUnit.Pixel);
            for (var i = 1; i <= lines; i++)
                g.DrawString($"Line {i:D3}  unique-token-{(i * 7919) % 10007}", font, Brushes.Black, 4, (i - 1) * lineHeight);
        }

        using var stitcher = new ScrollStitcher { IgnoreTrailingPixels = 20 };
        var steps = new[] { 144, 288, 264, 216, 288, 288, 144, 264, 288, 216, 288, 264, 288, 288, 216, 288 }; // uneven, like a real wheel
        var top = 0;
        var frameNumber = 0;
        foreach (var step in new[] { 0 }.Concat(steps))
        {
            top = Math.Min(top + step, page.Height - viewHeight);
            using var frame = page.Clone(new Rectangle(0, top, width, viewHeight), page.PixelFormat);
            using (var g = Graphics.FromImage(frame))
            {
                using var border = new Pen(frameNumber++ % 2 == 0 ? Color.FromArgb(250, 250, 250) : Color.FromArgb(237, 237, 237));
                g.DrawLine(border, 1, 0, 1, viewHeight - 1); // window border that changes with activation
            }
            var outcome = stitcher.TryAdd(frame);
            Assert.True(outcome is StitchOutcome.Appended or StitchOutcome.NoChange, $"frame at {top}: {outcome}");
        }

        using var result = stitcher.Build();
        Assert.Equal(top + viewHeight, result.Height); // nothing skipped, nothing repeated
        for (var y = 0; y < result.Height; y += 5)
            for (var x = 5; x < 330; x += 3)
                Assert.Equal(page.GetPixel(x, y).ToArgb(), result.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void ANearlyIdenticalFrameStillCountsAsTheEndOfThePage()
    {
        using var page = MakePage(60, 600);
        using var stitcher = new ScrollStitcher();
        using var f1 = Window(page, 0, 300);
        using var f2 = Window(page, 100, 300);
        using var f3 = Window(page, 100, 300);
        using (var g = Graphics.FromImage(f3)) g.FillRectangle(Brushes.Black, 20, 50, 2, 30); // only the caret differs

        stitcher.TryAdd(f1);
        stitcher.TryAdd(f2);
        Assert.Equal(StitchOutcome.NoChange, stitcher.TryAdd(f3));
    }

    [Fact]
    public void StickyHeaderAndFooterAppearOnlyOnce()
    {
        using var page = MakePage(60, 1000);
        using var chrome = MakePage(60, 40, seed: 99); // the fixed bars
        using var stitcher = new ScrollStitcher();

        foreach (var top in new[] { 0, 150, 300, 450 })
        {
            using var frame = Window(page, top, 400, headerRows: 40, footerRows: 40, headerSource: chrome);
            Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(frame));
        }

        using var result = stitcher.Build();
        // header(40) + middle content + footer(40): middle = 320 rows of first frame + 3 * 150 new rows
        Assert.Equal(40 + 320 + 3 * 150 + 40, result.Height);
        Assert.True(SameRows(result, chrome, 0, 0, 40));                               // header once, on top
        Assert.True(SameRows(result, chrome, result.Height - 40, 0, 40));              // footer once, at the bottom
        Assert.True(SameRows(result, page, 40, 40, 320 + 450));                        // content continuous (page rows 40..810)
    }

    [Fact]
    public void HorizontalScrollingStitchesColumns()
    {
        using var wide = MakePage(900, 50);
        using var stitcher = new ScrollStitcher(ScrollDirection.Horizontal);

        foreach (var left in new[] { 0, 120, 240, 360 })
        {
            using var frame = new Bitmap(400, 50);
            using (var g = Graphics.FromImage(frame))
                g.DrawImage(wide, new Rectangle(0, 0, 400, 50), new Rectangle(left, 0, 400, 50), GraphicsUnit.Pixel);
            Assert.Equal(StitchOutcome.Appended, stitcher.TryAdd(frame));
        }

        using var result = stitcher.Build();
        Assert.Equal(760, result.Width);
        Assert.Equal(50, result.Height);
        for (var x = 0; x < 760; x += 37)
            for (var y = 0; y < 50; y += 7)
                Assert.Equal(wide.GetPixel(x, y).ToArgb(), result.GetPixel(x, y).ToArgb());
    }

    [Fact]
    public void BuildWithoutFramesThrows()
    {
        using var stitcher = new ScrollStitcher();
        Assert.Throws<InvalidOperationException>(() => stitcher.Build());
    }
}

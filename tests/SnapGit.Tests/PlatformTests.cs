using SnapGit.Core.Models;
using SnapGit.Platform.Capture;
using SnapGit.Platform.Ocr;
using Xunit;

namespace SnapGit.Tests;

/// <summary>
/// Tests that touch Windows itself (OCR engine, screen). They need an interactive desktop session; each one
/// returns early (and says why) when the machine can't provide what it needs, instead of failing.
/// </summary>
[Collection("Heavy")]
public class PlatformTests
{
    private static Bitmap TextImage(string text, float size = 48)
    {
        var bmp = new Bitmap(900, 140);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        using var font = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString(text, font, Brushes.Black, 20, 30);
        return bmp;
    }

    [Fact]
    public async Task OcrReadsRenderedEnglishText()
    {
        var ocr = new WindowsOcrService();
        var english = ocr.AvailableLanguages.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (english is null) return; // no English OCR pack installed here

        using var image = TextImage("Hello SnapGit 2026");
        var result = await ocr.RecognizeAsync(image, english.Tag);

        Assert.Contains("Hello", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026", result.Text);
        var line = Assert.Single(result.Lines);
        Assert.True(line.Bounds.Width > 200 && line.Bounds.Right <= image.Width, $"line bounds {line.Bounds}");
    }

    [Fact]
    public void OcrLayoutMergesPiecesOfOneRowButKeepsRealColumnsApart()
    {
        OcrLine L(string text, float x, float y, float w = 40) => new(text, new RectangleF(x, y, w, 20));

        // Row 1 split in 3 pieces, row 2 split in 2; plus a far-away column on row 1.
        var merged = OcrLayout.MergeRows(
        [
            L("001", 60, 0), L("Line", 0, 1), L("token-7", 110, 0, 80),
            L("Line", 0, 30), L("002", 60, 30),
            L("SIDEBAR", 600, 0, 80),
        ]);

        Assert.Equal(["Line 001 token-7", "SIDEBAR", "Line 002"], merged.Select(m => m.Text));
        Assert.Equal(0f, merged[0].Bounds.Left);
        Assert.True(merged[0].Bounds.Right >= 190);
    }

    [Fact]
    public void OcrLayoutResultDoesNotDependOnInputOrder()
    {
        OcrLine L(string text, float x, float y, float w = 40) => new(text, new RectangleF(x, y, w, 20));
        // "001" bridges "Line" and "token": they are only connected through it.
        var pieces = new List<OcrLine> { L("Line", 0, 0), L("001", 60, 0), L("token", 110, 0), L("Line", 0, 30), L("002", 60, 30), L("token", 110, 30) };

        var expected = OcrLayout.MergeRows(pieces).Select(r => r.Text).ToList();
        Assert.Equal(["Line 001 token", "Line 002 token"], expected);

        var random = new Random(5);
        for (var i = 0; i < 25; i++)
        {
            var shuffled = pieces.OrderBy(_ => random.Next()).ToList();
            Assert.Equal(expected, OcrLayout.MergeRows(shuffled).Select(r => r.Text));
        }
    }

    [Fact]
    public async Task OcrReadsAWholeRowOfSpacedWordsAsOneLine()
    {
        var ocr = new WindowsOcrService();
        var english = ocr.AvailableLanguages.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (english is null) return;

        using var image = TextImage("Line 001      unique-token-7919", 40);
        var result = await ocr.RecognizeAsync(image, english.Tag);

        var line = Assert.Single(result.Lines);
        Assert.Contains("Line", line.Text);
        Assert.Contains("7919", line.Text);
    }

    [Fact]
    public async Task OcrReturnsEmptyTextForABlankImage()
    {
        var ocr = new WindowsOcrService();
        if (ocr.AvailableLanguages.Count == 0) return;

        using var blank = new Bitmap(300, 100);
        using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
        var result = await ocr.RecognizeAsync(blank);
        Assert.Equal(string.Empty, result.Text.Trim());
    }

    [Fact]
    public async Task OcrRejectsAnUnknownLanguageClearly()
    {
        var ocr = new WindowsOcrService();
        using var image = new Bitmap(50, 50);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ocr.RecognizeAsync(image, "xx-NOPE"));
    }

    [Fact]
    public void GrabberCapturesTheRequestedSizeAndIsOpaque()
    {
        var grabber = new ScreenGrabber();
        Bitmap bmp;
        try { bmp = grabber.Grab(new Rectangle(grabber.VirtualScreen.X, grabber.VirtualScreen.Y, 120, 80), includeCursor: true); }
        catch (System.ComponentModel.Win32Exception) { return; } // locked session / no desktop

        using (bmp)
        {
            Assert.Equal(new Size(120, 80), bmp.Size);
            Assert.Equal(255, bmp.GetPixel(60, 40).A);
        }
    }

    [Fact]
    public void GrabberEnumeratesWindowsWithSaneBounds()
    {
        var grabber = new ScreenGrabber();
        var windows = grabber.GetWindows();
        foreach (var window in windows)
        {
            Assert.False(string.IsNullOrEmpty(window.Title));
            Assert.True(window.Bounds.Width >= 20 && window.Bounds.Height >= 20);
        }

        if (windows.Count > 0)
        {
            var chain = grabber.GetWindowChain(windows[0], new Point(windows[0].Bounds.X + 5, windows[0].Bounds.Y + 5));
            Assert.Same(windows[0], chain[0]); // outermost first
        }
    }
}

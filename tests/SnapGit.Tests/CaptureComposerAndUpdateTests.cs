using SnapGit.Core.Imaging;
using SnapGit.Core.Updates;
using Xunit;

namespace SnapGit.Tests;

public class CaptureComposerTests
{
    private static Bitmap Snapshot()
    {
        var bmp = new Bitmap(200, 100);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.FillRectangle(Brushes.Red, 0, 0, 100, 100);
        g.FillRectangle(Brushes.Blue, 100, 0, 100, 100);
        return bmp;
    }

    [Fact]
    public void FreeHandKeepsInsideAndClearsOutside()
    {
        using var snapshot = Snapshot();
        // triangle: (20,10) (180,10) (20,90) — lower-right of its bounding box lies outside
        using var result = CaptureComposer.FreeHand(snapshot, [new Point(20, 10), new Point(180, 10), new Point(20, 90)], out var bounds);

        Assert.Equal(new Rectangle(20, 10, 160, 80), bounds);
        Assert.Equal(bounds.Size, result.Size);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(10, 5).ToArgb());      // inside, red half
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(120, 5).ToArgb());    // inside, blue half
        Assert.Equal(0, result.GetPixel(150, 70).A);                            // outside the triangle: transparent
    }

    [Fact]
    public void FreeHandRejectsDegenerateInput()
    {
        using var snapshot = Snapshot();
        Assert.Throws<ArgumentException>(() => CaptureComposer.FreeHand(snapshot, [new Point(1, 1), new Point(5, 5)], out _));
        Assert.Throws<ArgumentException>(() => CaptureComposer.FreeHand(snapshot, [new Point(500, 500), new Point(600, 500), new Point(600, 600)], out _));
    }

    [Fact]
    public void MultiAreaKeepsRelativePositionsAndLeavesGapsTransparent()
    {
        using var snapshot = Snapshot();
        using var result = CaptureComposer.MultiArea(snapshot, [new Rectangle(10, 10, 30, 20), new Rectangle(150, 60, 30, 20)], out var bounds);

        Assert.Equal(new Rectangle(10, 10, 170, 70), bounds);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(5, 5).ToArgb());          // first region (red side)
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(150, 55).ToArgb());      // second region (blue side)
        Assert.Equal(0, result.GetPixel(80, 35).A);                                // gap between them
    }

    [Fact]
    public void MultiAreaClipsToTheScreenAndRejectsEmptyInput()
    {
        using var snapshot = Snapshot();
        using var clipped = CaptureComposer.MultiArea(snapshot, [new Rectangle(180, 80, 100, 100)], out var bounds);
        Assert.Equal(new Rectangle(180, 80, 20, 20), bounds);
        Assert.Throws<ArgumentException>(() => CaptureComposer.MultiArea(snapshot, [new Rectangle(500, 500, 10, 10)], out _));
    }
}

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("{\"version\":\"1.2.0\",\"url\":\"https://x/y\",\"notes\":\"New!\"}", "1.2.0", "https://x/y", "New!")]
    [InlineData("{\"version\":\"v2.0\"}", "2.0", "", "")]
    public void ParsesTheFeed(string json, string version, string url, string notes)
    {
        var info = UpdateChecker.Parse(json);
        Assert.NotNull(info);
        Assert.Equal(Version.Parse(version), info.Version);
        Assert.Equal(url, info.Url);
        Assert.Equal(notes, info.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":\"banana\"}")]
    [InlineData("{\"nothing\":1}")]
    public void BadFeedsAreIgnored(string json) => Assert.Null(UpdateChecker.Parse(json));

    [Fact]
    public void OnlyNewerVersionsAreReported()
    {
        var current = new Version(1, 0, 0);
        Assert.NotNull(UpdateChecker.NewerThan(current, "{\"version\":\"1.0.1\"}"));
        Assert.Null(UpdateChecker.NewerThan(current, "{\"version\":\"1.0.0\"}"));
        Assert.Null(UpdateChecker.NewerThan(current, "{\"version\":\"0.9\"}"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com/feed.json")]
    [InlineData("not a url")]
    public async Task DisabledOrInvalidFeedUrlsDoNothing(string url) =>
        Assert.Null(await UpdateChecker.CheckAsync(url, new Version(1, 0)));
}

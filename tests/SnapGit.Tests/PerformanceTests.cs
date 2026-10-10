using System.Diagnostics;
using SnapGit.Platform.Capture;
using Xunit;
using Xunit.Abstractions;

namespace SnapGit.Tests;

/// <summary>Loose guard-rails (not benchmarks): the hotkey-to-overlay path must stay snappy even on multi-monitor 4K setups.</summary>
[Collection("Heavy")]
public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void GrabbingTheWholeVirtualScreenIsFast()
    {
        var grabber = new ScreenGrabber();
        var area = grabber.VirtualScreen;
        Bitmap? first = null;
        try { first = grabber.Grab(area, includeCursor: true); }
        catch (System.ComponentModel.Win32Exception) { return; } // no desktop session
        first.Dispose();

        var sw = Stopwatch.StartNew();
        const int runs = 5;
        for (var i = 0; i < runs; i++) grabber.Grab(area, includeCursor: true).Dispose();
        var perGrab = sw.Elapsed.TotalMilliseconds / runs;

        output.WriteLine($"virtual screen {area.Width}x{area.Height}: {perGrab:0} ms per full grab");
        Assert.True(perGrab < 1500, $"a full-desktop grab took {perGrab:0} ms");
    }

    [Fact]
    public void StitchingTwentyFramesOfALargeAreaStaysInteractive()
    {
        var rng = new Random(9);
        using var page = new Bitmap(1200, 6000);
        using (var g = Graphics.FromImage(page))
            for (var y = 0; y < page.Height; y += 4)
                using (var brush = new SolidBrush(Color.FromArgb(rng.Next(256), rng.Next(256), rng.Next(256))))
                    g.FillRectangle(brush, rng.Next(0, 600), y, rng.Next(100, 600), 4);

        using var stitcher = new SnapGit.Core.Imaging.ScrollStitcher();
        var sw = Stopwatch.StartNew();
        for (var top = 0; top + 900 <= page.Height && stitcher.FrameCount < 20; top += 300)
        {
            using var frame = page.Clone(new Rectangle(0, top, 1200, 900), page.PixelFormat);
            stitcher.TryAdd(frame);
        }
        output.WriteLine($"{stitcher.FrameCount} frames of 1200x900 stitched in {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / Math.Max(1, stitcher.FrameCount)} ms/frame)");
        Assert.Equal(18, stitcher.FrameCount); // (6000 - 900) / 300 + 1
        Assert.True(sw.ElapsedMilliseconds < 15_000, $"stitching took {sw.ElapsedMilliseconds} ms");
    }
}

using SnapGit.Core.Imaging;
using SnapGit.Core.Models;
using SnapGit.Core.Settings;
using SnapGit.Platform.Capture;

namespace SnapGit.App.Services;

/// <summary>
/// Auto-scrolls the area under <c>area</c> with synthetic wheel input and stitches the frames.
/// The step size adapts to how far the content really moved, backs off when a jump was too big to align,
/// and stops when the page stops moving, the frame limit is hit, or the user presses Esc.
/// </summary>
public static class ScrollingCapture
{
    public static async Task<Bitmap?> RunAsync(IScreenGrabber grabber, AppSettings settings, Rectangle area,
        IProgress<int> progress, CancellationToken cancellation = default)
    {
        var horizontal = settings.ScrollDirection == ScrollDirection.Horizontal;
        var extent = horizontal ? area.Width : area.Height;
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        var delay = Math.Clamp(settings.ScrollDelayMs, 100, 2000);

        using var stitcher = new ScrollStitcher(settings.ScrollDirection);

        using (var first = grabber.Grab(area, includeCursor: false))
            stitcher.TryAdd(first);
        progress.Report(1);

        var notches = 2;
        var stalled = 0;
        var failedAlignments = 0;
        var attemptNumber = 0;

        for (var attempts = 0; attempts < settings.ScrollMaxFrames * 3 && stitcher.FrameCount < settings.ScrollMaxFrames; attempts++)
        {
            if (cancellation.IsCancellationRequested || ScrollInjector.IsKeyDown(Keys.Escape)) break;

            ScrollInjector.Scroll(center, notches, horizontal);
            await Task.Delay(delay, CancellationToken.None);

            using var frame = grabber.Grab(area, includeCursor: false);
            DumpFrame(frame, ++attemptNumber);
            var outcome = stitcher.TryAdd(frame);
            Core.Diagnostics.Log.Info($"scroll: frame {stitcher.FrameCount}, notches={notches}, outcome={outcome}, shift={stitcher.LastShift}");

            // A frame that does not line up is often just caught mid-animation (smooth scrolling, a fading
            // highlight): look once more at the same position before giving up on this step size.
            if (outcome == StitchOutcome.NoMatch)
            {
                await Task.Delay(delay, CancellationToken.None);
                using var retry = grabber.Grab(area, includeCursor: false);
                DumpFrame(retry, ++attemptNumber);
                outcome = stitcher.TryAdd(retry);
                Core.Diagnostics.Log.Info($"scroll: retry of the same position -> {outcome}");
            }
            switch (outcome)
            {
                case StitchOutcome.Appended:
                    stalled = 0;
                    failedAlignments = 0;
                    progress.Report(stitcher.FrameCount);
                    if (stitcher.LastShift > 0) // aim for ~55 % of the view per step: enough overlap, few frames
                        notches = Math.Clamp((int)Math.Round(notches * (extent * 0.55) / stitcher.LastShift), 1, 25);
                    break;

                case StitchOutcome.NoChange:
                    if (++stalled >= 2) return stitcher.Build(); // reached the end
                    break;

                case StitchOutcome.NoMatch:
                    if (++failedAlignments >= 3) return stitcher.Build();
                    // Jumped too far to align: go back and take smaller steps.
                    ScrollInjector.Scroll(center, -notches, horizontal);
                    await Task.Delay(delay, CancellationToken.None);
                    notches = Math.Max(1, notches / 2);
                    break;
            }
        }
        return stitcher.Build();
    }

    /// <summary>Developer aid: with SNAPGIT_DEBUG_FRAMES=&lt;folder&gt; every grabbed frame is saved, to see why a stitch stopped.</summary>
    private static void DumpFrame(Bitmap frame, int number)
    {
        var folder = Environment.GetEnvironmentVariable("SNAPGIT_DEBUG_FRAMES");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        frame.Save(Path.Combine(folder, $"frame-{number:D3}.png"), System.Drawing.Imaging.ImageFormat.Png);
    }
}

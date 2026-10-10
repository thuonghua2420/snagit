using System.Runtime.InteropServices;
using SnapGit.Core.Recording;
using SnapGit.Platform.Capture;
using SnapGit.Platform.Recording;
using Xunit;
using Xunit.Abstractions;

namespace SnapGit.Tests;

/// <summary>
/// DIAGNOSTICS, not part of the normal run (they put a window on the desktop for a few seconds): set SNAPGIT_REAL_SCREEN=1 to run them.
/// They record a REAL region of the screen (a window of our own with dense small text) and compare the video, the export and the editor preview with a direct grab.
/// Nothing is saved to disk; a test skips itself when its window is covered by something else.
/// </summary>
[Collection("Heavy")]
public class RealScreenFidelityTests : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

    private static bool Enabled => Environment.GetEnvironmentVariable("SNAPGIT_REAL_SCREEN") == "1";

    private readonly ITestOutputHelper _output;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-real-" + Guid.NewGuid().ToString("N"));

    public RealScreenFidelityTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class TextWindow : Form
    {
        public TextWindow(Size size)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
            ClientSize = size;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using var font = new Font("Segoe UI", 9f);
            using var mono = new Font("Consolas", 9f);
            for (var line = 0; line < 60; line++)
            {
                var color = line % 5 == 0 ? Color.FromArgb(200, 30, 30) : line % 5 == 1 ? Color.FromArgb(20, 120, 40) : Color.Black;
                using var brush = new SolidBrush(color);
                g.DrawString($"Row {line:00}  customer_id  first_name  last_name  {line * 7919 % 1000:000}@example.com  Hà Nội  Việt Nam", font, brush, 12, 6 + line * 17);
                g.DrawString("0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz", mono, brush, ClientSize.Width / 2f, 6 + line * 17);
            }
            using var pen = new Pen(Color.FromArgb(120, 120, 120), 1);
            for (var x = 0; x < ClientSize.Width; x += 160) g.DrawLine(pen, x, 0, x, ClientSize.Height);
        }
    }

    /// <summary>Wraps a grabber so every pixel becomes a 2 × 2 block.</summary>
    private sealed class DoublingGrabber(SnapGit.Core.Models.IScreenGrabber inner) : SnapGit.Core.Models.IScreenGrabber
    {
        public Rectangle VirtualScreen => inner.VirtualScreen;
        public Rectangle MonitorUnderCursor => inner.MonitorUnderCursor;
        public IReadOnlyList<SnapGit.Core.Models.WindowInfo> GetWindows(IntPtr excludeHandle = default) => inner.GetWindows(excludeHandle);
        public IReadOnlyList<SnapGit.Core.Models.WindowInfo> GetWindowChain(SnapGit.Core.Models.WindowInfo window, Point screenPoint) => inner.GetWindowChain(window, screenPoint);
        public Bitmap? GrabWindow(IntPtr handle) => inner.GrabWindow(handle);

        public Bitmap Grab(Rectangle bounds, bool includeCursor)
        {
            // asked for the doubled area: grab the real (half-size) one and enlarge it
            using var small = inner.Grab(new Rectangle(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height / 2), includeCursor);
            var big = new Bitmap(small.Width * 2, small.Height * 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var s = small.LockBits(new Rectangle(Point.Empty, small.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var d = big.LockBits(new Rectangle(Point.Empty, big.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var src = new int[s.Stride / 4 * small.Height];
            var dst = new int[d.Stride / 4 * big.Height];
            Marshal.Copy(s.Scan0, src, 0, src.Length);
            for (var y = 0; y < small.Height; y++)
                for (var x = 0; x < small.Width; x++)
                {
                    var p = src[y * (s.Stride / 4) + x];
                    var o = 2 * y * (d.Stride / 4) + 2 * x;
                    dst[o] = dst[o + 1] = dst[o + d.Stride / 4] = dst[o + d.Stride / 4 + 1] = p;
                }
            Marshal.Copy(dst, 0, d.Scan0, dst.Length);
            small.UnlockBits(s);
            big.UnlockBits(d);
            return big;
        }
    }

    private static Bitmap BoxDown(Bitmap big, int width, int height)
    {
        var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var p = Pixels(big);
        var stride = big.Width * 4;
        var data = result.LockBits(new Rectangle(Point.Empty, result.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var row = new byte[data.Stride * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                for (var c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (var dy = 0; dy < 2; dy++) for (var dx = 0; dx < 2; dx++) sum += p[(2 * y + dy) * stride + (2 * x + dx) * 4 + c];
                    row[y * data.Stride + x * 4 + c] = (byte)(sum / 4);
                }
        Marshal.Copy(row, 0, data.Scan0, row.Length);
        result.UnlockBits(data);
        return result;
    }

    private static byte[] Pixels(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { bmp.UnlockBits(data); }
    }

    private static double Psnr(Bitmap a, Bitmap b)
    {
        Assert.Equal(a.Size, b.Size);
        var pa = Pixels(a);
        var pb = Pixels(b);
        double sum = 0;
        for (var i = 0; i < pa.Length; i += 4)
            for (var c = 0; c < 3; c++) { var d = pa[i + c] - pb[i + c]; sum += d * d; }
        var mse = sum / (pa.Length / 4 * 3);
        return mse < 1e-9 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }


    /// <summary>PSNR of all channels, of luma (brightness: edges, text shape) and of chroma (colour: where 4:2:0 hurts ClearType text).</summary>
    private static (double All, double Luma, double Chroma) Fidelity(Bitmap a, Bitmap b)
    {
        Assert.Equal(a.Size, b.Size);
        var pa = Pixels(a);
        var pb = Pixels(b);
        double all = 0, luma = 0, chroma = 0;
        var count = pa.Length / 4;
        for (var i = 0; i < pa.Length; i += 4)
        {
            double ya = 0.299 * pa[i + 2] + 0.587 * pa[i + 1] + 0.114 * pa[i], yb = 0.299 * pb[i + 2] + 0.587 * pb[i + 1] + 0.114 * pb[i];
            luma += (ya - yb) * (ya - yb);
            double cba = pa[i] - ya, cbb = pb[i] - yb, cra = pa[i + 2] - ya, crb = pb[i + 2] - yb;
            chroma += ((cba - cbb) * (cba - cbb) + (cra - crb) * (cra - crb)) / 2;
            for (var c = 0; c < 3; c++) { var d = pa[i + c] - pb[i + c]; all += d * d; }
        }
        static double Db(double sum, int n) { var mse = sum / n; return mse < 1e-9 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse); }
        return (Db(all, count * 3), Db(luma, count), Db(chroma, count));
    }

    /// <summary>Mean absolute horizontal gradient: how crisp the edges are (a blurred copy has a much lower value).</summary>
    private static double Sharpness(Bitmap bmp)
    {
        var p = Pixels(bmp);
        var stride = bmp.Width * 4;
        double sum = 0;
        long n = 0;
        for (var y = 0; y < bmp.Height; y += 2)
            for (var x = 1; x < bmp.Width; x++)
            {
                var i = y * stride + x * 4;
                sum += Math.Abs(p[i + 1] - p[i - 3]);
                n++;
            }
        return sum / n;
    }

    [Fact]
    public async Task HowMuchTheEditorExportLoses()
    {
        if (!Enabled) return;
        Exception? failure = null;
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                SetThreadDpiAwarenessContext(new IntPtr(-4));
                using var form = new TextWindow(new Size(1600, 900));
                form.Show();
                form.Activate();
                form.BringToFront();
                var until = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }

                var grabber = new ScreenGrabber();
                var area = form.RectangleToScreen(form.ClientRectangle);
                using var direct = grabber.Grab(area, includeCursor: false);
                using (var expected = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(expected, new Rectangle(Point.Empty, expected.Size));
                    if (Psnr(expected, direct) < 30) { _output.WriteLine("the test window is not on top of the desktop here: skipped"); return; }
                }

                var path = Path.Combine(_dir, "source.mp4");
                using (var recorder = new VideoRecorder(grabber))
                {
                    recorder.Start(new RecordingOptions(area, 30) { Resolution = VideoResolution.Original, Quality = VideoQuality.High }, path);
                    var stop = DateTime.UtcNow.AddMilliseconds(3000);
                    while (DateTime.UtcNow < stop) { Application.DoEvents(); Thread.Sleep(10); }
                    recorder.StopAsync().GetAwaiter().GetResult();
                }

                void Report(string name, string file)
                {
                    using var frame = VideoEditor.GetFrameAsync(file, TimeSpan.FromSeconds(1.5), 4096).GetAwaiter().GetResult();
                    var (all, luma, chroma) = Fidelity(direct, frame);
                    _output.WriteLine($"{name,-34} {new FileInfo(file).Length / 1024,6} KB   RGB {all:0.0}   luma {luma:0.0} dB   chroma {chroma:0.0} dB");
                }

                Report("recording (1st generation)", path);

                var plainColour = Path.Combine(_dir, "plain-colour.mp4");
                using (var recorder = new VideoRecorder(grabber) { HighQualityColorConversion = false })
                {
                    recorder.Start(new RecordingOptions(area, 30) { Resolution = VideoResolution.Original, Quality = VideoQuality.High }, plainColour);
                    var stop = DateTime.UtcNow.AddMilliseconds(3000);
                    while (DateTime.UtcNow < stop) { Application.DoEvents(); Thread.Sleep(10); }
                    recorder.StopAsync().GetAwaiter().GetResult();
                }
                Report("recording, default colour conversion", plainColour);

                var info = VideoEditor.ProbeAsync(path).GetAwaiter().GetResult();
                var plan = VideoEditPlan.For(info.Duration, info.Size);
                var cutOnly = Path.Combine(_dir, "cut.mp4");
                VideoEditor.RenderAsync(path, cutOnly, plan.Remove(TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1))).GetAwaiter().GetResult();
                Report("export: a section cut out (2nd gen.)", cutOnly);

                var rotated = Path.Combine(_dir, "rot.mp4");
                VideoEditor.RenderAsync(path, rotated, plan.RotateClockwise().RotateCounterClockwise().FlipHorizontally().FlipHorizontally() with { Crop = new Rectangle(0, 0, 1600, 900) }).GetAwaiter().GetResult();
                Report("export: unchanged picture (2nd gen.)", rotated);
            }
            catch (Exception ex) { failure = ex; }
            finally { done.TrySetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task;
        if (failure is not null) throw failure;
    }

    [Fact]
    public async Task HowTheEditorPreviewScalesAFullHdFrame()
    {
        if (!Enabled) return;
        Exception? failure = null;
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                SetThreadDpiAwarenessContext(new IntPtr(-4));
                using var form = new TextWindow(new Size(1600, 900));
                form.Show();
                form.Activate();
                form.BringToFront();
                var until = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }

                var grabber = new ScreenGrabber();
                var area = form.RectangleToScreen(form.ClientRectangle);
                using var direct = grabber.Grab(area, includeCursor: false);
                using (var expected = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    form.DrawToBitmap(expected, new Rectangle(Point.Empty, expected.Size));
                    if (Psnr(expected, direct) < 30) { _output.WriteLine("the test window is not on top of the desktop here: skipped"); return; }
                }

                var path = Path.Combine(_dir, "preview.mp4");
                using (var recorder = new VideoRecorder(grabber))
                {
                    recorder.Start(new RecordingOptions(area, 30) { Resolution = VideoResolution.Original, Quality = VideoQuality.High }, path);
                    var stop = DateTime.UtcNow.AddMilliseconds(2500);
                    while (DateTime.UtcNow < stop) { Application.DoEvents(); Thread.Sleep(10); }
                    recorder.StopAsync().GetAwaiter().GetResult();
                }

                const int display = 1445; // the width of the picture area in the editor window of the screenshot
                var displayHeight = (int)Math.Round(area.Height * (double)display / area.Width);

                Bitmap Stretch(Bitmap src, System.Drawing.Drawing2D.InterpolationMode mode)
                {
                    var r = new Bitmap(display, displayHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(r);
                    g.InterpolationMode = mode;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.DrawImage(src, new Rectangle(0, 0, display, displayHeight), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
                    return r;
                }

                // the ideal: the sharp source shrunk once with a good filter
                using var reference = Stretch(direct, System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic);
                _output.WriteLine($"reference sharpness {Sharpness(reference):0.0}");

                using var m1small = VideoEditor.GetFrameAsync(path, TimeSpan.FromSeconds(1.5), 1280).GetAwaiter().GetResult();
                using var m1 = Stretch(m1small, System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear);
                using var m2 = VideoEditor.GetFrameAsync(path, TimeSpan.FromSeconds(1.5), display).GetAwaiter().GetResult();
                using var m3full = VideoEditor.GetFrameAsync(path, TimeSpan.FromSeconds(1.5), 4096).GetAwaiter().GetResult();
                using var m3 = Stretch(m3full, System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic);
                _output.WriteLine($"native decode {m3full.Width}x{m3full.Height}");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 12; i++) { using var f = VideoEditor.GetFrameAsync(path, TimeSpan.FromSeconds(0.2 + i * 0.1), 4096).GetAwaiter().GetResult(); }
                _output.WriteLine($"native decode (new composition each call, like GetFrameAsync): {watch.ElapsedMilliseconds / 12.0:0} ms per frame");

                foreach (var (name, frame) in new[] { ("M1 decode 1280 then stretch (current editor)", m1), ("M2 decode at display width", m2), ("M3 decode native, one bicubic shrink", m3) })
                {
                    using var fitted = frame.Size == reference.Size ? new Bitmap(frame) : Stretch(frame, System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor); // 1 px rounding difference only
                    var (all, luma, chroma) = Fidelity(reference, fitted);
                    _output.WriteLine($"{name,-48} luma {luma:0.0} dB  chroma {chroma:0.0} dB  sharpness {Sharpness(frame):0.0}");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { done.TrySetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task;
        if (failure is not null) throw failure;
    }

    [Fact]
    public async Task RecordedRealScreenMatchesADirectGrab()
    {
        if (!Enabled) return;
        Exception? failure = null;
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                SetThreadDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2, like the app
                _output.WriteLine($"system DPI {GetDpiForSystem()} (96 = 100 %)");
                using var form = new TextWindow(new Size(1280, 720));
                form.Show();
                form.Activate();
                form.BringToFront();
                var until = DateTime.UtcNow.AddMilliseconds(800);
                while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }

                var grabber = new ScreenGrabber();
                var area = form.RectangleToScreen(form.ClientRectangle);
                _output.WriteLine($"area {area}");
                using var direct = grabber.Grab(area, includeCursor: false);
                using (var expected = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    // the region must show OUR window; otherwise this would measure (and could expose) whatever is on the desktop
                    form.DrawToBitmap(expected, new Rectangle(Point.Empty, expected.Size));
                    var match = Psnr(expected, direct);
                    _output.WriteLine($"window on screen vs its own render: {match:0.0} dB");
                    if (match < 30) { _output.WriteLine("the test window is not on top of the desktop here: skipped (nothing measured, nothing saved)"); return; }
                }

                // 1) plain recording, 2) every pixel doubled (nearest) before encoding: 4:2:0 chroma at 2x size = exact colour per source pixel
                foreach (var oversample in new[] { 1, 2 })
                {
                    var path = Path.Combine(_dir, $"v{oversample}.mp4");
                    SnapGit.Core.Models.IScreenGrabber source = oversample == 1 ? grabber : new DoublingGrabber(grabber);
                    var recordArea = oversample == 1 ? area : new Rectangle(area.X, area.Y, area.Width * 2, area.Height * 2);
                    using var recorder = new VideoRecorder(source);
                    recorder.Start(new RecordingOptions(recordArea, 30) { Resolution = VideoResolution.Original, Quality = VideoQuality.High }, path);
                    var stop = DateTime.UtcNow.AddMilliseconds(3000);
                    while (DateTime.UtcNow < stop) { Application.DoEvents(); Thread.Sleep(10); }
                    recorder.StopAsync().GetAwaiter().GetResult();

                    var size = recorder.OutputSize;
                    using var decoded = VideoEditor.GetFrameAsync(path, TimeSpan.FromSeconds(2), size.Width).GetAwaiter().GetResult();
                    // bring the decoded frame back to source resolution exactly like a player on a 1x display would (2x2 box average)
                    using var frame = oversample == 1 ? new Bitmap(decoded) : BoxDown(decoded, area.Width, area.Height);
                    var (all, luma, chroma) = Fidelity(direct, frame);
                    _output.WriteLine($"oversample {oversample}x  encoded {size.Width}x{size.Height}  {new FileInfo(path).Length / 1024,6} KB   RGB {all:0.0} dB   luma {luma:0.0} dB   chroma {chroma:0.0} dB");
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { done.TrySetResult(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task;
        if (failure is not null) throw failure;
    }
}

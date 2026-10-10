using System.Runtime.ExceptionServices;
using System.Text.Json;
using SnapGit.App.Forms;
using SnapGit.App.Services;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Core.Recording;
using SnapGit.Platform.Recording;
using Xunit;

namespace SnapGit.Tests;

/// <summary>The video editor page inside the main window, on a real clip: loads, plays, edits, undoes, exports a new library item. Needs a desktop session.</summary>
[Collection("UI")]
public class VideoEditorFormTests : IClassFixture<QuadrantClip>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "snapgit-vedit-" + Guid.NewGuid().ToString("N"));
    private readonly QuadrantClip _clip;

    public VideoEditorFormTests(QuadrantClip clip)
    {
        _clip = clip;
        Directory.CreateDirectory(_root);
        AppPaths.DataRoot = Path.Combine(_root, "data");
        Directory.CreateDirectory(AppPaths.DataRoot);
    }

    public void Dispose()
    {
        AppPaths.DataRoot = Path.Combine(Path.GetTempPath(), "snapgit-tests-data");
        Loc.SetLanguage("en");
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90)))
            throw new TimeoutException("The UI thread did not finish within 90 s — is a modal dialog waiting for input?");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Pump(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static void PumpUntil(Func<bool> condition, int timeoutMs, string what)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Timed out waiting for: {what}");
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static T Await<T>(Task<T> task, int timeoutMs, string what)
    {
        PumpUntil(() => task.IsCompleted, timeoutMs, what);
        return task.GetAwaiter().GetResult();
    }

    private static void Shot(Control control, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SNAPGIT_SHOTS");
        if (string.IsNullOrEmpty(dir) || control.Width < 1 || control.Height < 1) return;
        Directory.CreateDirectory(dir);
        using var bmp = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bmp, new Rectangle(Point.Empty, control.Size));
        bmp.Save(Path.Combine(dir, name + ".png"));
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child)) yield return nested;
        }
    }

    private AppHost NewHost(string language, string theme)
    {
        var settings = new Dictionary<string, object?>
        {
            ["Language"] = language,
            ["Theme"] = theme,
            ["LibraryFolder"] = Path.Combine(_root, "lib"),
        };
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings));
        return new AppHost();
    }

    private string AddToLibrary(AppHost host)
    {
        var path = host.Library.ReservePath(".mp4", "clip");
        File.Copy(_clip.Path_, path);
        host.Library.NotifyModified(path);
        return path;
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static VideoEditorPage OpenPage(EditorForm main, string path)
    {
        Assert.True(Await(main.OpenVideoAsync(path), 20000, "the video page to open"));
        var page = main.Video!;
        PumpUntil(() => page.Loaded && page.View.HasFrame, 15000, "the video to load and show its first frame");
        return page;
    }

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void EditsPlaysAndExportsAClip(string language, string theme)
    {
        if (!_clip.Available) return;
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            var path = AddToLibrary(host);
            using var main = new EditorForm(host, EditorForm.BlankDocument(), null, isProject: false);
            main.Show();
            var form = OpenPage(main, path);
            Assert.Equal(EditorForm.MainPage.Video, main.Page);
            Assert.False(main.Canvas.Visible, "the editor is hidden behind the video page");

            Assert.InRange(form.Playback.Duration.TotalSeconds, 2.0, 4.5);
            Assert.Equal(new Size(320, 200), form.Plan.SourceSize);
            Assert.True(form.Plan.IsUnchanged);

            // every toolbar button of the page is reachable by screen readers
            var unnamed = AllControls(form).OfType<ToolStrip>().SelectMany(s => s.Items.Cast<ToolStripItem>())
                .Where(i => i is not ToolStripSeparator and not ToolStripLabel and not ToolStripProgressBar)
                .Where(i => string.IsNullOrWhiteSpace(i.AccessibleName)).Select(i => $"{i.GetType().Name} '{i.Text}'").ToList();
            Assert.True(unnamed.Count == 0, "Video page items without an accessible name: " + string.Join(", ", unnamed));

            // cut a section out, undo, keep a range
            var total = form.Playback.Duration;
            form.Timeline.Selection = new TimeRange(S(1), S(2));
            form.DeleteSelection();
            Assert.Equal(2, form.Plan.Keep.Count);
            Assert.Null(form.Timeline.Selection);
            form.Undo();
            Assert.Single(form.Plan.Keep);
            form.Redo();
            Assert.Equal(2, form.Plan.Keep.Count);
            form.Undo();

            form.Timeline.Selection = new TimeRange(S(0.5), S(2.5));
            form.KeepSelection();
            Assert.Equal([new TimeRange(S(0.5), S(2.5))], form.Plan.Keep);
            form.Undo();
            Assert.Equal([new TimeRange(TimeSpan.Zero, total)], form.Plan.Keep);

            // a selection that would leave nothing is refused
            form.Timeline.Selection = new TimeRange(TimeSpan.Zero, total + S(5));
            form.DeleteSelection();
            Assert.Single(form.Plan.Keep);
            form.Timeline.Selection = null;

            // picture edits
            form.Rotate(clockwise: true);
            Assert.Equal(90, form.Plan.Rotation);
            Assert.Equal(new Size(200, 320), form.Plan.OutputSize);
            form.Flip(horizontal: true);
            Assert.True(form.Plan.FlipX);
            form.ToggleMute();
            Assert.True(form.Plan.Mute);

            // crop: draw a rectangle in the rotated picture, apply it
            form.SetCropMode(true);
            Assert.True(form.View.CropMode);
            Assert.Equal(new Rectangle(0, 0, 200, 320), form.View.CropDraft);
            form.View.CropDraft = new Rectangle(0, 0, 200, 160);
            form.SetCropMode(false);
            Assert.False(form.View.CropMode);
            Assert.Equal(new Rectangle(0, 0, 200, 160), form.Plan.Crop);
            Assert.Equal(new Size(200, 160), form.Plan.OutputSize);

            form.ApplyEdit(p => p.WithCaption(new VideoCaption("Hi", S(0), S(1))));
            Assert.Single(form.Plan.Captions);
            Pump(300);
            Shot(main, $"video-editor-{language}-{theme}");

            // undo all the way back
            while (!form.Plan.IsUnchanged) form.Undo();
            Assert.Equal(0, form.Plan.Rotation);
            Assert.Null(form.Plan.Crop);

            form.Rotate(clockwise: false);
            form.Timeline.Selection = new TimeRange(S(0.5), S(2));
            form.DeleteSelection();

            // export as a NEW library item, original untouched
            var before = host.Library.GetItems().Count;
            var exported = Await(form.ExportAsync(), 60000, "the export");
            Assert.NotNull(exported);
            Assert.True(File.Exists(exported));
            Assert.NotEqual(path, exported);
            Assert.Equal(before + 1, host.Library.GetItems().Count);
            Assert.True(new FileInfo(path).Length == new FileInfo(_clip.Path_).Length, "the original is untouched");

            var result = Await(VideoEditor.ProbeAsync(exported!), 15000, "probing the export");
            Assert.Equal(new Size(200, 320), result.Size);
            Assert.InRange(result.Duration.TotalSeconds, total.TotalSeconds - 2.0, total.TotalSeconds - 1.0);
            Assert.False(form.IsDirty, "exporting counts as saving");

            main.Close(); // hides the window (nothing unsaved is left to ask about)
            Assert.False(main.Visible);
            Assert.Null(main.Video);
            Assert.Equal(EditorForm.MainPage.Library, main.Page);
        });
    }

    [Fact]
    public void PlayingSkipsCutPartsAndStopsAtTheEndOfWhatIsKept()
    {
        if (!_clip.Available) return;
        RunSta(() =>
        {
            using var host = NewHost("en", "Light");
            var path = AddToLibrary(host);
            using var main = new EditorForm(host, EditorForm.BlankDocument(), null, isProject: false);
            main.Show();
            var form = OpenPage(main, path);

            // keep 0 – 0.6 s and 1.8 s – end
            form.ApplyEdit(p => p.Remove(S(0.6), S(1.8)));
            var seen = new List<TimeSpan>();
            form.Playback.FrameReady += (_, at) => seen.Add(at);

            form.TogglePlay();
            Assert.True(form.Playback.IsPlaying);
            PumpUntil(() => !form.Playback.IsPlaying, 12000, "playback to stop at the end");

            Assert.NotEmpty(seen);
            // the cut part is not displayed (a frame or two near the edges is tolerated while the seek takes effect)
            Assert.True(seen.Count(t => t > S(0.75) && t < S(1.65)) <= 2, "frames from inside the cut part were shown: " + string.Join(", ", seen.Where(t => t > S(0.75) && t < S(1.65))));
            Assert.Contains(seen, t => t >= S(1.8));

            form.MarkClean();
        });
    }

    [Fact]
    public void VideosOpenInTheSameWindowAsPicturesAndKeepTheirOwnState()
    {
        if (!_clip.Available) return;
        RunSta(() =>
        {
            using var host = NewHost("en", "Light");
            var path = AddToLibrary(host);
            Assert.Equal(LibraryItemKind.Video, LibraryItem.KindOf(path));
            LibraryItem picture;
            using (var bmp = new Bitmap(200, 120)) picture = host.Library.Add(bmp, "still");

            var main = host.MainWindow;
            host.OpenItem(path);
            PumpUntil(() => main.Video?.Loaded == true, 15000, "the video page");
            Assert.Equal(EditorForm.MainPage.Video, main.Page);
            Assert.Equal(1, Application.OpenForms.Cast<Form>().Count(f => f is EditorForm)); // one window for everything

            // the same video again → the same page, nothing reloads
            var page = main.Video;
            host.OpenItem(path);
            Pump(200);
            Assert.Same(page, main.Video);

            // a picture opens in the SAME window; the video page is kept (hidden) with its edits
            page!.ApplyEdit(p => p with { Mute = true });
            host.OpenItem(picture.FilePath);
            Assert.Equal(EditorForm.MainPage.Editor, main.Page);
            Assert.Equal(picture.FilePath, main.CurrentPath);
            Assert.Same(page, main.Video);
            Assert.False(page.Visible);
            Assert.True(page.Plan.Mute);
            Assert.Equal(1, Application.OpenForms.Cast<Form>().Count(f => f is EditorForm));

            // library page, then back to whatever was in front last (the picture)
            host.ShowLibrary();
            Assert.Equal(EditorForm.MainPage.Library, main.Page);
            Assert.True(main.CanGoBack);
            main.GoBack();
            Assert.Equal(EditorForm.MainPage.Editor, main.Page);

            page.MarkClean();
        });
    }
}

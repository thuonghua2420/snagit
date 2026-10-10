using System.Runtime.ExceptionServices;
using System.Text.Json;
using SnapGit.App.Forms;
using SnapGit.App.Services;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Editor;
using SnapGit.Editor.Annotations;
using SnapGit.Editor.Tools;
using Xunit;

namespace SnapGit.Tests;

/// <summary>
/// Builds and paints the real forms (light/dark, English/Vietnamese) on an STA thread. Catches constructor and
/// paint-time exceptions (missing icon cases, bad layout code, GDI+ argument errors) that unit tests of the logic can't see.
/// </summary>
[Collection("UI")]
public class UiSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "snapgit-ui-" + Guid.NewGuid().ToString("N"));
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

    private void Mark(string step) => _output.WriteLine($"T {_sw.ElapsedMilliseconds,7} ms  {step}");

    public UiSmokeTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
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

        // A timeout must FAIL the test: it almost always means a modal dialog (e.g. "Save changes?") is blocking the UI thread.
        if (!thread.Join(TimeSpan.FromSeconds(45)))
            throw new TimeoutException("The UI thread did not finish within 45 s — is a modal dialog waiting for input?");

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Saves a rendering of a control when SNAPGIT_SHOTS points to a folder (visual review aid; no screen capture involved).</summary>
    private static void Shot(Control control, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SNAPGIT_SHOTS");
        if (string.IsNullOrEmpty(dir) || control.Width < 1 || control.Height < 1) return;
        Directory.CreateDirectory(dir);
        using var bmp = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bmp, new Rectangle(Point.Empty, control.Size));
        bmp.Save(Path.Combine(dir, name + ".png"));
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

    private AppHost NewHost(string language, string theme, Action<Dictionary<string, object?>>? tweak = null)
    {
        var settings = new Dictionary<string, object?>
        {
            ["Language"] = language,
            ["Theme"] = theme,
            ["LibraryFolder"] = Path.Combine(_root, "lib"),
        };
        tweak?.Invoke(settings);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings));
        return new AppHost();
    }

    private static Bitmap Picture(int w = 480, int h = 320)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.WhiteSmoke);
        g.FillRectangle(Brushes.SteelBlue, 20, 20, 200, 120);
        g.DrawString("SnapGit", new Font("Segoe UI", 28), Brushes.Black, 240, 60);
        return bmp;
    }

    private static ToolMouseEventArgs Mouse(float x, float y, MouseButtons b = MouseButtons.Left) => new(new PointF(x, y), b, Keys.None);

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void EditorFormBuildsAndPaintsEveryAnnotationType(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            using var form = new EditorForm(host, new EditorDocument(Picture()), filePath: null, isProject: false);
            form.Show();
            Pump(200);

            var context = form.Context;

            // Drag-created tools
            var y = 10f;
            foreach (var kind in new[] { ToolKind.Arrow, ToolKind.Line, ToolKind.Rectangle, ToolKind.Ellipse, ToolKind.Highlight, ToolKind.Blur, ToolKind.Spotlight, ToolKind.Magnify })
            {
                context.SetTool(kind);
                context.Tool.MouseDown(context, Mouse(30, y));
                context.Tool.MouseMove(context, Mouse(200, y + 35));
                context.Tool.MouseUp(context, Mouse(200, y + 35));
                y += 10;
            }
            // Click tools and others
            context.SetTool(ToolKind.Step);
            context.Tool.MouseDown(context, Mouse(300, 200));
            context.SetTool(ToolKind.Stamp);
            foreach (var stamp in Enum.GetValues<StampKind>())
            {
                context.CurrentStamp = stamp;
                context.Tool.MouseDown(context, Mouse(260 + (int)stamp * 8, 260));
            }

            context.SetTool(ToolKind.Pen);
            context.Tool.MouseDown(context, Mouse(10, 300));
            context.Tool.MouseMove(context, Mouse(60, 310));
            context.Tool.MouseMove(context, Mouse(120, 295));
            context.Tool.MouseUp(context, Mouse(120, 295));
            using var graphics = form.CreateGraphics();
            var text = new TextAnnotation { Rect = new RectangleF(250, 20, 150, 40), Style = context.GetToolStyle(ToolKind.Text) };
            context.CommitText(new TextEditRequest(text, true), "Hello", graphics);
            var callout = new CalloutAnnotation { Style = context.GetToolStyle(ToolKind.Callout) };
            callout.SetDrag(new PointF(250, 100), new PointF(420, 150), false);
            context.CommitText(new TextEditRequest(callout, true), "Callout", graphics);
            // Select-all + every toolbar sync path
            context.SetTool(ToolKind.Select);
            context.SelectAll();
            context.UpdateStyle(s => s with { Opacity = 0.8f, Shadow = true, GradientTo = Color.DodgerBlue, GradientAngle = 45, OutlineWidth = 2, OutlineColor = Color.White });
            Pump(100);

            // Organise: align, group (renders through the group), ungroup, distribute
            Assert.True(context.Align(AlignMode.Left));
            Assert.True(context.GroupSelection());
            using (var groupShot = new Bitmap(form.Canvas.Width, form.Canvas.Height)) form.Canvas.DrawToBitmap(groupShot, new Rectangle(Point.Empty, groupShot.Size));
            Assert.True(context.UngroupSelection());
            Assert.True(context.Selected.Count >= 20);
            context.Distribute(horizontal: true);
            Pump(50);
            Assert.True(context.Document.Annotations.Count >= 20);

            // Paint the canvas for real (OnPaint with every annotation + selection chrome)
            using var shot = new Bitmap(form.Canvas.Width, form.Canvas.Height);
            form.Canvas.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
            using var shotForm = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(shotForm, new Rectangle(Point.Empty, shotForm.Size));
            context.SetSelection([]);
            Shot(form, $"editor-{language}-{theme}");
            // Every tool can be selected without throwing; every style-bar state renders.
            foreach (var kind in Enum.GetValues<ToolKind>())
            {
                context.SetTool(kind);
                Pump(20);
            }

            context.Document.MarkSaved(); // otherwise Close() shows the "Save changes?" dialog
            form.Close();
        });
    }

    [Fact]
    public void EveryToolbarButtonHasAnAccessibleName()
    {
        RunSta(() =>
        {
            using var host = NewHost("en", "Light");
            using var editor = new EditorForm(host, new EditorDocument(Picture()), null, false);
            editor.ShowLibraryPage(); // creates the library page, which lives inside the same window
            editor.ShowEditorPage();

            // (the video page has its own check in VideoEditorFormTests)
            var unnamed = AllControls(editor).OfType<ToolStrip>()
                .SelectMany(s => s.Items.Cast<ToolStripItem>())
                .Where(i => i is not ToolStripSeparator and not ToolStripLabel and not ToolStripComboBox and not ToolStripTextBox)
                .Where(i => string.IsNullOrWhiteSpace(i.AccessibleName))
                .Select(i => $"{i.GetType().Name} tip='{i.ToolTipText}' text='{i.Text}'")
                .ToList();
            Assert.True(unnamed.Count == 0, "Toolbar items without an accessible name:\n" + string.Join("\n", unnamed));
            Assert.Contains(AllControls(editor), c => c is LibraryView);
        });
    }

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void SettingsAndHotkeysAreReachableFromTheEditor(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            using var editor = new EditorForm(host, new EditorDocument(Picture()), null, false);

            var menu = editor.MainMenuStrip!.Items.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Single(m => m.Text == Loc.T("&Settings"));
            var entries = menu.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
            Assert.Contains(Loc.T("Capture hotkeys…"), entries);
            Assert.Contains(Loc.T("All settings…"), entries);

            var button = AllControls(editor).OfType<ToolStrip>().SelectMany(s => s.Items.Cast<ToolStripItem>())
                .Single(i => i.ToolTipText == Loc.T("Settings and capture hotkeys"));
            Assert.Equal(Loc.T("Settings and capture hotkeys"), button.AccessibleName); // icon-only: screen readers get the tooltip

            // the dialog opens straight on the requested tab, and every hotkey box shows its current gesture
            using var form = new SettingsForm(host.Settings, [], SettingsTab.Hotkeys);
            form.Show();
            Pump(100);
            var tabs = form.Controls.OfType<TabControl>().Single();
            Assert.Equal(Loc.T("Hotkeys"), tabs.SelectedTab!.Text);
            Shot(form, $"settings-hotkeys-{language}-{theme}");
            Assert.NotEmpty(AllControls(tabs.SelectedTab).OfType<SnapGit.App.Controls.HotkeyBox>());
            form.Close();

            using var recording = new SettingsForm(host.Settings, [], SettingsTab.Recording);
            recording.Show();
            Pump(50);
            Assert.Equal(Loc.T("Recording"), recording.Controls.OfType<TabControl>().Single().SelectedTab!.Text);
            recording.Close();
        });
    }

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void EditorShowsRecentCapturesInItsHistoryStrip(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            LibraryItem current;
            using (var bmp = Picture())
            {
                host.Library.Add(bmp, "alpha");
                host.Library.Add(bmp, "beta");
                current = host.Library.Add(bmp, "gamma");
            }

            using var form = new EditorForm(host, new EditorDocument(Picture()), current.FilePath, isProject: false);
            form.Show();
            Pump(1500); // thumbnails load asynchronously

            var strip = form.History;
            Assert.Equal(3, strip.ItemCount);
            Assert.Equal(3, strip.ThumbnailsLoaded);
            Assert.True(strip.Bottom <= form.ClientSize.Height && strip.Height > 60, "the strip is docked inside the window");
            Assert.True(strip.Left >= form.Canvas.Left, "it sits under the canvas, not under the tool box");

            // A new capture appears without reopening anything.
            using (var bmp = Picture()) host.Library.Add(bmp, "delta");
            Pump(500);
            Assert.Equal(4, strip.ItemCount);
            Shot(form, $"editor-history-{language}-{theme}");

            form.Context.Document.MarkSaved();
            form.Close();
        });
    }

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void LibraryPageShowsEveryKindOfItem(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            using (var bmp = Picture())
            {
                host.Library.Add(bmp, "alpha");
                var item = host.Library.Add(bmp, "beta");
                host.Library.SetTags(item, ["work"]);
                host.Library.SetFavorite(item, true);
            }
            // an animated GIF
            using (var f1 = Picture(120, 80)) using (var f2 = Picture(120, 80))
                SnapGit.Core.Imaging.GifEncoder.Write(host.Library.ReservePath(".gif", "anim"), [new(f1, 100), new(f2, 100)]);

            using var form = new EditorForm(host, new EditorDocument(Picture()), null, false);
            form.Show();
            form.ShowLibraryPage();
            Pump(1500); // thumbnails load asynchronously

            Shot(form, $"library-{language}-{theme}");
            var list = form.Library!.List;
            Assert.Equal(3, list.Items.Count);
            Assert.All(list.Items.Cast<ListViewItem>(), i => Assert.NotEqual("placeholder", i.ImageKey)); // every thumbnail arrived

            form.Document.MarkSaved();
            form.Close();
        });
    }

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void SettingsFormSavesChangesThroughItsSaveButton(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            using var form = new SettingsForm(host.Settings, host.Ocr.AvailableLanguages);
            form.Show();
            Pump(150);

            var tabs = AllControls(form).OfType<TabControl>().Single();
            for (var i = 0; i < tabs.TabPages.Count; i++) { tabs.SelectedIndex = i; Pump(60); Shot(form, $"settings-{language}-{theme}-{i}"); }
            tabs.SelectedIndex = 0;
            var checks = AllControls(form).OfType<CheckBox>().ToList();
            var cursor = checks.First(c => c.Text.Contains(Loc.T("Include the mouse cursor"), StringComparison.Ordinal));
            var wasChecked = cursor.Checked;
            cursor.Checked = !wasChecked;

            var save = AllControls(form).OfType<Button>().First(b => b.Text == Loc.T("Save"));
            save.PerformClick();
            Pump(100);

            Assert.Equal(!wasChecked, host.Settings.Current.IncludeCursor);
            Assert.True(File.Exists(AppPaths.SettingsFile));
            Assert.Contains("IncludeCursor", File.ReadAllText(AppPaths.SettingsFile));
        });
    }

    [Fact]
    public void TrimFormCutsAGifIntoANewLibraryItem()
    {
        RunSta(() =>
        {
            using var host = NewHost("en", "Light");
            var frames = Enumerable.Range(0, 6).Select(i =>
            {
                var bmp = new Bitmap(80, 40);
                using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.FillRectangle(Brushes.Blue, 5 + i * 10, 10, 8, 8); }
                return new SnapGit.Core.Imaging.GifFrame(bmp, 100);
            }).ToList();
            var gifPath = host.Library.ReservePath(".gif", "anim");
            SnapGit.Core.Imaging.GifEncoder.Write(gifPath, frames);
            foreach (var f in frames) f.Image.Dispose();

            var item = host.Library.GetItems().Single();
            using var form = new TrimForm(host, item);
            form.Show();
            Pump(1500); // frames load asynchronously

            var bars = AllControls(form).OfType<TrackBar>().ToList();
            Assert.Equal(2, bars.Count);
            Assert.Equal(5, bars[1].Maximum);        // 6 frames -> 0..5
            bars[0].Value = 1;                       // first kept frame = 2nd
            bars[1].Value = 3;                       // last kept = 4th
            Pump(100);
            bars[0].Value = 4;                       // crossing the other handle pushes it, never inverts the range
            Assert.True(bars[1].Value >= bars[0].Value);
            bars[0].Value = 1;
            bars[1].Value = 3;

            AllControls(form).OfType<Button>().First(b => b.Text == Loc.T("Save as new item")).PerformClick();
            Pump(2500);

            var gifs = host.Library.GetItems(new LibraryQuery { Kind = LibraryItemKind.Animation });
            Assert.Equal(2, gifs.Count);
            var trimmed = gifs.First(g => g.FilePath != item.FilePath);
            Assert.Contains("trimmed", trimmed.Name);
            var kept = SnapGit.Core.Imaging.GifTrimmer.ReadFrames(trimmed.FilePath);
            Assert.Equal(3, kept.Count);
            foreach (var f in kept) f.Image.Dispose();
            Assert.True(File.Exists(item.FilePath)); // the original is untouched
        });
    }

    [Fact]
    public void AboutFormAndParameterDialogBuild()
    {
        RunSta(() =>
        {
            using var host = NewHost("vi", "Dark");
            using var about = new AboutForm(host);
            about.Show();
            Pump(100);
            about.Close();

            var dialog = new ParameterDialog("t")
                .AddLabel("hello").AddNumber("n", "N", 5, 1, 10).AddText("t", "T", "x").AddCombo("c", "C", ["a", "b"], 1)
                .AddCheck("k", "K", true).AddColor("col", "Color", Color.Red).AddMultiline("m", "M", "l1\nl2");
            Assert.Equal(5, dialog.Int("n"));
            Assert.Equal("x", dialog.String("t"));
            Assert.Equal(1, dialog.Index("c"));
            Assert.True(dialog.Bool("k"));
            Assert.Equal(Color.Red.ToArgb(), dialog.ColorValue("col").ToArgb());
            dialog.Dispose();
        });
    }

    [Fact]
    public void RecoveryFilesAreOfferedOnlyWhenOldEnough()
    {
        var fresh = RecoveryService.NewPath();
        Directory.CreateDirectory(RecoveryService.Folder);
        File.WriteAllText(fresh, "x");
        Assert.DoesNotContain(fresh, RecoveryService.Pending()); // just written: a live autosave, not a crash leftover

        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddMinutes(-5));
        Assert.Contains(fresh, RecoveryService.Pending());

        RecoveryService.Discard(fresh);
        Assert.DoesNotContain(fresh, RecoveryService.Pending());
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child)) yield return nested;
        }
    }
}

[CollectionDefinition("UI", DisableParallelization = true)]
public class UiCollection;

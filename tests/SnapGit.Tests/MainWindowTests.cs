using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using SnapGit.App.Forms;
using SnapGit.App.Services;
using SnapGit.Core.Diagnostics;
using SnapGit.Core.Library;
using SnapGit.Core.Localization;
using SnapGit.Editor.Tools;
using Xunit;

namespace SnapGit.Tests;

/// <summary>
/// Capture, library, editor and history are ONE window now: pictures load into it in place, the library is a page of it,
/// closing hides it, and it steps aside while a capture runs.
/// </summary>
[Collection("UI")]
public class MainWindowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "snapgit-main-" + Guid.NewGuid().ToString("N"));

    public MainWindowTests()
    {
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
        if (!thread.Join(TimeSpan.FromSeconds(60)))
            throw new TimeoutException("The UI thread did not finish within 60 s — is a modal dialog waiting for input?");
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

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child)) yield return nested;
        }
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

    private AppHost NewHost(string language = "en", string theme = "Light")
    {
        var settings = new Dictionary<string, object?> { ["Language"] = language, ["Theme"] = theme, ["LibraryFolder"] = Path.Combine(_root, "lib") };
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings));
        return new AppHost();
    }

    private static LibraryItem AddPicture(AppHost host, string title, Color color)
    {
        using var bmp = new Bitmap(320, 200);
        using (var g = Graphics.FromImage(bmp)) g.Clear(color);
        return host.Library.Add(bmp, title);
    }

    private static void DrawRectangle(SnapGit.Editor.EditorContext context)
    {
        context.SetTool(ToolKind.Rectangle);
        context.Tool.MouseDown(context, new ToolMouseEventArgs(new PointF(20, 20), MouseButtons.Left, Keys.None));
        context.Tool.MouseMove(context, new ToolMouseEventArgs(new PointF(120, 90), MouseButtons.Left, Keys.None));
        context.Tool.MouseUp(context, new ToolMouseEventArgs(new PointF(120, 90), MouseButtons.Left, Keys.None));
    }

    private static int EditorWindows() => Application.OpenForms.Cast<Form>().Count(f => f is EditorForm);

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void OneWindowHoldsTheLibraryTheEditorAndTheHistory(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            var first = AddPicture(host, "alpha", Color.IndianRed);
            var second = AddPicture(host, "beta", Color.SteelBlue);
            AddPicture(host, "gamma", Color.SeaGreen);

            var main = host.MainWindow;
            host.ShowLibrary();
            Pump(1200);

            // the library page: everything captured so far; the editor's own chrome is out of the way
            Assert.True(main.Visible);
            Assert.Equal(EditorForm.MainPage.Library, main.Page);
            Assert.Equal(3, main.Library!.List.Items.Count);
            Assert.False(main.Canvas.Visible);
            Assert.False(main.History.Visible);
            Assert.False(main.CanGoBack, "nothing has been opened yet");
            Shot(main, $"main-library-{language}-{theme}");

            // opening an item loads it into THIS window
            host.OpenItem(first.FilePath);
            Pump(300);
            Assert.Equal(EditorForm.MainPage.Editor, main.Page);
            Assert.Equal(first.FilePath, main.CurrentPath);
            Assert.True(main.Canvas.Visible);
            Assert.True(main.History.Visible);
            Assert.False(main.Library.Visible);
            Assert.Equal(1, EditorWindows());
            Assert.Equal(3, main.History.ItemCount);
            Assert.True(main.CanGoBack);
            Shot(main, $"main-editor-{language}-{theme}");

            // …and so does the next one, keeping the colours the user chose
            var firstDocument = main.Document;
            main.Context.SetTool(ToolKind.Rectangle);
            main.Context.UpdateStyle(s => s with { Stroke = Color.Magenta });
            host.OpenItem(second.FilePath);
            Pump(200);
            Assert.NotSame(firstDocument, main.Document);
            Assert.Equal(second.FilePath, main.CurrentPath);
            Assert.Equal(1, EditorWindows());
            Assert.Equal(Color.Magenta.ToArgb(), main.Context.GetToolStyle(ToolKind.Rectangle).Stroke.ToArgb());

            // library again, and "Back" leads to the picture
            host.ShowLibrary();
            Assert.Equal(EditorForm.MainPage.Library, main.Page);
            main.GoBack();
            Assert.Equal(EditorForm.MainPage.Editor, main.Page);
            Assert.Equal(second.FilePath, main.CurrentPath);
        });
    }

    [Fact]
    public void UnsavedEditsAreAskedAboutBeforeAPictureIsReplaced()
    {
        RunSta(() =>
        {
            using var host = NewHost();
            var a = AddPicture(host, "A", Color.IndianRed);
            var b = AddPicture(host, "B", Color.SteelBlue);
            var main = host.MainWindow;
            var asked = new List<string>();

            host.OpenInEditor(a.FilePath);
            DrawRectangle(main.Context);
            Assert.True(main.Document.IsModified);

            // Cancel: nothing is replaced, the picture and its edit stay
            main.SaveChangesPrompt = name => { asked.Add(name); return DialogResult.Cancel; };
            Assert.False(host.OpenInEditor(b.FilePath));
            Assert.Single(asked);
            Assert.Equal(a.FilePath, main.CurrentPath);
            Assert.True(main.Document.IsModified);

            // No: the edit is dropped, the new picture is loaded clean
            main.SaveChangesPrompt = name => { asked.Add(name); return DialogResult.No; };
            Assert.True(host.OpenInEditor(b.FilePath));
            Assert.Equal(b.FilePath, main.CurrentPath);
            Assert.False(main.Document.IsModified);

            // Yes: the project is saved first, then replaced
            DrawRectangle(main.Context);
            var sizeBefore = new FileInfo(b.FilePath).Length;
            main.SaveChangesPrompt = name => { asked.Add(name); return DialogResult.Yes; };
            Assert.True(host.OpenInEditor(a.FilePath));
            Assert.Equal(a.FilePath, main.CurrentPath);
            Assert.NotEqual(sizeBefore, new FileInfo(b.FilePath).Length);   // B's project now holds the rectangle
            Assert.Equal(3, asked.Count);

            // a picture that is not modified is replaced without any question
            asked.Clear();
            Assert.True(host.OpenInEditor(b.FilePath));
            Assert.Empty(asked);
        });
    }

    [Fact]
    public void ClosingTheWindowHidesItAndStartsFresh()
    {
        RunSta(() =>
        {
            using var host = NewHost();
            var a = AddPicture(host, "A", Color.IndianRed);
            var main = host.MainWindow;
            host.OpenInEditor(a.FilePath);
            DrawRectangle(main.Context);

            // unsaved work: Cancel keeps the window as it is
            main.SaveChangesPrompt = _ => DialogResult.Cancel;
            main.Close();
            Assert.True(main.Visible);
            Assert.Equal(a.FilePath, main.CurrentPath);
            Assert.True(main.Document.IsModified);

            // Don't save: the window goes away (to the tray) but is not destroyed, and comes back blank on the library page
            main.SaveChangesPrompt = _ => DialogResult.No;
            main.Close();
            Assert.False(main.Visible);
            Assert.False(main.IsDisposed);
            Assert.Null(main.CurrentPath);
            Assert.False(main.Document.IsModified);
            Assert.Equal(EditorForm.MainPage.Library, main.Page);
            Assert.False(main.CanGoBack);

            host.ShowLibrary();
            Assert.True(main.Visible);
            Assert.Same(main, host.MainWindow);
        });
    }

    [Fact]
    public void TheWindowStepsAsideForACaptureAndComesBack()
    {
        RunSta(() =>
        {
            using var host = NewHost();
            var main = host.MainWindow;

            // not visible: nothing to hide, nothing to bring back
            host.HideMainWindowAsync().GetAwaiter().GetResult();
            host.RestoreMainWindow();
            Assert.False(main.Visible);

            host.ShowMain();
            Assert.True(main.Visible);
            var hide = host.HideMainWindowAsync();
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!hide.IsCompleted && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            Assert.True(hide.IsCompleted);
            Assert.False(main.Visible, "hidden so it cannot end up in the screenshot");

            host.RestoreMainWindow();
            Assert.True(main.Visible);
            host.RestoreMainWindow(); // second call: harmless
            Assert.True(main.Visible);
        });
    }

    [Fact]
    public void EditorShortcutsDoNotFireWhileAnotherPageIsInFront()
    {
        RunSta(() =>
        {
            using var host = NewHost();
            var a = AddPicture(host, "A", Color.IndianRed);
            var main = host.MainWindow;
            host.OpenInEditor(a.FilePath);
            DrawRectangle(main.Context);
            Assert.True(main.Document.IsModified);

            bool Press(Keys keys)
            {
                var method = typeof(EditorForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
                // a real key message: menu shortcuts are resolved from the window handle the message targets
                var args = new object[] { Message.Create(main.Handle, 0x0100, (IntPtr)(int)(keys & Keys.KeyCode), IntPtr.Zero), keys };
                return (bool)method.Invoke(main, args)!;
            }

            // library in front: Ctrl+S (menu item of the hidden editor) must not save behind the user's back
            host.ShowLibrary();
            Assert.False(Press(Keys.Control | Keys.S));
            Assert.True(main.Document.IsModified);

            // editor in front: it does
            main.ShowEditorPage();
            Assert.True(Press(Keys.Control | Keys.S));
            Assert.False(main.Document.IsModified);
        });
    }

    [Theory]
    [InlineData("en", "Light")]
    [InlineData("vi", "Dark")]
    public void CaptureAndLibraryAreReachableFromEveryPage(string language, string theme)
    {
        RunSta(() =>
        {
            using var host = NewHost(language, theme);
            var main = host.MainWindow;
            host.ShowLibrary();
            Pump(100);

            // menu bar: the Capture menu is always there and lists every mode + recording
            var capture = main.MainMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == Loc.T("&Capture"));
            Assert.True(capture.Visible);
            var entries = capture.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.Text).ToList();
            Assert.Contains(Loc.T("Record video"), entries);
            Assert.Contains(Loc.T("Scrolling area"), entries);
            Assert.Contains(Loc.T("Profiles"), entries);

            // the picture editor's own menus are hidden on the library page, and back when the editor is shown
            var file = main.MainMenuStrip.Items.OfType<ToolStripMenuItem>().Single(m => m.Text == Loc.T("&File"));
            Assert.False(file.Visible);
            main.ShowEditorPage();
            Assert.True(file.Visible);

            // the editor's toolbar has Capture and Library buttons; the library page has Capture and Back
            var editorButtons = main.Controls.OfType<ToolStrip>().SelectMany(s => s.Items.Cast<ToolStripItem>()).Select(i => i.Text).ToList();
            Assert.Contains(Loc.T("Capture"), editorButtons);
            Assert.Contains(Loc.T("Library"), editorButtons);

            var libraryButtons = AllControls(main.Library!).OfType<ToolStrip>().SelectMany(s => s.Items.Cast<ToolStripItem>()).Select(i => i.Text).ToList();
            Assert.Contains(Loc.T("Capture"), libraryButtons);
            Assert.Contains(Loc.T("Back"), libraryButtons);
        });
    }

    [Fact]
    public void TheBackButtonOfTheLibraryFollowsWhetherThereIsAnythingToGoBackTo()
    {
        RunSta(() =>
        {
            using var host = NewHost();
            var a = AddPicture(host, "A", Color.IndianRed);
            var main = host.MainWindow;

            host.ShowLibrary();
            Pump(100);
            var back = AllControls(main.Library!).OfType<ToolStrip>().SelectMany(s => s.Items.Cast<ToolStripItem>()).Single(i => i.Text == Loc.T("Back"));
            Assert.False(back.Enabled);

            host.OpenInEditor(a.FilePath);
            host.ShowLibrary();
            Pump(100);
            Assert.True(back.Enabled);
        });
    }
}

using System.Drawing.Imaging;
using SnapGit.Core.Hotkeys;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Settings;
using Windows.Data.Pdf;
using Windows.Storage;
using Xunit;

namespace SnapGit.Tests;

public class CoreFeatureTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-core-" + Guid.NewGuid().ToString("N"));

    public CoreFeatureTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Bitmap Solid(int w, int h, Color c)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(c);
        return bmp;
    }

    private FileLibraryService NewLibrary(Action<AppSettings>? configure = null)
    {
        var settings = new JsonSettingsService(Path.Combine(_dir, "settings.json"));
        settings.Current.LibraryFolder = Path.Combine(_dir, "lib");
        configure?.Invoke(settings.Current);
        return new FileLibraryService(settings, new LibraryIndex(Path.Combine(_dir, "index.json")));
    }

    // ------------------------------------------------------------ GIF

    [Fact]
    public void GifEncoderWritesAReadableAnimation()
    {
        var frames = new List<GifFrame>();
        for (var i = 0; i < 4; i++)
        {
            var bmp = Solid(64, 48, Color.White);
            using (var g = Graphics.FromImage(bmp)) g.FillRectangle(Brushes.Crimson, 4 + i * 10, 10, 16, 16); // moving square
            frames.Add(new GifFrame(bmp, 100));
        }

        var path = Path.Combine(_dir, "anim.gif");
        GifEncoder.Write(path, frames);
        foreach (var f in frames) f.Image.Dispose();

        using var gif = Image.FromFile(path);
        var dimension = new FrameDimension(gif.FrameDimensionsList[0]);
        Assert.Equal(4, gif.GetFrameCount(dimension));
        Assert.Equal(new Size(64, 48), gif.Size);

        using var first = new Bitmap(gif);
        Assert.Equal(Color.White.ToArgb(), first.GetPixel(60, 40).ToArgb());
        var inSquare = first.GetPixel(10, 15);
        Assert.True(inSquare.R > 180 && inSquare.G < 80, $"square colour was {inSquare}");
    }

    [Fact]
    public void GifFrameDifferencingKeepsFilesSmall()
    {
        var frames = Enumerable.Range(0, 20).Select(i =>
        {
            var bmp = Solid(400, 300, Color.FromArgb(240, 240, 245));
            using (var g = Graphics.FromImage(bmp)) g.FillEllipse(Brushes.SteelBlue, 10 + i * 5, 100, 30, 30);
            return new GifFrame(bmp, 80);
        }).ToList();

        var path = Path.Combine(_dir, "small.gif");
        GifEncoder.Write(path, frames);
        foreach (var f in frames) f.Image.Dispose();

        // 20 raw frames would be 20 * 400*300 = 2.4 MB of indices; only the moving region is stored.
        Assert.True(new FileInfo(path).Length < 150_000, $"gif is {new FileInfo(path).Length} bytes");
    }

    [Fact]
    public void GifRejectsEmptyInput() =>
        Assert.Throws<ArgumentException>(() => GifEncoder.Write(Path.Combine(_dir, "x.gif"), []));

    // ------------------------------------------------------------ PDF

    [Fact]
    public async Task PdfHasOnePagePerImageAndWindowsCanOpenIt()
    {
        using var a = Solid(300, 200, Color.Orange);
        using var b = Solid(150, 400, Color.SteelBlue);
        var path = Path.Combine(_dir, "doc.pdf");

        PdfWriter.Write(path, [a, b]);

        var head = File.ReadAllBytes(path).AsSpan(0, 5).ToArray();
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(head));

        // Ask Windows' own PDF renderer: proves the file structure (xref, objects) is valid.
        var file = await StorageFile.GetFileFromPathAsync(path);
        var pdf = await PdfDocument.LoadFromFileAsync(file);
        Assert.Equal(2u, pdf.PageCount);
        using var page0 = pdf.GetPage(0);
        Assert.Equal(300.0, page0.Size.Width, 1);   // WinRT reports 1/96-inch units: 300 px page = 225 pt in the file
        Assert.Equal(200.0, page0.Size.Height, 1);
    }

    // ------------------------------------------------------------ composer

    [Fact]
    public void CombineLayoutsComputeTheRightSize()
    {
        using var a = Solid(100, 50, Color.Red);
        using var b = Solid(80, 70, Color.Green);
        using var c = Solid(60, 60, Color.Blue);
        var images = new List<Bitmap> { a, b, c };

        using var vertical = ImageComposer.Combine(images, CombineLayout.Vertical, 10, Color.White);
        Assert.Equal(new Size(100, 50 + 70 + 60 + 20), vertical.Size);

        using var horizontal = ImageComposer.Combine(images, CombineLayout.Horizontal, 10, Color.White);
        Assert.Equal(new Size(100 + 80 + 60 + 20, 70), horizontal.Size);

        using var grid = ImageComposer.Combine(images, CombineLayout.Grid, 4, Color.White, gridColumns: 2);
        Assert.Equal(new Size(100 + 80 + 4, 70 + 60 + 4), grid.Size);
        Assert.Equal(Color.Blue.ToArgb(), grid.GetPixel(5, 70 + 4 + 5).ToArgb()); // third image on the second row
    }

    [Fact]
    public void StepDocumentContainsEveryImageScaledToThePage()
    {
        using var wide = Solid(2000, 400, Color.Teal);
        using var small = Solid(300, 200, Color.Crimson);
        using var doc = ImageComposer.BuildStepDocument([(wide, "Open the file"), (small, "Click Save")], "How to save", pageWidth: 800);

        Assert.Equal(800, doc.Width);
        Assert.True(doc.Height > 400 + 200);
        var teal = Enumerable.Range(0, doc.Height).Count(y => doc.GetPixel(400, y).ToArgb() == Color.Teal.ToArgb());
        Assert.InRange(teal, 100, 160); // 2000x400 scaled to 720 wide => 144 rows
    }

    // ------------------------------------------------------------ names

    [Theory]
    [InlineData("SnapGit {yyyy-MM-dd HH-mm-ss}", "SnapGit 2026-10-03 14-05-09")]
    [InlineData("{title} - {counter:000}", "Notepad - 007")]
    [InlineData("shot_{counter}", "shot_7")]
    [InlineData("a{unclosed", "a{unclosed")]
    [InlineData("", "Notepad 2026-10-03 14-05-09")]
    [InlineData("bad:name?*", "badname")]
    public void NamePatternExpandsTokens(string pattern, string expected)
    {
        var now = new DateTime(2026, 10, 3, 14, 5, 9);
        Assert.Equal(expected, NamePattern.Format(pattern, now, "Notepad", 7));
    }

    [Fact]
    public void NamePatternFlattensMultilineTitles()
    {
        Assert.Equal("first line second line", NamePattern.Format("{title}", DateTime.Now, "first line\r\nsecond\t line"));
        Assert.True(NamePattern.Format("{title}", DateTime.Now, new string('x', 500)).Length <= 120);
    }

    [Fact]
    public void NamePatternFallsBackWhenTitleMissing()
    {
        Assert.Equal("SnapGit", NamePattern.Format("{title}", DateTime.Now));
        Assert.Equal("capture", NamePattern.Format("???", DateTime.Now)); // nothing usable left after sanitising
    }

    // ------------------------------------------------------------ library

    [Fact]
    public void LibraryStoresProjectsOrImagesPerSetting()
    {
        using var bmp = Solid(40, 30, Color.Red);

        var projects = NewLibrary(s => s.SaveCapturesAsProject = true);
        var project = projects.Add(bmp, "My Window");
        Assert.Equal(LibraryItemKind.Project, project.Kind);
        Assert.StartsWith("My Window", project.Name);
        Assert.Equal(Color.Red.ToArgb(), SnapProjectFile.Read(project.FilePath).Image.GetPixel(1, 1).ToArgb());

        var images = NewLibrary(s => { s.SaveCapturesAsProject = false; s.DefaultFormat = ImageFileFormat.Jpeg; s.LibraryFolder = Path.Combine(_dir, "lib2"); });
        var image = images.Add(bmp);
        Assert.Equal(LibraryItemKind.Image, image.Kind);
        Assert.EndsWith(".jpg", image.FilePath);
    }

    [Fact]
    public void LibraryQueryFiltersAndSorts()
    {
        var library = NewLibrary();
        using var bmp = Solid(10, 10, Color.Red);
        var a = library.Add(bmp, "alpha invoice");
        var b = library.Add(bmp, "beta report");
        var c = library.Add(bmp, "gamma");
        library.SetTags(a, ["work", "money"]);
        library.SetTags(b, ["work"]);
        library.SetFavorite(c, true);

        Assert.Equal(3, library.GetItems().Count);
        Assert.Equal(2, library.GetItems(new LibraryQuery { Tag = "work" }).Count);
        Assert.Equal(["gamma"], library.GetItems(new LibraryQuery { FavoritesOnly = true }).Select(i => i.Name.Split(' ')[0]));
        Assert.Single(library.GetItems(new LibraryQuery { Text = "MONEY" }));          // matches a tag
        Assert.Single(library.GetItems(new LibraryQuery { Text = "report" }));         // matches a name
        Assert.Equal(["alpha", "beta", "gamma"], library.GetItems(new LibraryQuery { Sort = LibrarySort.Name, Descending = false }).Select(i => i.Name.Split(' ')[0]));
        Assert.Equal(["money", "work"], library.AllTags());
    }

    [Fact]
    public void LibraryRenameDeleteImportAndMetadataFollowTheFile()
    {
        var library = NewLibrary();
        using var bmp = Solid(10, 10, Color.Red);
        var item = library.Add(bmp, "first");
        library.SetTags(item, ["x"]);

        var renamed = library.Rename(item, "second");
        Assert.False(File.Exists(item.FilePath));
        Assert.True(File.Exists(renamed.FilePath));
        Assert.Equal(["x"], renamed.Tags); // tags moved with the file

        var other = library.Add(bmp, "third");
        Assert.Throws<IOException>(() => library.Rename(other, "second"));

        var external = Path.Combine(_dir, "outside.png");
        bmp.Save(external, ImageFormat.Png);
        var imported = library.Import(external);
        Assert.True(File.Exists(external)); // import copies
        Assert.StartsWith("outside", imported.Name);

        library.Delete(renamed);
        Assert.DoesNotContain("x", library.AllTags());
    }

    [Fact]
    public void LibraryIndexSurvivesRestart()
    {
        var indexPath = Path.Combine(_dir, "index.json");
        var first = new LibraryIndex(indexPath);
        first.SetTags("a.png", ["one", "Two"]);
        first.SetFavorite("a.png", true);

        var second = new LibraryIndex(indexPath);
        Assert.Equal(["one", "Two"], second.TagsOf("a.PNG")); // file names are case-insensitive
        Assert.True(second.IsFavorite("a.png"));
    }

    [Fact]
    public void ReservedPathsNeverCollide()
    {
        var library = NewLibrary(s => s.NamePattern = "fixed");
        var first = library.ReservePath(".gif");
        File.WriteAllText(first, "x");
        var second = library.ReservePath(".gif");
        Assert.NotEqual(first, second);
        Assert.EndsWith("fixed (2).gif", second);
    }

    // ------------------------------------------------------------ batch + settings

    [Fact]
    public void BatchConvertResizesConvertsAndReportsFailures()
    {
        var input = Path.Combine(_dir, "in");
        Directory.CreateDirectory(input);
        using (var big = Solid(400, 200, Color.Red)) big.Save(Path.Combine(input, "a.png"), ImageFormat.Png);
        File.WriteAllText(Path.Combine(input, "broken.png"), "not an image");

        var progress = new List<int>();
        var result = BatchConverter.Run(
            [Path.Combine(input, "a.png"), Path.Combine(input, "broken.png")],
            new BatchOptions(Path.Combine(_dir, "out"), ImageFileFormat.Jpeg, 80, MaxWidth: 100),
            ImageEncoder.LoadUnlocked,
            new Progress<(int Done, int Total)>(p => progress.Add(p.Done)));

        Assert.Single(result.Converted);
        Assert.Single(result.Failed);
        using var converted = new Bitmap(result.Converted[0]);
        Assert.Equal(new Size(100, 50), converted.Size);
        Assert.EndsWith(".jpg", result.Converted[0]);
    }

    [Fact]
    public void FixedRegionSizeParses()
    {
        Assert.Equal(new Size(1280, 720), new AppSettings { FixedRegionSize = "1280x720" }.GetFixedRegionSize());
        Assert.Equal(new Size(800, 600), new AppSettings { FixedRegionSize = "800 × 600" }.GetFixedRegionSize());
        Assert.Null(new AppSettings { FixedRegionSize = "abc" }.GetFixedRegionSize());
        Assert.Null(new AppSettings().GetFixedRegionSize());
    }

    [Fact]
    public void ProfilesOverrideOnlyTheirOwnOptions()
    {
        var settings = new AppSettings
        {
            CaptureDelaySeconds = 3, IncludeCursor = true, CopyToClipboardAfterCapture = false, OpenEditorAfterCapture = true,
            FixedRegionSize = "800x600", UsePrintWindowForWindows = true,
        };

        var global = SnapGit.Core.Settings.CaptureOptions.Create(settings);
        Assert.Equal(3, global.DelaySeconds);
        Assert.Equal(new Size(800, 600), global.FixedRegion);
        Assert.True(global.UsePrintWindow);

        var profile = new CaptureProfile { Name = "Docs", Mode = SnapGit.Core.Models.CaptureMode.Region, DelaySeconds = 0, FixedRegionSize = "1280x720", IncludeCursor = false, CopyToClipboard = true, OpenEditor = false };
        var options = SnapGit.Core.Settings.CaptureOptions.Create(settings, profile);
        Assert.Equal(0, options.DelaySeconds);
        Assert.Equal(new Size(1280, 720), options.FixedRegion);
        Assert.False(options.IncludeCursor);
        Assert.True(options.CopyToClipboard);
        Assert.False(options.OpenEditor);
        Assert.True(options.UsePrintWindow);                     // not part of a profile: inherited

        Assert.Null(SnapGit.Core.Settings.CaptureOptions.Create(settings, new CaptureProfile { FixedRegionSize = "nonsense" }).FixedRegion);
        Assert.Equal(0, SnapGit.Core.Settings.CaptureOptions.Create(settings, new CaptureProfile { DelaySeconds = -5 }).DelaySeconds);
    }

    [Fact]
    public void ProfilesSurviveTheSettingsFile()
    {
        var path = Path.Combine(_dir, "profiles.json");
        var first = new JsonSettingsService(path);
        first.Current.Profiles.Add(new CaptureProfile { Name = "Slide", Mode = SnapGit.Core.Models.CaptureMode.Scrolling, DelaySeconds = 2, CopyToClipboard = true });
        first.Save();

        var second = new JsonSettingsService(path);
        second.Load();
        var profile = Assert.Single(second.Current.Profiles);
        Assert.Equal("Slide", profile.Name);
        Assert.Equal(SnapGit.Core.Models.CaptureMode.Scrolling, profile.Mode);   // enums are stored by name
        Assert.True(profile.CopyToClipboard);
        Assert.Contains("\"Scrolling\"", File.ReadAllText(path));
    }

    [Fact]
    public void EveryHotkeyActionHasAUniqueDefault()
    {
        var defaults = AppSettings.DefaultHotkeys();
        foreach (var action in Enum.GetValues<HotkeyAction>()) Assert.True(defaults.ContainsKey(action), $"{action} has no default");
        Assert.Equal(defaults.Count, defaults.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OldSettingsFilesStillLoadWithNewDefaults()
    {
        var path = Path.Combine(_dir, "old.json");
        File.WriteAllText(path, "{\"JpegQuality\": 70, \"Hotkeys\": {\"CaptureRegion\": \"Ctrl+Alt+1\"}}");
        var service = new JsonSettingsService(path);
        service.Load();

        Assert.Equal(70, service.Current.JpegQuality);
        Assert.True(service.Current.SaveCapturesAsProject);          // new property: default applies
        Assert.Contains(HotkeyAction.GrabText, service.Current.GetGestures().Keys); // missing hotkeys fall back to defaults
    }
}

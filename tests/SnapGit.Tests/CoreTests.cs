using SnapGit.Core.Hotkeys;
using SnapGit.Core.Imaging;
using SnapGit.Core.Library;
using SnapGit.Core.Settings;
using Xunit;

namespace SnapGit.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("PrintScreen", HotkeyModifiers.None, Keys.PrintScreen)]
    [InlineData("Alt+PrintScreen", HotkeyModifiers.Alt, Keys.PrintScreen)]
    [InlineData("Ctrl+Shift+S", HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.S)]
    [InlineData("control + win + F5", HotkeyModifiers.Control | HotkeyModifiers.Win, Keys.F5)]
    [InlineData("Ctrl+Alt+1", HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.D1)]  // digit key, not Keys value 1
    [InlineData("Ctrl+D3", HotkeyModifiers.Control, Keys.D3)]
    public void Parses(string text, HotkeyModifiers modifiers, Keys key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Shift")]
    [InlineData("A+B")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("Ctrl+12")]    // numeric strings must not be read as Keys values
    [InlineData("Ctrl+999999")]
    public void RejectsInvalid(string? text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData(Keys.F12)]
    [InlineData(Keys.D1)]
    [InlineData(Keys.PrintScreen)]
    [InlineData(Keys.Oemtilde)]
    public void RoundTripsThroughToString(Keys key)
    {
        var original = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, key);
        Assert.True(HotkeyGesture.TryParse(original.ToString(), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void DefaultHotkeysAreAllParsable()
    {
        foreach (var (action, text) in new AppSettings().Hotkeys)
            Assert.True(HotkeyGesture.TryParse(text, out var g) && g.IsValid, $"{action} default '{text}' is not a valid gesture");
    }
}

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snapgit-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void DefaultsProvideAGestureForEveryAction()
    {
        var gestures = new AppSettings().GetGestures();
        foreach (var action in Enum.GetValues<HotkeyAction>())
            Assert.True(gestures.ContainsKey(action), $"{action} has no default hotkey");
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var path = Path.Combine(_dir, "settings.json");
        var first = new JsonSettingsService(path);
        first.Current.JpegQuality = 55;
        first.Current.DefaultFormat = ImageFileFormat.Jpeg;
        first.Current.Hotkeys[HotkeyAction.CaptureRegion] = "Ctrl+Shift+X";
        first.Save();

        var second = new JsonSettingsService(path);
        second.Load();

        Assert.Equal(55, second.Current.JpegQuality);
        Assert.Equal(ImageFileFormat.Jpeg, second.Current.DefaultFormat);
        Assert.Equal("Ctrl+Shift+X", second.Current.Hotkeys[HotkeyAction.CaptureRegion]);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ this is not json");

        var service = new JsonSettingsService(path);
        service.Load();

        Assert.Equal(90, service.Current.JpegQuality);
    }

    [Fact]
    public void EmptyHotkeyMeansUnbound()
    {
        var settings = new AppSettings();
        settings.Hotkeys[HotkeyAction.OpenLibrary] = "";
        Assert.False(settings.GetGestures().ContainsKey(HotkeyAction.OpenLibrary));
    }

    [Fact]
    public void LibraryAddListDelete()
    {
        var settings = new JsonSettingsService(Path.Combine(_dir, "settings.json"));
        settings.Current.LibraryFolder = Path.Combine(_dir, "lib");
        var library = new FileLibraryService(settings);

        using var bmp = new Bitmap(10, 10);
        var changes = 0;
        library.Changed += (_, _) => changes++;

        var a = library.Add(bmp, "My: window?");
        var b = library.Add(bmp, "My: window?"); // same name must not overwrite

        Assert.NotEqual(a.FilePath, b.FilePath);
        Assert.Equal(2, library.GetItems().Count);

        library.Delete(a);
        Assert.Single(library.GetItems());
        Assert.Equal(3, changes);
    }

    [Fact]
    public void LoadUnlockedReleasesTheFile()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "x.png");
        using (var bmp = new Bitmap(4, 4)) ImageEncoder.Save(bmp, path, ImageFileFormat.Png);

        using var loaded = ImageEncoder.LoadUnlocked(path);
        File.Delete(path); // would throw if the file were still locked

        Assert.Equal(4, loaded.Width);
    }
}

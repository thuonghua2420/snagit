using SnapGit.App.Ui;
using Xunit;

namespace SnapGit.Tests;

/// <summary>Not a pass/fail check of looks (that needs eyes): renders every icon, labelled, to a PNG when SNAPGIT_SHOTS is set.</summary>
public class IconSheetTests
{
    [Fact]
    public void EveryIconRendersAndHasInk()
    {
        foreach (var icon in Enum.GetValues<UiIcon>())
        {
            using var light = new Bitmap(Icons.Get(icon, Color.FromArgb(55, 55, 60), 64));
            Assert.True(CountInk(light) > 30, $"{icon} drew (almost) nothing in the light theme");
            using var dark = new Bitmap(Icons.Get(icon, Color.FromArgb(225, 225, 230), 64));
            Assert.True(CountInk(dark) > 30, $"{icon} drew (almost) nothing in the dark theme");
        }

        var dir = Environment.GetEnvironmentVariable("SNAPGIT_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        Save(Path.Combine(dir, "icons-light.png"), Color.FromArgb(55, 55, 60), Color.White);
        Save(Path.Combine(dir, "icons-dark.png"), Color.FromArgb(225, 225, 230), Color.FromArgb(40, 40, 44));
    }

    private static int CountInk(Bitmap bmp)
    {
        var count = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).A > 40) count++;
        return count;
    }

    private static void Save(string path, Color fg, Color background)
    {
        var icons = Enum.GetValues<UiIcon>();
        const int cell = 96, columns = 12;
        var rowHeight = 60;
        var rows = (icons.Length + columns - 1) / columns;
        using var sheet = new Bitmap(columns * cell, rows * rowHeight);
        using var g = Graphics.FromImage(sheet);
        g.Clear(background);
        using var font = new Font("Segoe UI", 7.5f);
        using var textBrush = new SolidBrush(fg);
        for (var i = 0; i < icons.Length; i++)
        {
            int x = i % columns * cell, y = i / columns * rowHeight;
            // the real toolbar size (16 px, pixel for pixel), the same icon at 2× density (32 px) and its disabled state
            g.DrawImage(Icons.Get(icons[i], null, 16), x + 8, y + 8, 16, 16);
            g.DrawImage(Icons.Get(icons[i], null, 32), x + 30, y + 4, 32, 32);
            using (var grey = Icons.Disabled(Icons.Get(icons[i], null, 16))) g.DrawImage(grey, x + 70, y + 8, 16, 16);
            g.DrawString(icons[i].ToString(), font, textBrush, new RectangleF(x, y + 40, cell, 16), new StringFormat { Alignment = StringAlignment.Center });
        }
        sheet.Save(path);
    }
}

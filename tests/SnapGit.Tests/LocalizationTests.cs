using System.Text.RegularExpressions;
using SnapGit.Core.Localization;
using Xunit;

namespace SnapGit.Tests;

/// <summary>
/// Keeps the Vietnamese table honest: scans the real source tree for every Loc.T("…") / Loc.N("…") literal and fails
/// if one has no translation or if a translation dropped/added a {placeholder}.
/// </summary>
public class LocalizationTests
{
    private static readonly Regex Literal = new(@"Loc\.[TN]\(""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SnapGit.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SnapGit.sln not found above the test assembly.");
    }

    private static string Unescape(string literal) =>
        literal.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");

    private static IEnumerable<(string Key, string File)> UsedKeys()
    {
        var src = Path.Combine(RepoRoot(), "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                Path.GetFileName(file) is "Vietnamese.cs" or "Loc.cs")
                continue;

            foreach (var line in File.ReadLines(file))
            {
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue; // docs/comments
                foreach (Match match in Literal.Matches(line))
                    yield return (Unescape(match.Groups[1].Value), Path.GetFileName(file));
            }
        }
    }

    [Fact]
    public void EveryTranslatableStringHasAVietnameseEntry()
    {
        var used = UsedKeys().ToList();
        Assert.True(used.Count > 100, $"scan found only {used.Count} strings — the regex or the path is wrong");

        var missing = used.Where(u => !Vietnamese.Table.ContainsKey(u.Key)).Select(u => $"{u.File}: \"{u.Key}\"").Distinct().ToList();
        Assert.True(missing.Count == 0, "Missing Vietnamese translations:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void TranslationsKeepTheSamePlaceholders()
    {
        var broken = new List<string>();
        foreach (var (english, vietnamese) in Vietnamese.Table)
        {
            var a = Placeholder.Matches(english).Select(m => m.Value).OrderBy(x => x).ToList();
            var b = Placeholder.Matches(vietnamese).Select(m => m.Value).OrderBy(x => x).ToList();
            if (!a.SequenceEqual(b)) broken.Add($"\"{english}\" -> \"{vietnamese}\"");
        }
        Assert.True(broken.Count == 0, "Placeholder mismatch:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void LanguageSwitchTranslatesAndFallsBackToEnglish()
    {
        try
        {
            Loc.SetLanguage("vi");
            Assert.Equal("Lưu", Loc.T("Save"));
            Assert.Equal("Đã lưu bản_ghi", Loc.T("Saved {0}", "bản_ghi"));
            Assert.Equal("never translated text", Loc.T("never translated text"));

            Loc.SetLanguage("en");
            Assert.Equal("Save", Loc.T("Save"));
            Assert.Equal("Saved x", Loc.T("Saved {0}", "x"));
        }
        finally
        {
            Loc.SetLanguage("en");
        }
    }

    [Fact]
    public void TranslationsAreNotEmptyAndActuallyDiffer()
    {
        foreach (var (english, vietnamese) in Vietnamese.Table)
        {
            Assert.False(string.IsNullOrWhiteSpace(vietnamese), $"empty translation for \"{english}\"");
            if (english.Length > 12) Assert.NotEqual(english, vietnamese);
        }
    }
}

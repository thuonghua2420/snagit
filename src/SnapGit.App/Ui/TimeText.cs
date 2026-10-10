using System.Globalization;

namespace SnapGit.App.Ui;

public static class TimeText
{
    /// <summary>"3.2" → <c>0:03.2</c>, "75" → <c>1:15.0</c>; hours only when needed.</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        var tenths = (int)(time.Milliseconds / 100);
        var text = time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
        return text + "." + tenths.ToString(CultureInfo.InvariantCulture);
    }
}

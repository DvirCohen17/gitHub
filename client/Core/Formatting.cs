using System.Globalization;

namespace GitHubDemo.Core;

public static class Formatting
{
    // "just now", "5 min ago", "yesterday", "12 Mar"...
    public static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalSeconds < 45) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24 && time.Date == DateTime.Today) return $"{(int)span.TotalHours} h ago";
        if (time.Date == DateTime.Today.AddDays(-1)) return "yesterday";
        if (span.TotalDays < 7) return time.ToString("dddd", CultureInfo.CurrentCulture);
        return time.ToString(time.Year == DateTime.Now.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentCulture);
    }

    // Chat timestamps: "14:05", "Yesterday 14:05", "12 Mar 14:05".
    public static string MessageTime(DateTime time)
    {
        string clock = time.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (time.Date == DateTime.Today) return clock;
        if (time.Date == DateTime.Today.AddDays(-1)) return "Yesterday " + clock;
        return time.ToString(time.Year == DateTime.Now.Year ? "d MMM " : "d MMM yyyy ", CultureInfo.CurrentCulture) + clock;
    }

    public static string Size(long chars) => chars < 1000 ? $"{chars} chars" : $"{chars / 1000.0:0.#}k chars";
}

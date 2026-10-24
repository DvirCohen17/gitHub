using System.Windows;
using System.Windows.Media;

namespace GitHubDemo.Core;

// Colour themes. Every style in Themes/Design.xaml reads Theme.* brushes with
// DynamicResource, so applying a theme restyles the whole app at once.
public static class ThemeManager
{
    public sealed record Palette(string Name, Color Background, Color Text, Color Accent, bool IsDark);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly IReadOnlyList<Palette> Palettes = new[]
    {
        new Palette("Dark", C("#16181D"), C("#E6E8EB"), C("#3B82F6"), true),
        new Palette("Light", C("#F6F7F9"), C("#1F2328"), C("#2563EB"), false),
        new Palette("Midnight", C("#0F172A"), C("#E2E8F0"), C("#818CF8"), true),
        new Palette("Forest", C("#0F1A14"), C("#DCEFE3"), C("#34D399"), true),
        new Palette("Blue", C("#EAF2FF"), C("#0B2545"), C("#1D4ED8"), false),
        new Palette("Green", C("#ECF7EE"), C("#0F3D1E"), C("#15803D"), false),
        new Palette("Red", C("#FDF0F0"), C("#4A1010"), C("#B91C1C"), false),
        new Palette("CyberPunk", C("#0A0032"), C("#00FFFF"), C("#FF1493"), true),
        new Palette("Matrix", C("#000000"), C("#00FF41"), C("#00B140"), true),
        new Palette("Solarized Light", C("#FDF6E3"), C("#586E75"), C("#268BD2"), false),
        new Palette("Solarized Dark", C("#002B36"), C("#93A1A1"), C("#2AA198"), true),
        new Palette("Vintage", C("#F4E3E3"), C("#6C4F4F"), C("#8B5E3C"), false),
        new Palette("Neon", C("#000000"), C("#39FF14"), C("#FF007F"), true),
        new Palette("Pastel", C("#FBE8EB"), C("#6B4A5A"), C("#B57EDC"), false),
    };

    public static IEnumerable<string> Names => Palettes.Select(p => p.Name);

    public static Palette Current { get; private set; } = Palettes[0];

    public static event Action? Changed;

    public static Palette Find(string name) =>
        Palettes.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Palettes[0];

    public static void Apply(string name)
    {
        var p = Find(name);
        Current = p;
        var r = Application.Current.Resources;
        Color bg = p.Background, text = p.Text, accent = p.Accent;
        bool lightAccent = Luma(accent) > 160;

        r["Theme.WindowBackground"] = Solid(bg);
        r["Theme.Foreground"] = Solid(text);
        r["Theme.Muted"] = Solid(Blend(text, bg, 0.40));
        r["Theme.Faint"] = Solid(Blend(text, bg, 0.62));
        r["Theme.Surface"] = Solid(Blend(bg, text, 0.045));
        r["Theme.SurfaceAlt"] = Solid(Blend(bg, text, p.IsDark ? 0.025 : 0.03));
        r["Theme.SurfaceHover"] = Solid(Blend(bg, text, 0.09));
        r["Theme.Border"] = Solid(Blend(bg, text, 0.14));
        r["Theme.ButtonBackground"] = Solid(Blend(bg, text, 0.06));
        r["Theme.ButtonForeground"] = Solid(text);
        r["Theme.ButtonBorder"] = Solid(Blend(bg, text, 0.20));
        r["Theme.ButtonHover"] = Solid(Blend(bg, text, 0.12));
        r["Theme.InputBackground"] = Solid(Blend(bg, text, p.IsDark ? 0.035 : 0.0));
        r["Theme.InputForeground"] = Solid(text);
        r["Theme.InputBorder"] = Solid(Blend(bg, text, 0.20));
        r["Theme.Accent"] = Solid(accent);
        r["Theme.AccentHover"] = Solid(Blend(accent, lightAccent ? Colors.Black : Colors.White, 0.14));
        r["Theme.AccentForeground"] = Solid(lightAccent ? Colors.Black : Colors.White);
        r["Theme.AccentSoft"] = Solid(Blend(bg, accent, p.IsDark ? 0.22 : 0.14));
        r["Theme.AccentText"] = Solid(Contrast(accent, bg) < 3 ? Blend(accent, text, 0.45) : accent);
        r["Theme.Danger"] = Solid(C(p.IsDark ? "#F2555A" : "#D93036"));
        r["Theme.DangerSoft"] = Solid(Blend(bg, C("#E5484D"), 0.16));
        r["Theme.Success"] = Solid(C(p.IsDark ? "#3DD68C" : "#1E8E5A"));
        r["Theme.Warning"] = Solid(C(p.IsDark ? "#F5B342" : "#B26B00"));
        r["Theme.Online"] = Solid(C("#30A46C"));
        r["Theme.Away"] = Solid(C("#F5A524"));
        r["Theme.Offline"] = Solid(Blend(text, bg, 0.55));
        r["Theme.EditorBackground"] = Solid(Blend(bg, p.IsDark ? Colors.Black : Colors.White, 0.25));
        r["Theme.EditorSelection"] = Solid(Color.FromArgb(0x55, accent.R, accent.G, accent.B));
        r["Theme.Overlay"] = Solid(Color.FromArgb(0x99, 0, 0, 0));
        r["Theme.ShadowColor"] = p.IsDark ? Colors.Black : Color.FromRgb(0x30, 0x34, 0x3C);
        r["Theme.AccentColor"] = accent;
        CodeStyles.Invalidate(); // syntax colours depend on light/dark
        Changed?.Invoke();
    }

    public static Brush AccentBrush(string theme) => Solid(Find(theme).Accent);

    // A stable, readable colour for a user (initials avatars, cursors).
    public static Color UserColor(string name)
    {
        string[] palette = { "#E5484D", "#F76B15", "#FFB224", "#30A46C", "#12A594", "#0090FF", "#3E63DD", "#8E4EC6", "#D6409F", "#978365" };
        int hash = 17;
        foreach (char ch in name.ToLowerInvariant()) hash = unchecked(hash * 31 + ch);
        return C(palette[(int)((uint)hash % palette.Length)]);
    }

    public static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));

    private static double Luma(Color c) => c.R * 0.299 + c.G * 0.587 + c.B * 0.114;

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Ch(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}

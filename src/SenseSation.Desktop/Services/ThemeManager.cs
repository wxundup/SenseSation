using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace SenseSation.Desktop.Services;

/// <summary>
/// Runtime theming. Updates the accent brushes in Application.Resources so every
/// control bound with DynamicResource recolors instantly — no restart.
/// </summary>
public static class ThemeManager
{
    public sealed record Preset(string Name, string Accent, string Accent2);

    public static readonly Preset[] Presets =
    [
        new("Valorant Red", "#FF4655", "#FF6B75"),
        new("Crimson",      "#E11D48", "#FB7185"),
        new("Midnight Blue","#3B82F6", "#60A5FA"),
        new("Cyber Purple", "#7C5CFF", "#9D7CFF"),
        new("Emerald",      "#10B981", "#34D399"),
        new("Arctic White", "#D7DDE8", "#FFFFFF"),
        new("Obsidian",     "#64748B", "#94A3B8"),
    ];

    private static readonly Dictionary<string, (string a, string a2)> RankThemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Iron"] = ("#6B7280", "#C7CDD4"),
        ["Bronze"] = ("#B0793C", "#E0A567"),
        ["Silver"] = ("#9AA4AD", "#E4E8EC"),
        ["Gold"] = ("#C9A227", "#F5C451"),
        ["Platinum"] = ("#22C7D6", "#34E0D0"),
        ["Diamond"] = ("#7AA2F7", "#3B82F6"),
        ["Ascendant"] = ("#1F9D55", "#36D67A"),
        ["Immortal"] = ("#FF4655", "#B11226"),
        ["Radiant"] = ("#F5C451", "#FF8A3D"),
    };

    public static string Current { get; private set; } = "Valorant Red";

    public static void ApplyPreset(string name)
    {
        var p = Presets.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? Presets[0];
        Current = p.Name;
        Apply(p.Accent, p.Accent2);
    }

    public static void ApplyRank(string division)
    {
        if (RankThemes.TryGetValue(division ?? "", out var c)) Apply(c.a, c.a2);
        else Apply(Presets[0].Accent, Presets[0].Accent2);
    }

    private static void Apply(string accentHex, string accent2Hex)
    {
        var res = Application.Current?.Resources;
        if (res is null) return;
        var a = Color.Parse(accentHex);
        var a2 = Color.Parse(accent2Hex);

        res["AccentColor"] = a;
        res["AccentBrush"] = new SolidColorBrush(a);
        res["Accent2Brush"] = new SolidColorBrush(a2);
        res["AccentGrad"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(a, 0),
                new GradientStop(Darken(a, 0.78), 1),
            }
        };
    }

    private static Color Darken(Color c, double f) =>
        Color.FromArgb(c.A, (byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
}

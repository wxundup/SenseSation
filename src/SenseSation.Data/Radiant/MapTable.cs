namespace SenseSation.Data.Radiant;

/// <summary>Maps Valorant internal map codenames (the MapId path segment) to display names.</summary>
public static class MapTable
{
    private static readonly Dictionary<string, string> Maps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ascent"] = "Ascent",
        ["Bonsai"] = "Split",
        ["Canyon"] = "Fracture",
        ["Duality"] = "Bind",
        ["Foxtrot"] = "Breeze",
        ["Jam"] = "Lotus",
        ["Juliett"] = "Sunset",
        ["Infinity"] = "Abyss",
        ["Pitt"] = "Pearl",
        ["Port"] = "Icebox",
        ["Rook"] = "Corrode",
        ["Triad"] = "Haven",
        ["HURM_Alley"] = "District",
        ["HURM_Bowl"] = "Kasbah",
        ["HURM_Helix"] = "Drift",
        ["HURM_Yard"] = "Piazza",
        ["HURM_HighTide"] = "Glitch",
        ["Range"] = "The Range",
    };

    /// <summary>Display name for a MapId path or codename; falls back to the codename if unknown.</summary>
    public static string Display(string? mapIdOrName)
    {
        if (string.IsNullOrEmpty(mapIdOrName)) return "";
        var seg = mapIdOrName.TrimEnd('/').Split('/').Last();
        return Maps.TryGetValue(seg, out var name) ? name : seg;
    }
}

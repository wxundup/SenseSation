namespace SenseSation.Core.Models;

/// <summary>Maps Valorant competitive tier numbers (0-27) to display names.</summary>
public static class RankTable
{
    private static readonly string[] Names =
    [
        "Unranked",
        "Unused 1", "Unused 2",
        "Iron 1", "Iron 2", "Iron 3",
        "Bronze 1", "Bronze 2", "Bronze 3",
        "Silver 1", "Silver 2", "Silver 3",
        "Gold 1", "Gold 2", "Gold 3",
        "Platinum 1", "Platinum 2", "Platinum 3",
        "Diamond 1", "Diamond 2", "Diamond 3",
        "Ascendant 1", "Ascendant 2", "Ascendant 3",
        "Immortal 1", "Immortal 2", "Immortal 3",
        "Radiant"
    ];

    public static string NameFor(int tier) =>
        tier >= 0 && tier < Names.Length ? Names[tier] : "Unknown";

    public static Rank Make(int tier, int rr) => new(tier, NameFor(tier), rr);
}

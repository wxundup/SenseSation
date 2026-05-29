namespace SenseSation.Core.Models;

/// <summary>
/// The current pre-game/in-game lobby as read from the LOCAL Riot client API.
/// Contains only account metadata (names, agents, ranks) — the same information
/// surfaced by mainstream in-client trackers. No live positions, economy, or
/// any state that would confer an unfair in-match advantage.
/// </summary>
public sealed record LiveLobby
{
    public required string MatchId { get; init; }
    public string Map { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Phase { get; init; } = ""; // PREGAME / INGAME
    public IReadOnlyList<PlayerSummary> Allies { get; init; } = [];
    public IReadOnlyList<PlayerSummary> Enemies { get; init; } = [];

    public double AverageAllyTier => Allies.Count == 0 ? 0 : Allies.Average(p => p.Rank.Tier);
    public double AverageEnemyTier => Enemies.Count == 0 ? 0 : Enemies.Average(p => p.Rank.Tier);
}

/// <summary>Snapshot of whether the local Riot client looks reachable, for diagnostics.</summary>
public sealed record LiveEnvironment
{
    public bool ValorantRunning { get; init; }
    public bool RiotClientRunning { get; init; }
    public bool LockfileExists { get; init; }
    public string LockfilePath { get; init; } = "";

    /// <summary>The client should be reachable when the game/client is up and the lockfile is present.</summary>
    public bool LooksReachable => LockfileExists && (ValorantRunning || RiotClientRunning);

    /// <summary>Human-readable status for the UI.</summary>
    public string Describe()
    {
        if (!ValorantRunning && !RiotClientRunning) return "Valorant / Riot Client not running.";
        if (!LockfileExists) return "Riot Client running, but lockfile not found yet — wait for it to finish starting.";
        return ValorantRunning ? "Valorant detected." : "Riot Client detected (game not in foreground yet).";
    }
}

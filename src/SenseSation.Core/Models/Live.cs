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

/// <summary>
/// A lobby player's public career snapshot: current rank plus recent competitive
/// matches. Same account metadata in-client trackers (Tracker.gg, Blitz) show.
/// </summary>
public sealed record PlayerCareer
{
    public required string Puuid { get; init; }
    public string Name { get; init; } = "";
    public string Tag { get; init; } = "";
    public Rank Rank { get; init; } = Rank.Unranked;
    public IReadOnlyList<MatchSummary> Recent { get; init; } = [];

    public string DisplayName => string.IsNullOrEmpty(Tag) ? (string.IsNullOrEmpty(Name) ? "Player" : Name) : $"{Name}#{Tag}";
    public int Wins => Recent.Count(m => m.Result == MatchResult.Win);
    public int Losses => Recent.Count(m => m.Result == MatchResult.Loss);
    public double WinRate => Recent.Count == 0 ? 0 : Math.Round(100.0 * Wins / Recent.Count, 0);
    public double AvgKd => Recent.Count == 0 ? 0 : Math.Round(Recent.Average(m => m.You.Kd), 2);
}

/// <summary>Pre-game agent-select state — who's on your team, who locked, map/mode.</summary>
public sealed record PreGameLobby
{
    public required string MatchId { get; init; }
    public string Map { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Phase { get; init; } = ""; // "character_select", "provisioned", etc.

    /// <summary>Agents already locked by your teammates + self (Subject, CharacterId, locked?).</summary>
    public IReadOnlyList<(string Puuid, string? CharacterId, bool Locked)> Allies { get; init; } = [];

    // Riot reports "character_select_active" while picking; match any character_select phase.
    public bool IsAgentSelect => Phase.StartsWith("character_select", StringComparison.OrdinalIgnoreCase);
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

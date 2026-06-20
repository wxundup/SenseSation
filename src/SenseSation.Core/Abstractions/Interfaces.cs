using SenseSation.Core.Models;

namespace SenseSation.Core.Abstractions;

/// <summary>Identifies a Riot account for the remote (HenrikDev) data source.</summary>
public readonly record struct RiotId(string Region, string Name, string Tag)
{
    public override string ToString() => $"{Name}#{Tag} [{Region}]";
}

/// <summary>
/// Remote match/stat data for a given account. Backed by HenrikDev's public
/// VALORANT API. Works without the game running and covers any public account.
/// </summary>
public interface IMatchDataSource
{
    /// <summary>A short label for the active source, shown in the UI (e.g. "HenrikDev", "Riot client").</summary>
    string SourceName { get; }

    /// <summary>
    /// True if the source needs an explicit region/name/tag (HenrikDev). False when it reads the
    /// logged-in local client (RadiantConnect), where the tracked account is whoever is signed in.
    /// </summary>
    bool NeedsRiotId { get; }

    Task<string?> ResolvePuuidAsync(RiotId id, CancellationToken ct = default);
    Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(RiotId id, int count, CancellationToken ct = default);
    Task<MatchDetail?> GetMatchAsync(string region, string matchId, RiotId owner, CancellationToken ct = default);
    Task<Rank?> GetRankAsync(RiotId id, CancellationToken ct = default);
    Task<IReadOnlyList<RrSnapshot>> GetMmrHistoryAsync(RiotId id, CancellationToken ct = default);
}

/// <summary>
/// LOCAL live client reader (RadiantConnect). Only available while Valorant is
/// running on this machine. Surfaces lobby metadata + ranks of teammates and
/// enemies — never live positional/economy state.
/// </summary>
public interface ILiveClient
{
    /// <summary>True once a session has been established with the local client.</summary>
    bool IsConnected { get; }

    /// <summary>The last connection/lobby error, for surfacing to the user. Null when fine.</summary>
    string? LastError { get; }

    /// <summary>Checks whether Valorant/Riot Client and the lockfile are present (no auth).</summary>
    LiveEnvironment DetectEnvironment();

    /// <summary>Connect to the running client via its lockfile. Returns false if unavailable.</summary>
    Task<bool> ConnectAsync(CancellationToken ct = default);

    /// <summary>The signed-in player's PUUID, or null if not connected.</summary>
    string? SelfPuuid { get; }

    /// <summary>Current pre-game/in-game lobby with resolved ranks, or null if not in a match.</summary>
    Task<LiveLobby?> GetCurrentLobbyAsync(CancellationToken ct = default);

    /// <summary>A lobby player's rank + recent competitive matches (public account data), or null.</summary>
    Task<PlayerCareer?> GetCareerAsync(string puuid, CancellationToken ct = default);

    /// <summary>Current pre-game agent-select lobby, or null if not in character select.</summary>
    Task<PreGameLobby?> GetPreGameAsync(CancellationToken ct = default);

    /// <summary>
    /// Pre-selects an agent (shows intent, does NOT lock). Equivalent to hovering an agent
    /// icon in-game. Safe and non-automated — only called on explicit user click.
    /// </summary>
    Task SelectAgentAsync(string agent, CancellationToken ct = default);

    /// <summary>
    /// Locks in the currently selected agent. Equivalent to clicking "Lock In" in the game.
    /// Only called on explicit user click — never automated.
    /// </summary>
    Task LockAgentAsync(string agent, CancellationToken ct = default);
}

/// <summary>Local persistence for match history, RR snapshots, and cached scoreboards.</summary>
public interface IMatchStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task UpsertMatchesAsync(RiotId owner, IEnumerable<MatchSummary> matches, CancellationToken ct = default);
    Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(RiotId owner, int count, CancellationToken ct = default);
    Task AppendRrAsync(RiotId owner, IEnumerable<RrSnapshot> snapshots, CancellationToken ct = default);
    Task<IReadOnlyList<RrSnapshot>> GetRrHistoryAsync(RiotId owner, CancellationToken ct = default);
}

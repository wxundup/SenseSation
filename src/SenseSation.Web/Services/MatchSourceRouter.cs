using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using SenseSation.Data.HenrikDev;
using SenseSation.Data.Radiant;

namespace SenseSation.Web.Services;

/// <summary>
/// Routes <see cref="IMatchDataSource"/> calls to HenrikDev when an API key is set,
/// otherwise to the local Riot client. Decided per-call from <see cref="SettingsStore"/>,
/// so setting the key in the UI switches sources immediately — no restart.
/// </summary>
public sealed class MatchSourceRouter(SettingsStore settings, HenrikDevClient henrik, RadiantMatchSource radiant)
    : IMatchDataSource
{
    private IMatchDataSource Active => settings.HasHenrikKey ? henrik : radiant;

    public string SourceName => Active.SourceName;
    public bool NeedsRiotId => Active.NeedsRiotId;

    public Task<string?> ResolvePuuidAsync(RiotId id, CancellationToken ct = default)
        => Active.ResolvePuuidAsync(id, ct);

    public Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(RiotId id, int count, CancellationToken ct = default)
        => Active.GetRecentMatchesAsync(id, count, ct);

    public Task<MatchDetail?> GetMatchAsync(string region, string matchId, RiotId owner, CancellationToken ct = default)
        => Active.GetMatchAsync(region, matchId, owner, ct);

    public Task<Rank?> GetRankAsync(RiotId id, CancellationToken ct = default)
        => Active.GetRankAsync(id, ct);

    public Task<IReadOnlyList<RrSnapshot>> GetMmrHistoryAsync(RiotId id, CancellationToken ct = default)
        => Active.GetMmrHistoryAsync(id, ct);
}

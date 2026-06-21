using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using RC = RadiantConnect.Network.PVPEndpoints.DataTypes;

namespace SenseSation.Data.Radiant;

/// <summary>
/// <see cref="IMatchDataSource"/> that needs NO external API key: it reads the
/// signed-in player's own stats straight from Riot's API using the local client
/// session (via <see cref="RiotSession"/>). Requires Valorant/Riot Client running.
/// The tracked account is always whoever is logged in, so the entered Riot ID is
/// ignored here.
/// </summary>
public sealed class RadiantMatchSource(RiotSession session) : IMatchDataSource
{
    private readonly RiotSession _session = session;

    public string SourceName => "Riot client (no key)";
    public bool NeedsRiotId => false;

    public async Task<string?> ResolvePuuidAsync(RiotId id, CancellationToken ct = default)
    {
        if (!await _session.EnsureConnectedAsync(ct))
            throw new InvalidOperationException(_session.LastError ?? "Riot client not connected.");
        return _session.SelfPuuid;
    }

    public async Task<Rank?> GetRankAsync(RiotId id, CancellationToken ct = default)
    {
        string self = await SelfAsync(ct);
        RC.PlayerMMR? mmr = await _session.ExecuteAsync(i => i.Endpoints.PvpEndpoints.FetchPlayerMMRAsync(self), ct);
        var u = mmr?.LatestCompetitiveUpdate;
        if (u is null) return Rank.Unranked;
        int tier = (int)(u.TierAfterUpdate ?? 0);
        int rr = (int)(u.RankedRatingAfterUpdate ?? 0);
        return RankTable.Make(tier, rr);
    }

    public async Task<IReadOnlyList<RrSnapshot>> GetMmrHistoryAsync(RiotId id, CancellationToken ct = default)
    {
        string self = await SelfAsync(ct);
        RC.CompetitiveUpdate? cu = await _session.ExecuteAsync(i => i.Endpoints.PvpEndpoints.FetchCompetitveUpdatesAsync(self), ct);
        if (cu?.Matches is null) return [];

        var list = new List<RrSnapshot>();
        foreach (var m in cu.Matches)
        {
            int tier = (int)(m.TierAfterUpdate ?? 0);
            long ms = AsMillis(m.MatchStartTime);
            list.Add(new RrSnapshot
            {
                At = ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.UtcNow,
                Tier = tier,
                TierName = RankTable.NameFor(tier),
                Rr = (int)(m.RankedRatingAfterUpdate ?? 0),
                RrDelta = (int)(m.RankedRatingEarned ?? 0),
                MatchId = m.MatchId,
            });
        }
        list.Reverse(); // API is newest-first; chart wants chronological
        return list;
    }

    public async Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(RiotId id, int count, CancellationToken ct = default)
    {
        string self = await SelfAsync(ct);
        RC.MatchHistory? history = await _session.ExecuteAsync(i => i.Endpoints.PvpEndpoints.FetchPlayerMatchHistoryAsync(self), ct);
        if (history?.History is null) return [];

        var ids = history.History
            .Where(h => string.Equals(h.QueueId, "competitive", StringComparison.OrdinalIgnoreCase))
            .Select(h => h.MatchId)
            .Where(s => !string.IsNullOrEmpty(s))
            .Take(count)
            .ToList();

        var result = new List<MatchSummary>();
        foreach (var matchId in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                RC.MatchInfo? info = await _session.ExecuteAsync(i => i.Endpoints.PvpEndpoints.FetchMatchInfoAsync(matchId!), ct);
                var detail = info is null ? null : Parse(info, self);
                if (detail is not null) result.Add(detail.Summary);
            }
            catch { /* skip a match that fails to load rather than fail the whole list */ }
        }
        return result;
    }

    public async Task<MatchDetail?> GetMatchAsync(string region, string matchId, RiotId owner, CancellationToken ct = default)
    {
        string self = await SelfAsync(ct);
        RC.MatchInfo? info = await _session.ExecuteAsync(i => i.Endpoints.PvpEndpoints.FetchMatchInfoAsync(matchId), ct);
        return info is null ? null : Parse(info, self);
    }

    private async Task<string> SelfAsync(CancellationToken ct)
    {
        if (!await _session.EnsureConnectedAsync(ct))
            throw new InvalidOperationException(_session.LastError ?? "Riot client not connected. Start Valorant and try again.");
        return _session.SelfPuuid!;
    }

    // ---- parsing (Riot raw match schema, via RadiantConnect typed records) -----

    internal static MatchDetail? Parse(RC.MatchInfo info, string selfPuuid)
    {
        var players = info.Players;
        if (players is null || players.Count == 0) return null;

        var me = players.FirstOrDefault(p => p.Subject == selfPuuid);
        if (me is null) return null;

        var meta = info.MatchInfoInternal;
        string myTeamId = me.TeamId ?? "";
        int rounds = (int)(me.Stats?.RoundsPlayed ?? 0);
        if (rounds == 0) rounds = info.RoundResults?.Count ?? 0;

        // Team result.
        var myTeam = info.Teams?.FirstOrDefault(t => t.TeamId == myTeamId);
        var oppTeam = info.Teams?.FirstOrDefault(t => t.TeamId != myTeamId);
        int won = (int)(myTeam?.RoundsWon ?? 0);
        int lost = (int)(oppTeam?.RoundsWon ?? 0);
        var result = won == lost ? MatchResult.Draw
            : (myTeam?.Won ?? won > lost) ? MatchResult.Win : MatchResult.Loss;

        var teamOf = players.ToDictionary(p => p.Subject ?? "", p => p.TeamId ?? "");

        var (firstKills, firstDeaths) = OpeningDuels(info, selfPuuid);
        var (multiKills, econViolations, econRounds) = RoundDerived(info, selfPuuid, myTeamId, teamOf);

        // Scoreboard + players.
        var scoreboard = new Dictionary<string, Scoreline>();
        var summaryPlayers = new List<PlayerSummary>();
        foreach (var p in players)
        {
            string pp = p.Subject ?? "";
            var line = Line(info, p, rounds);
            scoreboard[pp] = pp == selfPuuid
                ? line with { FirstKills = firstKills, FirstDeaths = firstDeaths, MultiKills = multiKills }
                : line;

            int tier = (int)(p.CompetitiveTier ?? 0);
            summaryPlayers.Add(new PlayerSummary
            {
                Puuid = pp,
                Name = p.GameName ?? "",
                Tag = p.TagLine ?? "",
                Agent = CharacterTable.Name(p.CharacterId),
                Team = (p.TeamId ?? "").Equals("Red", StringComparison.OrdinalIgnoreCase) ? Team.Red : Team.Blue,
                Rank = RankTable.Make(tier, 0),
                AccountLevel = (int)(p.AccountLevel ?? 0),
                IsSelf = pp == selfPuuid,
            });
        }

        int myTier = (int)(me.CompetitiveTier ?? 0);
        long startMs = meta?.GameStartMillis ?? 0;

        var summary = new MatchSummary
        {
            MatchId = meta?.MatchId ?? "",
            StartedAt = startMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(startMs) : DateTimeOffset.UtcNow,
            Map = MapName(meta?.MapId),
            Mode = ModeName(meta?.QueueId),
            Agent = CharacterTable.Name(me.CharacterId),
            Result = result,
            RoundsWon = won,
            RoundsLost = lost,
            RankAtMatch = RankTable.Make(myTier, 0),
            You = scoreboard[selfPuuid],
            EconDisciplinePct = econRounds > 0
                ? Math.Round(100.0 * (econRounds - econViolations) / econRounds, 1)
                : null,
        };

        return new MatchDetail
        {
            Summary = summary,
            AllPlayers = summaryPlayers,
            Scoreboard = scoreboard,
            Economy = Economy(info, selfPuuid),
        };
    }

    private static Scoreline Line(RC.MatchInfo info, RC.Player p, int rounds)
    {
        var s = p.Stats;
        int hs = 0, bs = 0, ls = 0;
        double damage = 0;
        string pp = p.Subject ?? "";

        foreach (var round in info.RoundResults ?? [])
            foreach (var ps in round.PlayerStats ?? [])
            {
                if (ps.Subject != pp) continue;
                foreach (var d in ps.DamageInternal ?? [])
                {
                    hs += (int)(d.Headshots ?? 0);
                    bs += (int)(d.Bodyshots ?? 0);
                    ls += (int)(d.Legshots ?? 0);
                    damage += d.DamageInternal ?? 0;
                }
            }

        int shots = hs + bs + ls;
        return new Scoreline
        {
            Kills = (int)(s?.Kills ?? 0),
            Deaths = (int)(s?.Deaths ?? 0),
            Assists = (int)(s?.Assists ?? 0),
            Score = (int)(s?.Score ?? 0),
            HeadshotPct = shots == 0 ? 0 : (int)Math.Round(100.0 * hs / shots),
            BodyshotPct = shots == 0 ? 0 : (int)Math.Round(100.0 * bs / shots),
            LegshotPct = shots == 0 ? 0 : (int)Math.Round(100.0 * ls / shots),
            DamagePerRound = rounds == 0 ? 0 : (int)Math.Round(damage / rounds),
        };
    }

    private static (int firstKills, int firstDeaths) OpeningDuels(RC.MatchInfo info, string self)
    {
        int fk = 0, fd = 0;
        var byRound = (info.Kills ?? []).Where(k => k.Round is not null).GroupBy(k => k.Round);
        foreach (var g in byRound)
        {
            var first = g.OrderBy(k => k.RoundTime ?? long.MaxValue).FirstOrDefault();
            if (first is null) continue;
            if (first.Killer == self) fk++;
            if (first.Victim == self) fd++;
        }
        return (fk, fd);
    }

    private static (int multiKills, int econViolations, int econRounds) RoundDerived(
        RC.MatchInfo info, string self, string myTeam, Dictionary<string, string> teamOf)
    {
        int multi = 0, viol = 0, econRounds = 0;
        foreach (var round in info.RoundResults ?? [])
        {
            var stats = round.PlayerStats ?? [];

            var mine = stats.FirstOrDefault(ps => ps.Subject == self);
            if (mine?.Kills is { Count: >= 2 }) multi++;

            int myLoadout = (int)(mine?.Economy?.LoadoutValue ?? -1);
            var allyLoadouts = stats
                .Where(ps => ps.Subject != self && teamOf.TryGetValue(ps.Subject ?? "", out var t) && t == myTeam)
                .Select(ps => (int)(ps.Economy?.LoadoutValue ?? 0))
                .ToList();

            if (myLoadout >= 0 && allyLoadouts.Count > 0)
            {
                econRounds++;
                if (allyLoadouts.Average() < 2000 && myLoadout >= 3900) viol++;
            }
        }
        return (multi, viol, econRounds);
    }

    private static List<RoundEconomy> Economy(RC.MatchInfo info, string self)
    {
        var list = new List<RoundEconomy>();
        foreach (var round in info.RoundResults ?? [])
        {
            var mine = (round.PlayerStats ?? []).FirstOrDefault(ps => ps.Subject == self);
            int load = (int)(mine?.Economy?.LoadoutValue ?? 0);
            list.Add(new RoundEconomy
            {
                RoundNumber = (int)(round.RoundNum ?? list.Count) + 1,
                LoadoutValue = load,
                CreditsRemaining = (int)(mine?.Economy?.Remaining ?? 0),
                BuyType = Classify(load),
                WonRound = round.WinningTeam == (info.Players?.FirstOrDefault(p => p.Subject == self)?.TeamId),
            });
        }
        return list;
    }

    private static BuyType Classify(int loadout) => loadout switch
    {
        < 1000 => BuyType.Save,
        < 2500 => BuyType.Eco,
        < 3900 => BuyType.ForceBuy,
        _ => BuyType.FullBuy
    };

    private static long AsMillis(object? o) => o switch
    {
        long l => l,
        int i => i,
        double d => (long)d,
        string s when long.TryParse(s, out var v) => v,
        // RadiantConnect deserializes `object` fields as JsonElement.
        System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Number
            && je.TryGetInt64(out var n) => n,
        System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
            && long.TryParse(je.GetString(), out var sv) => sv,
        _ => 0
    };

    private static string MapName(string? mapId) => MapTable.Display(mapId);

    private static string ModeName(string? queueId) => string.IsNullOrEmpty(queueId)
        ? "" : char.ToUpper(queueId[0]) + queueId[1..];
}

using System.Net;
using System.Text.Json;
using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;
using SenseSation.Data.Json;

namespace SenseSation.Data.HenrikDev;

/// <summary>Thrown when the HenrikDev API returns an error or is misconfigured.</summary>
public sealed class HenrikApiException(string message) : Exception(message);

/// <summary>
/// <see cref="IMatchDataSource"/> backed by the public HenrikDev VALORANT API.
/// Works without the game running and for any public account. All parsing is
/// defensive so partial/changed payloads degrade gracefully instead of throwing.
/// </summary>
public sealed class HenrikDevClient(HttpClient http, HenrikOptions options) : IMatchDataSource
{
    private readonly HttpClient _http = http;
    private readonly HenrikOptions _opts = options;

    public string SourceName => "HenrikDev";
    public bool NeedsRiotId => true;

    public async Task<string?> ResolvePuuidAsync(RiotId id, CancellationToken ct = default)
    {
        var data = await GetDataAsync($"/valorant/v1/account/{Esc(id.Name)}/{Esc(id.Tag)}", ct);
        if (data is null) return null;
        var puuid = data.Value.Str("puuid");
        return string.IsNullOrEmpty(puuid) ? null : puuid;
    }

    public async Task<Rank?> GetRankAsync(RiotId id, CancellationToken ct = default)
    {
        var data = await GetDataAsync(
            $"/valorant/v2/mmr/{Region(id)}/{Esc(id.Name)}/{Esc(id.Tag)}", ct);
        if (data is null) return null;

        var cur = data.Value.Prop("current_data");
        if (cur.ValueKind != JsonValueKind.Object) cur = data.Value;

        int tier = cur.Int("currenttier");
        int rr = cur.Int("ranking_in_tier");
        string name = cur.Str("currenttierpatched");
        return new Rank(tier, string.IsNullOrEmpty(name) ? RankTable.NameFor(tier) : name, rr);
    }

    public async Task<IReadOnlyList<RrSnapshot>> GetMmrHistoryAsync(RiotId id, CancellationToken ct = default)
    {
        var data = await GetDataAsync(
            $"/valorant/v1/mmr-history/{Region(id)}/{Esc(id.Name)}/{Esc(id.Tag)}", ct);
        if (data is null || data.Value.ValueKind != JsonValueKind.Array) return [];

        var list = new List<RrSnapshot>();
        foreach (var e in data.Value.EnumerateArray())
        {
            int tier = e.Int("currenttier");
            long dateRaw = e.Long("date_raw");
            var at = dateRaw > 0
                ? DateTimeOffset.FromUnixTimeSeconds(dateRaw)
                : DateTimeOffset.TryParse(e.Str("date"), out var p) ? p : DateTimeOffset.UtcNow;

            list.Add(new RrSnapshot
            {
                At = at,
                Tier = tier,
                TierName = e.Str("currenttierpatched", RankTable.NameFor(tier)),
                Rr = e.Int("ranking_in_tier"),
                RrDelta = e.Int("mmr_change_to_last_game"),
                MatchId = e.Str("match_id") is { Length: > 0 } m ? m : null,
            });
        }
        // API returns newest-first; present chronologically for graphing.
        list.Reverse();
        return list;
    }

    public async Task<IReadOnlyList<MatchSummary>> GetRecentMatchesAsync(
        RiotId id, int count, CancellationToken ct = default)
    {
        string? puuid = await ResolvePuuidAsync(id, ct);
        var data = await GetDataAsync(
            $"/valorant/v3/matches/{Region(id)}/{Esc(id.Name)}/{Esc(id.Tag)}?mode=competitive&size={count}", ct);
        if (data is null || data.Value.ValueKind != JsonValueKind.Array) return [];

        var result = new List<MatchSummary>();
        foreach (var match in data.Value.EnumerateArray())
        {
            var detail = ParseMatch(match, puuid, id);
            if (detail is not null) result.Add(detail.Summary);
        }
        return result;
    }

    public async Task<MatchDetail?> GetMatchAsync(string region, string matchId, RiotId owner, CancellationToken ct = default)
    {
        string? puuid = await ResolvePuuidAsync(owner, ct);
        var data = await GetDataAsync($"/valorant/v2/match/{Esc(matchId)}", ct);
        if (data is null) return null;
        // v2/match returns a single object under data.
        return ParseMatch(data.Value, puuid, owner);
    }

    // ---- parsing -------------------------------------------------------------

    private static MatchDetail? ParseMatch(JsonElement m, string? ownerPuuid, RiotId owner)
    {
        var meta = m.Prop("metadata");
        var players = m.Prop("players");
        var allPlayers = players.Arr("all_players").ToList();
        if (allPlayers.Count == 0) return null;

        // Locate the owner's row by puuid, falling back to name#tag.
        JsonElement? me = null;
        foreach (var p in allPlayers)
        {
            bool byPuuid = ownerPuuid is not null && p.Str("puuid") == ownerPuuid;
            bool byName = p.Str("name").Equals(owner.Name, StringComparison.OrdinalIgnoreCase)
                          && p.Str("tag").Equals(owner.Tag, StringComparison.OrdinalIgnoreCase);
            if (byPuuid || byName) { me = p; break; }
        }
        if (me is null) return null;
        var meEl = me.Value;
        string myPuuid = meEl.Str("puuid");
        string myTeam = meEl.Str("team"); // "Red" / "Blue"

        int rounds = meta.Int("rounds_played");
        var teams = m.Prop("teams");
        var myTeamNode = teams.Prop(myTeam.ToLowerInvariant());
        var oppTeamNode = teams.Prop(myTeam.Equals("Red", StringComparison.OrdinalIgnoreCase) ? "blue" : "red");
        int won = myTeamNode.Int("rounds_won");
        int lost = myTeamNode.Int("rounds_lost");
        if (won == 0 && lost == 0) { won = myTeamNode.Int("rounds_won"); lost = oppTeamNode.Int("rounds_won"); }
        if (rounds == 0) rounds = won + lost;

        bool hasWon = myTeamNode.Bool("has_won");
        var result = won == lost ? MatchResult.Draw : hasWon || won > lost ? MatchResult.Win : MatchResult.Loss;

        // Build puuid -> team map for econ analysis.
        var teamOf = allPlayers.ToDictionary(p => p.Str("puuid"), p => p.Str("team"));

        // Per-round derived stats for the owner.
        var (firstKills, firstDeaths, multiKills, econViolations, econRounds) =
            AnalyzeRounds(m, myPuuid, myTeam, teamOf);

        var scoreboard = new Dictionary<string, Scoreline>();
        var summaryPlayers = new List<PlayerSummary>();
        foreach (var p in allPlayers)
        {
            var line = ReadScoreline(p, rounds);
            string pp = p.Str("puuid");
            scoreboard[pp] = pp == myPuuid
                ? line with { FirstKills = firstKills, FirstDeaths = firstDeaths, MultiKills = multiKills }
                : line;

            int tier = p.Int("currenttier");
            summaryPlayers.Add(new PlayerSummary
            {
                Puuid = pp,
                Name = p.Str("name"),
                Tag = p.Str("tag"),
                Agent = p.Str("character"),
                Team = p.Str("team").Equals("Red", StringComparison.OrdinalIgnoreCase) ? Team.Red : Team.Blue,
                Rank = new Rank(tier, p.Str("currenttier_patched", RankTable.NameFor(tier)), 0),
                AccountLevel = p.Int("level"),
                IsSelf = pp == myPuuid,
            });
        }

        var myLine = scoreboard[myPuuid];
        int myTier = meEl.Int("currenttier");

        var summary = new MatchSummary
        {
            MatchId = meta.Str("matchid"),
            StartedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, meta.Long("game_start"))),
            Map = meta.Str("map"),
            Mode = meta.Str("mode"),
            Agent = meEl.Str("character"),
            Result = result,
            RoundsWon = won,
            RoundsLost = lost,
            RankAtMatch = new Rank(myTier, meEl.Str("currenttier_patched", RankTable.NameFor(myTier)), 0),
            You = myLine,
            EconDisciplinePct = econRounds > 0
                ? Math.Round(100.0 * (econRounds - econViolations) / econRounds, 1)
                : null,
        };

        return new MatchDetail
        {
            Summary = summary,
            AllPlayers = summaryPlayers,
            Scoreboard = scoreboard,
            Economy = ReadEconomy(m, myPuuid),
        };
    }

    private static Scoreline ReadScoreline(JsonElement p, int rounds)
    {
        var stats = p.Prop("stats");
        int hs = stats.Int("headshots");
        int bs = stats.Int("bodyshots");
        int ls = stats.Int("legshots");
        int shots = hs + bs + ls;

        int damage = p.Int("damage_made");
        if (damage == 0) damage = p.Prop("damage").Int("made");

        return new Scoreline
        {
            Kills = stats.Int("kills"),
            Deaths = stats.Int("deaths"),
            Assists = stats.Int("assists"),
            Score = stats.Int("score"),
            HeadshotPct = shots == 0 ? 0 : (int)Math.Round(100.0 * hs / shots),
            BodyshotPct = shots == 0 ? 0 : (int)Math.Round(100.0 * bs / shots),
            LegshotPct = shots == 0 ? 0 : (int)Math.Round(100.0 * ls / shots),
            DamagePerRound = rounds == 0 ? 0 : (int)Math.Round((double)damage / rounds),
        };
    }

    /// <summary>Derives opening-duel counts, multi-kill rounds, and econ-discipline violations.</summary>
    private static (int firstKills, int firstDeaths, int multiKills, int econViolations, int econRounds)
        AnalyzeRounds(JsonElement m, string myPuuid, string myTeam, Dictionary<string, string> teamOf)
    {
        int firstKills = 0, firstDeaths = 0, multiKills = 0, econViolations = 0, econRounds = 0;

        foreach (var round in m.Arr("rounds"))
        {
            var playerStats = round.Arr("player_stats").ToList();
            if (playerStats.Count == 0) continue;

            // Multi-kill: owner got 2+ kills this round.
            foreach (var ps in playerStats)
                if (ps.Str("player_puuid") == myPuuid && ps.Int("kills") >= 2)
                    multiKills++;

            // Opening duel: earliest kill event across the round.
            double bestTime = double.MaxValue;
            string? firstKiller = null, firstVictim = null;
            foreach (var ps in playerStats)
                foreach (var ke in ps.Arr("kill_events"))
                {
                    double t = ke.Prop("kill_time_in_round").ValueKind == JsonValueKind.Number
                        ? ke.Prop("kill_time_in_round").GetDouble() : double.MaxValue;
                    if (t < bestTime)
                    {
                        bestTime = t;
                        firstKiller = ke.Str("killer_puuid");
                        firstVictim = ke.Str("victim_puuid");
                    }
                }
            if (firstKiller == myPuuid) firstKills++;
            if (firstVictim == myPuuid) firstDeaths++;

            // Econ discipline: did the owner solo-buy a rifle while the team was on an eco/save?
            var allyLoadouts = new List<int>();
            int myLoadout = -1;
            foreach (var ps in playerStats)
            {
                string pp = ps.Str("player_puuid");
                int load = ps.Prop("economy").Int("loadout_value");
                if (pp == myPuuid) myLoadout = load;
                else if (teamOf.TryGetValue(pp, out var t) && t == myTeam) allyLoadouts.Add(load);
            }
            if (myLoadout >= 0 && allyLoadouts.Count > 0)
            {
                econRounds++;
                double allyAvg = allyLoadouts.Average();
                if (allyAvg < 2000 && myLoadout >= 3900) econViolations++; // solo rifle on a team save
            }
        }

        return (firstKills, firstDeaths, multiKills, econViolations, econRounds);
    }

    private static List<RoundEconomy> ReadEconomy(JsonElement m, string myPuuid)
    {
        var list = new List<RoundEconomy>();
        int n = 0;
        foreach (var round in m.Arr("rounds"))
        {
            n++;
            string winningTeam = round.Str("winning_team");
            foreach (var ps in round.Arr("player_stats"))
            {
                if (ps.Str("player_puuid") != myPuuid) continue;
                var eco = ps.Prop("economy");
                int load = eco.Int("loadout_value");
                list.Add(new RoundEconomy
                {
                    RoundNumber = n,
                    LoadoutValue = load,
                    CreditsRemaining = eco.Int("remaining"),
                    BuyType = Classify(load),
                    WonRound = false, // team-relative win not resolved here; UI shows buy pattern
                });
            }
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

    // ---- transport -----------------------------------------------------------

    private async Task<JsonElement?> GetDataAsync(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.ApiKey))
            throw new HenrikApiException(
                "HenrikDev API key is not configured. Set Henrik:ApiKey in user-secrets or appsettings.");

        using var req = new HttpRequestMessage(HttpMethod.Get, _opts.BaseUrl.TrimEnd('/') + path);
        req.Headers.TryAddWithoutValidation("Authorization", _opts.ApiKey);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
            throw new HenrikApiException($"HenrikDev {(int)resp.StatusCode} for {path}: {Truncate(body)}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryProp("data", out var data))
            return data.Clone();
        return root.Clone();
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
    private static string Esc(string s) => Uri.EscapeDataString(s);
    private static string Region(RiotId id) => Esc(id.Region.ToLowerInvariant());
}

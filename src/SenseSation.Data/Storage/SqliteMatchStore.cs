using Dapper;
using Microsoft.Data.Sqlite;
using SenseSation.Core.Abstractions;
using SenseSation.Core.Models;

namespace SenseSation.Data.Storage;

public sealed class SqliteStoreOptions
{
    public const string Section = "Storage";

    /// <summary>SQLite file path. Defaults to a per-user app-data location.</summary>
    public string DbPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenseSation", "sensesation.db");
}

/// <summary>
/// Local SQLite persistence for match history and RR snapshots, so the dashboard
/// has data offline and can graph RR trends beyond the API's short window.
/// </summary>
public sealed class SqliteMatchStore(SqliteStoreOptions options) : IMatchStore
{
    private readonly string _connStr = BuildConnectionString(options.DbPath);

    private static string BuildConnectionString(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connStr);
        c.Open();
        return c;
    }

    private static string OwnerKey(RiotId id) => $"{id.Name}#{id.Tag}@{id.Region}".ToLowerInvariant();

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var c = Open();

        // Schema v2: rr is keyed by MatchId (was At), which deduplicates RR rows
        // across refreshes. Drop the old rr table once to clear accumulated dupes.
        long version = Convert.ToInt64(await c.ExecuteScalarAsync<long?>("PRAGMA user_version;") ?? 0);
        if (version < 2)
        {
            await c.ExecuteAsync("DROP TABLE IF EXISTS rr; PRAGMA user_version = 2;");
        }

        await c.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS matches (
                Owner TEXT NOT NULL, MatchId TEXT NOT NULL,
                StartedAt INTEGER, Map TEXT, Mode TEXT, Agent TEXT,
                Result INTEGER, RoundsWon INTEGER, RoundsLost INTEGER,
                RankTier INTEGER, RankName TEXT,
                Kills INTEGER, Deaths INTEGER, Assists INTEGER, Score INTEGER,
                HsPct INTEGER, Adr INTEGER, FirstKills INTEGER, FirstDeaths INTEGER, MultiKills INTEGER,
                EconPct REAL NULL,
                PRIMARY KEY (Owner, MatchId)
            );
            CREATE TABLE IF NOT EXISTS rr (
                Owner TEXT NOT NULL, MatchId TEXT NOT NULL, At INTEGER,
                Tier INTEGER, TierName TEXT, Rr INTEGER, RrDelta INTEGER,
                PRIMARY KEY (Owner, MatchId)
            );
            """);
    }

    public async Task UpsertMatchesAsync(RiotId owner, IEnumerable<MatchSummary> matches, CancellationToken ct = default)
    {
        await using var c = Open();
        await using var tx = c.BeginTransaction();
        string key = OwnerKey(owner);
        foreach (var m in matches)
        {
            await c.ExecuteAsync("""
                INSERT OR REPLACE INTO matches
                (Owner, MatchId, StartedAt, Map, Mode, Agent, Result, RoundsWon, RoundsLost,
                 RankTier, RankName, Kills, Deaths, Assists, Score, HsPct, Adr,
                 FirstKills, FirstDeaths, MultiKills, EconPct)
                VALUES
                (@Owner, @MatchId, @StartedAt, @Map, @Mode, @Agent, @Result, @RoundsWon, @RoundsLost,
                 @RankTier, @RankName, @Kills, @Deaths, @Assists, @Score, @HsPct, @Adr,
                 @FirstKills, @FirstDeaths, @MultiKills, @EconPct);
                """,
                new
                {
                    Owner = key,
                    m.MatchId,
                    StartedAt = m.StartedAt.ToUnixTimeSeconds(),
                    m.Map,
                    m.Mode,
                    m.Agent,
                    Result = (int)m.Result,
                    m.RoundsWon,
                    m.RoundsLost,
                    RankTier = m.RankAtMatch.Tier,
                    RankName = m.RankAtMatch.Name,
                    m.You.Kills,
                    m.You.Deaths,
                    m.You.Assists,
                    m.You.Score,
                    HsPct = m.You.HeadshotPct,
                    Adr = m.You.DamagePerRound,
                    m.You.FirstKills,
                    m.You.FirstDeaths,
                    m.You.MultiKills,
                    EconPct = m.EconDisciplinePct,
                }, tx);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(RiotId owner, int count, CancellationToken ct = default)
    {
        await using var c = Open();
        var rows = await c.QueryAsync<MatchRow>(
            "SELECT * FROM matches WHERE Owner = @Owner ORDER BY StartedAt DESC LIMIT @Count;",
            new { Owner = OwnerKey(owner), Count = count });
        return rows.Select(r => r.ToSummary()).ToList();
    }

    public async Task AppendRrAsync(RiotId owner, IEnumerable<RrSnapshot> snapshots, CancellationToken ct = default)
    {
        await using var c = Open();
        await using var tx = c.BeginTransaction();
        string key = OwnerKey(owner);
        foreach (var s in snapshots)
        {
            // MatchId is the dedupe key; skip entries without one.
            if (string.IsNullOrEmpty(s.MatchId)) continue;
            await c.ExecuteAsync("""
                INSERT OR REPLACE INTO rr (Owner, MatchId, At, Tier, TierName, Rr, RrDelta)
                VALUES (@Owner, @MatchId, @At, @Tier, @TierName, @Rr, @RrDelta);
                """,
                new
                {
                    Owner = key,
                    s.MatchId,
                    At = s.At.ToUnixTimeSeconds(),
                    s.Tier,
                    s.TierName,
                    s.Rr,
                    s.RrDelta,
                }, tx);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<RrSnapshot>> GetRrHistoryAsync(RiotId owner, CancellationToken ct = default)
    {
        await using var c = Open();
        var rows = await c.QueryAsync<RrRow>(
            "SELECT * FROM rr WHERE Owner = @Owner ORDER BY At ASC;",
            new { Owner = OwnerKey(owner) });
        return rows.Select(r => r.ToSnapshot()).ToList();
    }

    // Flat row DTOs mapped by Dapper, then projected to domain records.
    private sealed class MatchRow
    {
        public string MatchId { get; set; } = "";
        public long StartedAt { get; set; }
        public string? Map { get; set; }
        public string? Mode { get; set; }
        public string? Agent { get; set; }
        public int Result { get; set; }
        public int RoundsWon { get; set; }
        public int RoundsLost { get; set; }
        public int RankTier { get; set; }
        public string? RankName { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int Score { get; set; }
        public int HsPct { get; set; }
        public int Adr { get; set; }
        public int FirstKills { get; set; }
        public int FirstDeaths { get; set; }
        public int MultiKills { get; set; }
        public double? EconPct { get; set; }

        public MatchSummary ToSummary() => new()
        {
            MatchId = MatchId,
            StartedAt = DateTimeOffset.FromUnixTimeSeconds(StartedAt),
            Map = Map ?? "",
            Mode = Mode ?? "",
            Agent = Agent ?? "",
            Result = (MatchResult)Result,
            RoundsWon = RoundsWon,
            RoundsLost = RoundsLost,
            RankAtMatch = new Rank(RankTier, RankName ?? RankTable.NameFor(RankTier), 0),
            EconDisciplinePct = EconPct,
            You = new Scoreline
            {
                Kills = Kills, Deaths = Deaths, Assists = Assists, Score = Score,
                HeadshotPct = HsPct, DamagePerRound = Adr,
                FirstKills = FirstKills, FirstDeaths = FirstDeaths, MultiKills = MultiKills,
            },
        };
    }

    private sealed class RrRow
    {
        public long At { get; set; }
        public int Tier { get; set; }
        public string? TierName { get; set; }
        public int Rr { get; set; }
        public int RrDelta { get; set; }
        public string? MatchId { get; set; }

        public RrSnapshot ToSnapshot() => new()
        {
            At = DateTimeOffset.FromUnixTimeSeconds(At),
            Tier = Tier,
            TierName = TierName ?? RankTable.NameFor(Tier),
            Rr = Rr,
            RrDelta = RrDelta,
            MatchId = MatchId,
        };
    }
}

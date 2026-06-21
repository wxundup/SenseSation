namespace SenseSation.Core.Models;

/// <summary>A competitive rank: numeric tier (0-27) plus display name and current RR.</summary>
public readonly record struct Rank(int Tier, string Name, int Rr)
{
    public static readonly Rank Unranked = new(0, "Unranked", 0);

    /// <summary>Coarse division name (Iron..Radiant) without the 1/2/3 sublevel.</summary>
    public string Division => Name.Split(' ')[0];
}

/// <summary>One player as seen in a match or live lobby.</summary>
public sealed record PlayerSummary
{
    public required string Puuid { get; init; }
    public string Name { get; init; } = "";
    public string Tag { get; init; } = "";
    public string Agent { get; init; } = "";
    public Team Team { get; init; } = Team.Unknown;
    public Rank Rank { get; init; } = Rank.Unranked;
    public int AccountLevel { get; init; }
    public bool IsSelf { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Tag) ? Name : $"{Name}#{Tag}";

    /// <summary>Name when shown, else the agent they're playing (hidden/incognito players), else "Player".</summary>
    public string NameOrAgent => !string.IsNullOrEmpty(Name) ? DisplayName
        : !string.IsNullOrEmpty(Agent) ? Agent : "Player";
}

/// <summary>A single player's line on the scoreboard for one match.</summary>
public sealed record Scoreline
{
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int Score { get; init; }
    public int HeadshotPct { get; init; }
    public int BodyshotPct { get; init; }
    public int LegshotPct { get; init; }
    public int DamagePerRound { get; init; }
    public int FirstKills { get; init; }
    public int FirstDeaths { get; init; }
    public int MultiKills { get; init; }

    public double Kd => Deaths == 0 ? Kills : Math.Round((double)Kills / Deaths, 2);
    public double Kda => Deaths == 0 ? Kills + Assists : Math.Round((double)(Kills + Assists) / Deaths, 2);
}

/// <summary>Compact match record for history lists and aggregate stats.</summary>
public sealed record MatchSummary
{
    public required string MatchId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public string Map { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Agent { get; init; } = "";
    public MatchResult Result { get; init; }
    public int RoundsWon { get; init; }
    public int RoundsLost { get; init; }
    public Rank RankAtMatch { get; init; } = Rank.Unranked;
    public required Scoreline You { get; init; }

    /// <summary>Econ-discipline % for this match (null when per-round economy was unavailable).</summary>
    public double? EconDisciplinePct { get; init; }

    public string ScoreLabel => $"{RoundsWon}-{RoundsLost}";
    public string KdaLabel => $"{You.Kills} / {You.Deaths} / {You.Assists}";
    public string ResultLabel => Result.ToString().ToUpperInvariant();
}

/// <summary>One round's economy snapshot, used for econ-discipline analysis.</summary>
public sealed record RoundEconomy
{
    public int RoundNumber { get; init; }
    public int LoadoutValue { get; init; }
    public int CreditsRemaining { get; init; }
    public BuyType BuyType { get; init; }
    public bool WonRound { get; init; }
}

/// <summary>Full match detail: scoreboard for all players plus per-round economy.</summary>
public sealed record MatchDetail
{
    public required MatchSummary Summary { get; init; }
    public IReadOnlyList<PlayerSummary> AllPlayers { get; init; } = [];
    public IReadOnlyDictionary<string, Scoreline> Scoreboard { get; init; } =
        new Dictionary<string, Scoreline>();
    public IReadOnlyList<RoundEconomy> Economy { get; init; } = [];
}

/// <summary>A point on the RR-over-time graph.</summary>
public sealed record RrSnapshot
{
    public DateTimeOffset At { get; init; }
    public int Tier { get; init; }
    public string TierName { get; init; } = "";
    public int Rr { get; init; }
    public int RrDelta { get; init; }
    public string? MatchId { get; init; }

    /// <summary>Monotonic ladder position (tier*100 + rr) for a clean graph y-axis.</summary>
    public int LadderPoints => Tier * 100 + Rr;
}

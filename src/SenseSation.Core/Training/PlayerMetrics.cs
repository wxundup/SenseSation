using SenseSation.Core.Models;

namespace SenseSation.Core.Training;

/// <summary>Aggregate performance metrics computed across a window of matches.</summary>
public sealed record PlayerMetrics
{
    public int MatchesAnalyzed { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public double WinRate => MatchesAnalyzed == 0 ? 0 : Math.Round(100.0 * Wins / MatchesAnalyzed, 1);

    public double HeadshotPct { get; init; }
    public double Kd { get; init; }
    public double Adr { get; init; }
    public double Kast { get; init; }
    public double FirstKillsPerMatch { get; init; }
    public double FirstDeathsPerMatch { get; init; }
    public double MultiKillsPerMatch { get; init; }
    public double EconDiscipline { get; init; }

    /// <summary>Most-played agent across the window, for context.</summary>
    public string TopAgent { get; init; } = "";

    /// <summary>Reads the value for a benchmark key so the engine can compare generically.</summary>
    public double ValueFor(string key) => key switch
    {
        "hs_pct" => HeadshotPct,
        "kd" => Kd,
        "adr" => Adr,
        "kast" => Kast,
        "first_kill_rate" => FirstKillsPerMatch,
        "first_death_rate" => FirstDeathsPerMatch,
        "multikill_rate" => MultiKillsPerMatch,
        "econ_discipline" => EconDiscipline,
        _ => 0
    };
}

/// <summary>Computes <see cref="PlayerMetrics"/> from a set of match summaries.</summary>
public static class MetricsCalculator
{
    public static PlayerMetrics Compute(IReadOnlyList<MatchSummary> matches)
    {
        if (matches.Count == 0) return new PlayerMetrics();

        var lines = matches.Select(m => m.You).ToList();
        int totalKills = lines.Sum(l => l.Kills);
        int totalDeaths = lines.Sum(l => l.Deaths);

        var topAgent = matches
            .Where(m => !string.IsNullOrEmpty(m.Agent))
            .GroupBy(m => m.Agent)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "";

        // KAST is not directly exposed by match summaries; approximate it from
        // the share of rounds where the player got a kill, assist, or survived.
        // We use a conservative proxy: (kills+assists capped per round + survived rounds).
        double kastProxy = ApproximateKast(matches);

        return new PlayerMetrics
        {
            MatchesAnalyzed = matches.Count,
            Wins = matches.Count(m => m.Result == MatchResult.Win),
            Losses = matches.Count(m => m.Result == MatchResult.Loss),
            HeadshotPct = Math.Round(lines.Average(l => l.HeadshotPct), 1),
            Kd = totalDeaths == 0 ? totalKills : Math.Round((double)totalKills / totalDeaths, 2),
            Adr = Math.Round(lines.Average(l => l.DamagePerRound), 0),
            Kast = Math.Round(kastProxy, 1),
            FirstKillsPerMatch = Math.Round(lines.Average(l => l.FirstKills), 1),
            FirstDeathsPerMatch = Math.Round(lines.Average(l => l.FirstDeaths), 1),
            MultiKillsPerMatch = Math.Round(lines.Average(l => l.MultiKills), 1),
            EconDiscipline = Math.Round(EconDisciplineFor(matches), 1),
            TopAgent = topAgent,
        };
    }

    private static double ApproximateKast(IReadOnlyList<MatchSummary> matches)
    {
        // Proxy: per match, rounds with impact ~= min(rounds, kills + assists + (rounds - deaths)).
        // Averaged and expressed as a percentage of rounds played.
        double sum = 0;
        int counted = 0;
        foreach (var m in matches)
        {
            int rounds = m.RoundsWon + m.RoundsLost;
            if (rounds == 0) continue;
            var l = m.You;
            int survived = Math.Max(0, rounds - l.Deaths);
            int impact = Math.Min(rounds, l.Kills + l.Assists + survived);
            sum += 100.0 * impact / rounds;
            counted++;
        }
        return counted == 0 ? 0 : sum / counted;
    }

    private static double EconDisciplineFor(IReadOnlyList<MatchSummary> matches)
    {
        var measured = matches.Where(m => m.EconDisciplinePct.HasValue)
                              .Select(m => m.EconDisciplinePct!.Value)
                              .ToList();
        // Neutral 85 when no match exposed per-round economy, to avoid penalizing on missing data.
        return measured.Count == 0 ? 85 : measured.Average();
    }
}

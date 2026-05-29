using SenseSation.Core.Models;
using SenseSation.Core.Training;
using Xunit;

namespace SenseSation.Tests;

public class TrainerEngineTests
{
    private static MatchSummary Match(
        MatchResult result, int kills, int deaths, int hsPct, int adr, int firstKills = 4,
        int firstDeaths = 3, int multiKills = 2, double? econ = null)
        => new()
        {
            MatchId = Guid.NewGuid().ToString(),
            StartedAt = DateTimeOffset.UtcNow,
            Map = "Ascent",
            Mode = "Competitive",
            Agent = "Jett",
            Result = result,
            RoundsWon = result == MatchResult.Win ? 13 : 8,
            RoundsLost = result == MatchResult.Win ? 8 : 13,
            EconDisciplinePct = econ,
            You = new Scoreline
            {
                Kills = kills, Deaths = deaths, Assists = 4, Score = kills * 200,
                HeadshotPct = hsPct, DamagePerRound = adr,
                FirstKills = firstKills, FirstDeaths = firstDeaths, MultiKills = multiKills,
            },
        };

    [Fact]
    public void Compute_aggregates_winrate_and_kd()
    {
        var matches = new[]
        {
            Match(MatchResult.Win, 20, 10, 25, 160),
            Match(MatchResult.Loss, 10, 20, 25, 160),
            Match(MatchResult.Win, 15, 15, 25, 160),
        };

        var m = MetricsCalculator.Compute(matches);

        Assert.Equal(3, m.MatchesAnalyzed);
        Assert.Equal(2, m.Wins);
        Assert.Equal(1, m.Losses);
        Assert.InRange(m.WinRate, 66.6, 66.7);
        Assert.Equal(1.0, m.Kd, 2); // 45 kills / 45 deaths
        Assert.Equal("Jett", m.TopAgent);
    }

    [Fact]
    public void Analyze_flags_low_headshot_as_a_weakness_with_drills()
    {
        // 12% HS is well below the 22% "Good" benchmark -> should be a ranked weakness.
        var matches = Enumerable.Range(0, 5)
            .Select(_ => Match(MatchResult.Win, 18, 14, 12, 150)).ToList();

        var report = TrainerEngine.Analyze(MetricsCalculator.Compute(matches));

        Assert.Contains(report.Weaknesses, w => w.Benchmark.Key == "hs_pct");
        Assert.NotEmpty(report.Plan);
        Assert.Contains(report.Plan, d => d.Area == "Aim");
    }

    [Fact]
    public void Metric_meeting_benchmark_is_not_flagged_as_a_weakness()
    {
        var matches = Enumerable.Range(0, 5)
            .Select(_ => Match(MatchResult.Win, 22, 12, 30, 175)).ToList();

        var report = TrainerEngine.Analyze(MetricsCalculator.Compute(matches));

        // No praise — strengths are never reported, and a met metric simply isn't a weakness.
        Assert.DoesNotContain(report.Weaknesses, w => w.Benchmark.Key == "hs_pct");
        Assert.Empty(report.Strengths);
    }

    [Fact]
    public void Losing_kd_is_always_a_major_weakness_with_a_blunt_critique()
    {
        var matches = Enumerable.Range(0, 5)
            .Select(_ => Match(MatchResult.Loss, 12, 16, 25, 150)).ToList();

        var report = TrainerEngine.Analyze(MetricsCalculator.Compute(matches));

        var kd = report.Weaknesses.Single(w => w.Benchmark.Key == "kd");
        Assert.Equal(Severity.Major, kd.Severity);
        Assert.False(string.IsNullOrWhiteSpace(kd.Critique));
    }

    [Fact]
    public void Econ_discipline_uses_measured_values_when_present()
    {
        var matches = new[]
        {
            Match(MatchResult.Win, 18, 14, 25, 160, econ: 60),
            Match(MatchResult.Loss, 12, 16, 25, 160, econ: 70),
        };

        var m = MetricsCalculator.Compute(matches);

        Assert.Equal(65, m.EconDiscipline, 1); // average of measured values, not the 85 default
    }
}

public class RankTableTests
{
    [Theory]
    [InlineData(0, "Unranked")]
    [InlineData(3, "Iron 1")]
    [InlineData(27, "Radiant")]
    public void NameFor_maps_known_tiers(int tier, string expected)
        => Assert.Equal(expected, RankTable.NameFor(tier));

    [Fact]
    public void Division_strips_sublevel()
        => Assert.Equal("Immortal", RankTable.Make(25, 40).Division);
}

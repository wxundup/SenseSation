namespace SenseSation.Core.Training;

/// <summary>
/// A target value for one performance metric, expressed as two thresholds:
/// <see cref="Good"/> (a healthy competitive level) and <see cref="Radiant"/>
/// (top-tier reference). Values are approximate, drawn from publicly reported
/// high-elo/Radiant aggregates — they are reference points for self-coaching,
/// not exact figures.
/// </summary>
public sealed record Benchmark(
    string Key,
    string Label,
    double Good,
    double Radiant,
    string Unit,
    bool HigherIsBetter,
    string Why);

public static class RadiantBenchmarks
{
    public static readonly IReadOnlyList<Benchmark> All =
    [
        new("hs_pct", "Headshot %", 22, 28, "%", true,
            "How many of your hits land on the head. Aim at head height and you kill faster. Higher is better."),
        new("kd", "K/D ratio", 1.05, 1.25, "", true,
            "Kills divided by deaths. Above 1 means you kill more than you die. Below 1 means you die too much."),
        new("adr", "Damage / round", 145, 165, "", true,
            "Average damage you deal each round. Hurting enemies helps your team finish them, even if you don't get the kill."),
        new("kast", "KAST %", 72, 78, "%", true,
            "How many rounds you actually helped in: you got a kill, an assist, stayed alive, or a teammate avenged your death."),
        new("first_kill_rate", "Opening kills / match", 3.5, 5.0, "", true,
            "How often you get the first kill of the round. Getting first blood usually wins the round for your team."),
        new("first_death_rate", "Opening deaths / match", 3.5, 2.5, "", false,
            "How often you die first. When you die first, your team has to fight 4-against-5. Lower is better."),
        new("multikill_rate", "Multi-kill rounds / match", 2.0, 3.0, "", true,
            "Rounds where you killed 2 or more enemies. Shows you keep fighting instead of dying after one kill."),
        new("econ_discipline", "Buying with team %", 80, 92, "%", true,
            "How often you buy together with your team. Buying a gun alone when your team is saving wastes money and loses rounds."),
    ];

    public static Benchmark Get(string key) => All.First(b => b.Key == key);
}

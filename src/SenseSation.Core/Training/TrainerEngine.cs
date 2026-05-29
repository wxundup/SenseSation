using SenseSation.Core.Models;

namespace SenseSation.Core.Training;

/// <summary>A gap between the player's value and the benchmark for one metric.</summary>
public sealed record Weakness
{
    public required Benchmark Benchmark { get; init; }
    public double Your { get; init; }
    public double Target { get; init; }
    public Severity Severity { get; init; }

    /// <summary>Signed shortfall expressed in the metric's own unit (always positive magnitude).</summary>
    public double Gap { get; init; }

    /// <summary>Blunt, value-aware critique in a Radiant-coach voice.</summary>
    public string Critique { get; init; } = "";
}

/// <summary>A concrete, actionable practice item tied to a weakness area.</summary>
public sealed record Drill(string Area, string Name, string How, string Where, int Minutes);

/// <summary>The full coaching output for a window of matches.</summary>
public sealed record TrainerReport
{
    public required PlayerMetrics Metrics { get; init; }
    public IReadOnlyList<Weakness> Weaknesses { get; init; } = [];
    public IReadOnlyList<Drill> Plan { get; init; } = [];
    public required string Headline { get; init; }
    public IReadOnlyList<string> Strengths { get; init; } = [];
}

/// <summary>
/// Rule-based coach with a Radiant bar and a blunt voice: it scores each metric
/// against <see cref="RadiantBenchmarks"/>, but the framing is critical — it tells
/// the player what they are doing wrong, not what they are doing well.
/// </summary>
public static class TrainerEngine
{
    public static TrainerReport Analyze(PlayerMetrics m)
    {
        var weaknesses = new List<Weakness>();

        foreach (var b in RadiantBenchmarks.All)
        {
            double v = m.ValueFor(b.Key);
            double gap = b.HigherIsBetter ? b.Good - v : v - b.Good;
            if (gap <= 0) continue; // meeting the bar — not a coaching point, we don't hand out praise

            double range = Math.Abs(b.Radiant - b.Good);
            double ratio = range <= 0 ? 1 : gap / range;
            var severity = ratio >= 1.0 ? Severity.Major : ratio >= 0.4 ? Severity.Notable : Severity.Minor;

            // A losing K/D or dying-first habit is always a major problem, regardless of the curve.
            if (b.Key == "kd" && v < 1.0) severity = Severity.Major;
            if (b.Key == "first_death_rate" && v >= b.Good + 1) severity = Severity.Major;

            weaknesses.Add(new Weakness
            {
                Benchmark = b,
                Your = v,
                Target = b.Good,
                Gap = Math.Round(gap, 2),
                Severity = severity,
                Critique = Critique(b.Key, v),
            });
        }

        var ranked = weaknesses
            .OrderByDescending(w => w.Severity)
            .ThenByDescending(w => w.Gap / Math.Max(1, Math.Abs(w.Benchmark.Radiant - w.Benchmark.Good)))
            .ToList();

        var plan = ranked
            .SelectMany(w => DrillCatalog.For(w.Benchmark.Key))
            .DistinctBy(d => d.Name)
            .Take(5)
            .ToList();

        return new TrainerReport
        {
            Metrics = m,
            Weaknesses = ranked,
            Plan = plan,
            Strengths = [], // no praise — this coach only points at problems
            Headline = Headline(m, ranked),
        };
    }

    private static string Headline(PlayerMetrics m, IReadOnlyList<Weakness> ranked)
    {
        if (m.MatchesAnalyzed == 0)
            return "Load some matches first. I can't coach what I can't see.";

        bool padsKast = m.Kast >= RadiantBenchmarks.Get("kast").Good;
        bool padsAdr = m.Adr >= RadiantBenchmarks.Get("adr").Good;
        bool losesDuels = m.Kd < RadiantBenchmarks.Get("kd").Good;
        bool noEntries = m.FirstKillsPerMatch < RadiantBenchmarks.Get("first_kill_rate").Good;

        // The classic stuck profile: stays alive and does damage, but loses the fights that win rounds.
        if ((padsKast || padsAdr) && losesDuels && noEntries)
            return $"Hard truth: your stats look okay ({m.Kast:0}% rounds helped, {m.Adr:0} damage a round) " +
                   $"but you lose the fights that matter — only {m.Kd:0.00} kills per death and {m.FirstKillsPerMatch:0.#} first kills a game. " +
                   "You stay alive and farm damage in rounds you've already lost. That's why you're stuck.";

        if (ranked.Count == 0)
            return "You clear the 'good' line on paper — but 'good' is the bottom bar, and every number here is still " +
                   "below Radiant level. Don't relax. Pick the closest gap and grind it.";

        var top = ranked[0];
        return $"You have {ranked.Count} weak spots across {m.MatchesAnalyzed} matches. Your biggest problem: " +
               $"{top.Benchmark.Label} (you: {Format(top.Your, top.Benchmark)}, need at least {Format(top.Target, top.Benchmark)}). " +
               "Fix that first or you stay exactly where you are.";
    }

    private static string Critique(string key, double v) => key switch
    {
        "kd" => $"You get {v:0.00} kills for every death — you die more than you kill. That hurts your team every round. " +
                "Only fight when you think you can win, and don't push into enemies alone.",
        "first_kill_rate" => $"You only get {v:0.#} first kills a game. You hang back and grab leftovers after teammates die. " +
                "Be the one who starts the fight, not the one who cleans up.",
        "first_death_rate" => $"You die first about {v:0.#} times a game. Every time, your team has to play 4-against-5. " +
                "Stop running into corners alone — wait, or push with a teammate.",
        "hs_pct" => $"Only {v:0.#}% of your hits are headshots. Your aim sits too low. " +
                "Keep your crosshair at head height — aim where heads will be, not at the body.",
        "adr" => $"You only do {v:0} damage a round. You're barely hurting anyone. " +
                "Shoot enemies even when you won't get the kill — chip damage helps your team finish them.",
        "kast" => $"You only help in {v:0}% of rounds. Too many rounds you do nothing. " +
                "Each round: get a kill, help a teammate get one, or at least stay alive.",
        "multikill_rate" => $"You only get 2+ kills in {v:0.#} rounds a game. You get one kill then die or hide. " +
                "After your first kill, keep going — there are usually more enemies to take.",
        "econ_discipline" => $"You only buy with your team {v:0}% of the time. Buying a gun alone when the team saves leaves everyone broke. " +
                "When they save, you save too.",
        _ => ""
    };

    private static string Format(double v, Benchmark b) =>
        b.Unit == "%" ? $"{v:0.#}%" : v.ToString("0.##");
}

/// <summary>Maps each benchmark key to concrete drills. Practical, not generic advice.</summary>
public static class DrillCatalog
{
    public static IReadOnlyList<Drill> For(string key) => key switch
    {
        "hs_pct" =>
        [
            new("Aim", "Aim at head height", "In the Practice Range, kill moving bots. After every kill, snap your crosshair back up to head height.", "Practice Range · Hard bots", 15),
            new("Aim", "Deathmatch, heads only", "Play Deathmatch. Don't care about winning — only check your crosshair is at head height before you shoot.", "Deathmatch", 20),
        ],
        "kd" =>
        [
            new("Duels", "Pick smarter fights", "Only fight when you can win or a teammate is right behind you. After each death ask: could I have won that?", "Unrated / watch your replay", 0),
            new("Aim", "Warm up first", "Do 5–10 min of aim training (or kill bots in the Range) before you play ranked.", "Aim Lab / Kovaak's", 10),
        ],
        "adr" =>
        [
            new("Impact", "Always do damage", "Shoot enemies even if you won't get the kill — chip damage lets your team finish them. Check your damage each match.", "Unrated", 0),
            new("Utility", "Learn 2 damage abilities", "Learn 2 damaging abilities (like mollies) per map for your agent and use them on enemies.", "Custom game", 15),
        ],
        "kast" =>
        [
            new("Positioning", "Don't die alone", "Hold spots where a teammate can avenge you if you die. Stop dying by yourself in random corners.", "Unrated / watch your replay", 0),
            new("Teamplay", "Do one thing each round", "Before each round, plan to do ONE thing: get a kill, help a teammate kill, or just stay alive.", "Unrated", 0),
        ],
        "first_kill_rate" =>
        [
            new("Entry", "Practice taking first contact", "In a custom game, practice peeking the common spots with a teammate ready behind you to back you up.", "Custom game", 15),
            new("Aim", "Stop, then shoot", "Come to a full stop before firing your first bullet — moving makes your shots miss.", "Practice Range", 10),
        ],
        "first_death_rate" =>
        [
            new("Discipline", "Stop dying first", "Wait 1–2 seconds, then peek with a teammate or after using an ability. Don't run in first by yourself.", "Watch your replay", 0),
            new("Info", "Peek to look, not to die", "Take a quick peek to spot enemies, then back off — instead of running all the way out alone.", "Unrated", 0),
        ],
        "multikill_rate" =>
        [
            new("Follow-up", "Keep going after a kill", "When a teammate dies and you avenge them, peek again fast to catch the next enemy.", "Unrated / watch your replay", 0),
            new("Aim", "Switch targets fast", "In the Range, kill one bot then quickly move your aim to the next. Don't let your crosshair sit still.", "Practice Range / Aim trainer", 15),
        ],
        "econ_discipline" =>
        [
            new("Money", "Buy with your team", "When your team says save, you save too. Never buy a gun alone on a save round.", "Unrated", 0),
            new("Money", "Learn when to save", "Simple rule: usually save the round after you lose, unless it's the 2nd round or you just won the pistol round.", "Learn / watch your replay", 10),
        ],
        _ => []
    };
}

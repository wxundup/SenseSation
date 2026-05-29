namespace SenseSation.Core.Training;

/// <summary>
/// One rank-specific mistake. <see cref="MetricKey"/> (a <see cref="Benchmark"/> key)
/// lets the UI flag the mistake as "confirmed by your stats" when the player is weak
/// in that metric — turning generic rank advice into personalized coaching.
/// </summary>
public sealed record RankTip(string Text, string? MetricKey = null);

/// <summary>Common mistakes + climb plan for one rank division.</summary>
public sealed record RankGuide(
    string Division,
    string Summary,
    IReadOnlyList<RankTip> Mistakes,
    IReadOnlyList<string> ClimbPlan,
    string NextRank);

/// <summary>
/// Rank-by-rank mistakes and climb advice (Iron → Radiant), distilled from
/// community coaching consensus. Combined with the player's measured metrics, it
/// highlights the mistakes their own data confirms.
/// </summary>
public static class RankPlaybook
{
    public static RankGuide For(string division) =>
        Guides.TryGetValue(division, out var g) ? g : Guides["Unranked"];

    private static readonly Dictionary<string, RankGuide> Guides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Unranked"] = new("Unranked",
            "Play your 5 placement games. Just focus on the basics: aim at head height and learn the maps.",
            [
                new("You shoot while moving — bullets fly everywhere. Stop moving, THEN shoot."),
                new("Your crosshair points at the ground. Keep it at head height.", "hs_pct"),
            ],
            ["Kill bots in the Practice Range for 10 min before playing", "Play Deathmatch to get better at shooting", "Pick one easy agent and learn one map"],
            "Iron"),

        ["Iron"] = new("Iron",
            "You're still learning. The basics matter way more than fancy plays right now.",
            [
                new("You shoot while running — that makes your aim terrible. Stop fully before shooting."),
                new("Your crosshair is too low. Keep it at head height and aim at corners before enemies show.", "hs_pct"),
                new("You don't know the maps yet, so enemies surprise you."),
                new("You empty your whole magazine far away. Far away, shoot one bullet at a time."),
            ],
            ["10 min of killing bots in the Range every day", "Play Deathmatch and stop crouching in fights", "Walk around every map once so you know the spots"],
            "Bronze"),

        ["Bronze"] = new("Bronze",
            "Your aim is still rough. Drop the bad habits before they become permanent.",
            [
                new("You crouch and spray in fights — that makes you an easy still target. Stand and stop before shooting."),
                new("Your crosshair isn't at head height on every corner.", "hs_pct"),
                new("You hold your abilities until you die. Use them to help take space."),
                new("You chase kills and forget about the spike (the bomb). Win the round, not just kills."),
            ],
            ["Practice stopping before you shoot", "Play Deathmatch with crouch turned off for a week", "Learn 2 ability spots per agent"],
            "Silver"),

        ["Silver"] = new("Silver",
            "Good aim still wins games here — but start thinking about the game, not just shooting.",
            [
                new("You don't use your abilities well, or use them too late."),
                new("You play too many agents. Pick 1–2 and get really good with them."),
                new("You push too aggressively with no teammate to back you up.", "first_death_rate"),
                new("Your aim drops when you're under pressure.", "hs_pct"),
            ],
            ["Pick 1–2 agents and learn their abilities", "Use an ability BEFORE you peek a corner", "Warm up in Deathmatch before every session"],
            "Gold"),

        ["Gold"] = new("Gold",
            "Your aim is okay, but your decisions are costing you. Smart play now matters more than aim.",
            [
                new("You peek too wide and take random 1-on-1 fights you don't need.", "first_death_rate"),
                new("You lose the fights you start because you go in with no plan or backup.", "kd"),
                new("You rarely get the first kill — you never take space first.", "first_kill_rate"),
                new("You buy a gun alone when your team is saving, wasting money.", "econ_discipline"),
                new("Your team plays like 5 strangers — no talking, no backing each other up."),
            ],
            ["Play for the team and back each other up, don't solo-frag", "Learn when to buy and when to save", "Watch the replay and look at how you died", "Use 2–3 agents per role"],
            "Platinum"),

        ["Platinum"] = new("Platinum",
            "This is the big traffic jam. Patience and teamwork get you out.",
            [
                new("You peek the same aggressive way every time, so good players punish you.", "first_death_rate"),
                new("You peek corners with no ability to help clear them first."),
                new("Bad team talk — people use the same ability twice or don't back each other up."),
                new("You run the same attack every round and don't change it up."),
            ],
            ["Play slower — get info before committing", "Always peek with a teammate, never alone", "Keep your callouts short and clear", "Fake attacks and take control of the map"],
            "Diamond"),

        ["Diamond"] = new("Diamond",
            "Strong lobbies. Trying to win alone holds you back here — play with your team.",
            [
                new("You try to kill the whole enemy team alone instead of fighting with teammates.", "kd"),
                new("You get one kill then die or hide instead of finishing.", "multikill_rate"),
                new("You use abilities on your own instead of combining them with your team."),
                new("You don't warm up — your aim is up and down day to day.", "hs_pct"),
            ],
            ["Warm up your aim every day (trainer + Deathmatch)", "Use abilities together with your team's pushes", "When a teammate dies, peek fast to avenge them", "Stick to one role and get good at it"],
            "Ascendant"),

        ["Ascendant"] = new("Ascendant",
            "You're better than ~90% of players. Now it's about fixing your own specific weak spots.",
            [
                new("You have bad habits you've never checked by watching your replays."),
                new("You don't have a clear job on the team."),
                new("Your timing on taking first fights is inconsistent.", "first_kill_rate"),
                new("You do damage but lose the actual fights.", "kd"),
            ],
            ["Watch your replay and study your deaths every session", "Pick a clear role and own it", "Drill your single worst skill", "Watch one pro who mains your agent"],
            "Immortal"),

        ["Immortal"] = new("Immortal",
            "You barely have weaknesses left. Small improvements and lots of games from here.",
            [
                new("You keep dying the same way — small repeated mistakes."),
                new("You lean on raw aim instead of smart setups and info.", "hs_pct"),
                new("You tilt and make worse decisions over long sessions."),
                new("Your first-kill impact is low for this level.", "first_kill_rate"),
            ],
            ["Find your 1–2 worst stats and grind them", "Study pro replays of your agent", "Stop playing after 2 losses in a row", "Play a lot of games consistently"],
            "Radiant"),

        ["Radiant"] = new("Radiant",
            "Top 500 in your region. Now it's about staying there and keeping a sharp head.",
            [
                new("Going on autopilot and getting lazy in ranked."),
                new("Not keeping up when the best agents/maps change."),
                new("Letting your mood swing with your RR."),
            ],
            ["Play your best agents and maps to hold your RR", "Keep up with what's strong right now", "Look for scrims or a team", "Protect your mindset and sleep"],
            "—"),
    };
}

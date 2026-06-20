namespace SenseSation.Web.Services;

/// <summary>Static agent metadata for the picker grid: colors, roles, display names.</summary>
public static class AgentInfo
{
    public record Record(string Name, string Role, string Color, string ColorDark);

    public static readonly IReadOnlyList<Record> All =
    [
        new("Astra", "Controller", "#8b36d7", "#3a1557"),
        new("Breach", "Initiator", "#c7561c", "#3d1808"),
        new("Brimstone", "Controller", "#e68a2e", "#4a2906"),
        new("Chamber", "Sentinel", "#cba540", "#3d3008"),
        new("Clove", "Controller", "#a35ed6", "#301548"),
        new("Cypher", "Sentinel", "#6d9eaf", "#1a2e36"),
        new("Deadlock", "Sentinel", "#7bb8b0", "#1a342e"),
        new("Fade", "Initiator", "#59698d", "#141f2c"),
        new("Gekko", "Initiator", "#84b542", "#283a0e"),
        new("Harbor", "Controller", "#3d8a99", "#0e2830"),
        new("ISO", "Duelist", "#7853c4", "#211242"),
        new("Jett", "Duelist", "#9be3f5", "#143a48"),
        new("KAY/O", "Initiator", "#5a5c6c", "#181820"),
        new("Killjoy", "Sentinel", "#f5d85c", "#4a3e06"),
        new("Miks", "Duelist", "#e0559a", "#401028"),
        new("Neon", "Duelist", "#2868e0", "#0a1a44"),
        new("Omen", "Controller", "#4e5ba3", "#141a32"),
        new("Phoenix", "Duelist", "#f7b25c", "#4a2e06"),
        new("Raze", "Duelist", "#e07030", "#441a04"),
        new("Reyna", "Duelist", "#c25ac2", "#361238"),
        new("Sage", "Sentinel", "#5aa389", "#142e24"),
        new("Skye", "Initiator", "#84b543", "#283a0e"),
        new("Sova", "Initiator", "#305ec4", "#0a1640"),
        new("Tejo", "Initiator", "#c4a030", "#362a08"),
        new("Viper", "Controller", "#5a9654", "#142e12"),
        new("Veto", "Duelist", "#c85050", "#381010"),
        new("Vyse", "Sentinel", "#8860b4", "#281438"),
        new("Waylay", "Duelist", "#60b088", "#143424"),
        new("Yoru", "Duelist", "#3476c7", "#082030"),
    ];

    public static Record? Get(string name) =>
        All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string BodyStyle(string name, bool locked)
    {
        var at = Get(name);
        string c = at?.Color ?? "#4a5568";
        string cd = at?.ColorDark ?? "#1a1a20";
        return locked
            ? $"background:linear-gradient(135deg, {c}, {cd});color:#fff;border:2px solid {c};box-shadow:0 0 16px {c}66"
            : $"background:linear-gradient(180deg, rgba(255,255,255,.05), rgba(255,255,255,.02));border:1px solid var(--line)";
    }
}

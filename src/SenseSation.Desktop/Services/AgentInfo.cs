using System;
using System.Collections.Generic;
using System.Linq;

namespace SenseSation.Desktop.Services;

/// <summary>Static agent metadata for the picker grid.</summary>
public static class AgentInfo
{
    public record Record(string Name, string Role);

    public static readonly IReadOnlyList<Record> All =
    [
        new("Astra", "Controller"), new("Breach", "Initiator"), new("Brimstone", "Controller"),
        new("Chamber", "Sentinel"), new("Clove", "Controller"), new("Cypher", "Sentinel"),
        new("Deadlock", "Sentinel"), new("Fade", "Initiator"), new("Gekko", "Initiator"),
        new("Harbor", "Controller"), new("Iso", "Duelist"), new("Jett", "Duelist"),
        new("KAYO", "Initiator"), new("Killjoy", "Sentinel"), new("Miks", "Duelist"),
        new("Neon", "Duelist"), new("Omen", "Controller"), new("Phoenix", "Duelist"),
        new("Raze", "Duelist"), new("Reyna", "Duelist"), new("Sage", "Sentinel"),
        new("Skye", "Initiator"), new("Sova", "Initiator"), new("Tejo", "Initiator"),
        new("Veto", "Duelist"), new("Viper", "Controller"), new("Vyse", "Sentinel"),
        new("Waylay", "Duelist"), new("Yoru", "Duelist"),
    ];

    public static string Role(string name) =>
        All.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Role ?? "";
}

namespace SenseSation.Core.Models;

public enum MatchResult { Win, Loss, Draw }

public enum Team { Blue, Red, Unknown }

/// <summary>Round buy classification, derived from loadout credit value.</summary>
public enum BuyType { Save, Eco, ForceBuy, FullBuy, Unknown }

/// <summary>Severity of a gap between the player's metric and the benchmark.</summary>
public enum Severity { Minor, Notable, Major }

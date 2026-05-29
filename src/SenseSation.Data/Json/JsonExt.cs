using System.Text.Json;

namespace SenseSation.Data.Json;

/// <summary>
/// Defensive accessors over <see cref="JsonElement"/>. HenrikDev's payload shape
/// differs subtly between API versions, so we read fields by probing rather than
/// binding to rigid DTOs — a missing field yields a default instead of throwing.
/// </summary>
internal static class JsonExt
{
    public static bool TryProp(this JsonElement el, string name, out JsonElement value)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    /// <summary>Reads the first present property among <paramref name="names"/>.</summary>
    public static JsonElement Prop(this JsonElement el, params string[] names)
    {
        foreach (var n in names)
            if (el.TryProp(n, out var v)) return v;
        return default;
    }

    public static string Str(this JsonElement el, string name, string fallback = "")
    {
        var v = el.Prop(name);
        return v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    }

    public static int Int(this JsonElement el, string name, int fallback = 0)
    {
        var v = el.Prop(name);
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var l) => (int)l,
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => fallback
        };
    }

    public static long Long(this JsonElement el, string name, long fallback = 0)
    {
        var v = el.Prop(name);
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : fallback;
    }

    public static bool Bool(this JsonElement el, string name, bool fallback = false)
    {
        var v = el.Prop(name);
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    public static IEnumerable<JsonElement> Arr(this JsonElement el, string name)
    {
        var v = el.Prop(name);
        return v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];
    }
}

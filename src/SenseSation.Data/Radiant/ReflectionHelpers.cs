using System.Collections;
using System.Reflection;

namespace SenseSation.Data.Radiant;

/// <summary>
/// Small reflective readers used to pull a few fields out of RadiantConnect's
/// strongly-typed (but version-volatile) DTOs without binding to their exact
/// shape at compile time. Keeps the live adapter resilient to SDK updates.
/// </summary>
internal static class ReflectionHelpers
{
    /// <summary>
    /// Walks an object graph (records/objects, breadth-limited) looking for an
    /// integer property whose name matches one of <paramref name="names"/>.
    /// Used to find the competitive tier wherever the SDK nests it.
    /// </summary>
    public static int? FindInt(object? root, string[] names, int maxDepth = 4)
    {
        if (root is null) return null;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object obj, int depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (obj, depth) = queue.Dequeue();
            if (obj is null || !seen.Add(obj)) continue;
            var type = obj.GetType();
            if (type.IsPrimitive || obj is string) continue;

            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object? val;
                try { val = p.GetValue(obj); } catch { continue; }
                if (val is null) continue;

                // Riot/RadiantConnect store tiers as Int64; match int/long/short.
                if (names.Any(n => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                {
                    switch (val)
                    {
                        case int i when i > 0: return i;
                        case long l when l > 0: return (int)l;
                        case short s when s > 0: return s;
                    }
                }

                if (depth < maxDepth && val is not string && val.GetType().IsClass)
                    queue.Enqueue((val, depth + 1));
            }
        }
        return null;
    }

    /// <summary>Reads a string property by trying several candidate names.</summary>
    public static string? GetString(object? obj, params string[] names)
    {
        if (obj is null) return null;
        var type = obj.GetType();
        foreach (var n in names)
        {
            var p = type.GetProperty(n, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p?.GetValue(obj) is string s) return s;
        }
        return null;
    }

    /// <summary>Enumerates an object that is (or exposes) an IEnumerable.</summary>
    public static IEnumerable<object> AsEnumerable(object? obj)
    {
        if (obj is IEnumerable e and not string)
            foreach (var item in e)
                if (item is not null) yield return item;
    }
}

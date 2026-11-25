using System.Text.Json;

namespace soft.square.net.ContentDesigner.Rendering;

/// <summary>
/// The token values of one element per breakpoint, written as the editorial pack's attributes:
/// <c>data-mb-{token}</c> for the base (Mobile) and <c>data-mb-{token}-{sm|md|lg|xl}</c> where a wider breakpoint
/// changes it. A wider breakpoint inherits what it does not set, as the pack's CSS does, so a value equal to the one in
/// force below it is left out.
/// </summary>
public sealed class TokenMap
{
    /// <summary>The breakpoints, narrowest first; "" is the base.</summary>
    public static IReadOnlyList<string> Breakpoints { get; } = ["", "sm", "md", "lg", "xl"];

    public const int Base = 0, Sm = 1, Md = 2, Lg = 3, Xl = 4;

    /// <summary>Tokens whose base value is the CSS default and needs no attribute.</summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal) { ["hidden"] = "false" };

    private readonly SortedDictionary<string, string?[]> _values = new(StringComparer.Ordinal);

    public void Set(string token, int breakpoint, string? value)
    {
        if (value is null) return;
        if (!_values.TryGetValue(token, out var slots)) _values[token] = slots = new string?[Breakpoints.Count];
        slots[breakpoint] = value;
    }

    public void SetAll(IReadOnlyDictionary<string, string>? tokens, int breakpoint)
    {
        if (tokens is null) return;
        foreach (var (token, value) in tokens) Set(token, breakpoint, value);
    }

    /// <summary>
    /// A DynamicFields style object: <c>{ "width": "full" }</c> or, responsive, <c>{ "width": { "base": "full", "md": "half" } }</c>.
    /// With <paramref name="baseOnly"/> (AMP) only the base values.
    /// </summary>
    public void SetStyle(JsonElement style, bool baseOnly = false)
    {
        if (style.ValueKind != JsonValueKind.Object) return;
        foreach (var entry in style.EnumerateObject())
        {
            if (!IsToken(entry.Name)) continue;
            if (entry.Value.ValueKind == JsonValueKind.String) Set(entry.Name, Base, entry.Value.GetString());
            else if (entry.Value.ValueKind == JsonValueKind.Object)
                foreach (var at in entry.Value.EnumerateObject())
                {
                    var bp = at.Name == "base" ? Base : Array.IndexOf([.. Breakpoints], at.Name);
                    if (bp < 0 || (baseOnly && bp != Base) || at.Value.ValueKind != JsonValueKind.String) continue;
                    Set(entry.Name, bp, at.Value.GetString());
                }
        }
    }

    public string? Get(string token, int breakpoint)
    {
        if (!_values.TryGetValue(token, out var slots)) return null;
        string? current = null;
        for (var bp = 0; bp <= breakpoint; bp++) current = slots[bp] ?? current;
        return current;
    }

    /// <summary>The attributes, without redundant ones.</summary>
    public Dictionary<string, object> Attributes()
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (token, slots) in _values)
        {
            var current = Defaults.GetValueOrDefault(token);
            for (var bp = 0; bp < slots.Length; bp++)
            {
                if (slots[bp] is not { } value || value == current) continue;
                result[bp == Base ? $"data-mb-{token}" : $"data-mb-{token}-{Breakpoints[bp]}"] = value;
                current = value;
            }
        }
        return result;
    }

    /// <summary>A token name the pack knows (letters and dashes); anything else is never written.</summary>
    private static bool IsToken(string name) => name.Length is > 0 and <= 32 && name.All(c => c is >= 'a' and <= 'z' or '-');
}

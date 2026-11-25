namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

/// <summary>
/// The style tokens a design may set: exactly the ones of UI.MudTail's editorial pack (<c>_style-tokens.scss</c>),
/// which DynamicFields' style presets also use. A renderer writes each as a <c>data-mb-{token}[-md|-lg]</c> attribute;
/// there are no free classes and no free CSS. Order, span and hidden are placement fields, not tokens.
/// </summary>
public static class StyleTokens
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Values { get; } = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["width"] = ["content", "wide", "full", "half", "third"],
        ["align"] = ["start", "center", "end"],
        ["spacing"] = ["none", "sm", "md", "lg", "xl"],
        ["tone"] = ["default", "muted", "accent", "inverse"],
        ["size"] = ["sm", "md", "lg", "xl"],
        ["level"] = ["h1", "h2", "h3", "h4", "h5", "h6"],
        ["aspect"] = ["auto", "1-1", "4-3", "3-2", "16-9", "21-9", "3-4", "9-16"],
        ["columns"] = ["1", "2", "3", "4", "5", "6", "12"],
        ["layout"] = ["standard", "wide", "full"]
    };

    public const int MaxSpan = 12;
    public const int MaxOrder = 24;
    public const int MaxTokens = 20;

    public static bool IsKnown(string token, string value) =>
        Values.TryGetValue(token, out var values) && values.Contains(value);

    /// <summary>A span: 1 to 12 columns, or <c>full</c>.</summary>
    public static bool IsSpan(string? span) =>
        span is null || span == "full" || (int.TryParse(span, out var n) && n is >= 1 and <= MaxSpan && n.ToString() == span);

    /// <summary>Throws when a token set is not one the editorial pack styles.</summary>
    public static IReadOnlyDictionary<string, string> Checked(IReadOnlyDictionary<string, string>? tokens, string where)
    {
        if (tokens is null || tokens.Count == 0) return Empty;
        if (tokens.Count > MaxTokens) throw new DomainException($"{where} has more than {MaxTokens} style tokens.");
        foreach (var (token, value) in tokens)
            if (!IsKnown(token, value))
                throw new DomainException(Values.ContainsKey(token)
                    ? $"{where}: '{value}' is not a value of the style token '{token}' ({string.Join(", ", Values[token])})."
                    : $"{where}: '{token}' is not a style token ({string.Join(", ", Values.Keys)}).");
        return new Dictionary<string, string>(tokens, StringComparer.Ordinal);
    }

    public static IReadOnlyDictionary<string, string> Empty { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

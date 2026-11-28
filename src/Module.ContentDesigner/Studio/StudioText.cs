using System.Text.Json;
using soft.square.net.WebApi.Modules.ContentDesigner.Client;

namespace soft.square.net.Module.ContentDesigner.Studio;

/// <summary>How the panels name things: a part, a content, a region, a feed event.</summary>
public static class StudioText
{
    /// <summary>"Headline", "Paragraph: Doors opened at…", "Row · row-1", "Items (the other contents)".</summary>
    public static string Title(StudioSession session, string id)
    {
        if (DesignEditor.IsRegion(id))
        {
            var regionId = DesignEditor.RegionId(id);
            var region = session.Current is { } view ? DesignEditor.FindRegion(view, regionId) : null;
            return region is null ? $"Region {regionId}" : $"{region.Kind} · {region.Id}";
        }
        if (id == "items") return "Items (the other contents, in reading order)";
        if (id.StartsWith("prop:", StringComparison.Ordinal))
        {
            var key = id["prop:".Length..];
            return session.Workspace?.KindValues.FirstOrDefault(v => v.Key == key)?.Label ?? key;
        }
        if (id.StartsWith("content:", StringComparison.Ordinal)) return Content(session, id["content:".Length..]);
        return id;
    }

    /// <summary>A content: its field's label and the start of its first text.</summary>
    public static string Content(StudioSession session, string contentId)
    {
        var content = session.Workspace?.Contents.FirstOrDefault(c => c.ContentId == contentId);
        var label = content?.FieldLabel ?? (content is null ? "Content" : RenderMapping.Label(content.FieldKey));
        var block = session.Document?.Contents.FirstOrDefault(b => b.Id == contentId);
        var text = block is null ? null : FirstText(block.Content);
        return text is null ? label : $"{label}: {Shorten(text, 48)}";
    }

    public static string? FirstText(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in content.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } s && !LooksLikeId(s)) return s;
            if (property.Value.ValueKind == JsonValueKind.Array)
                foreach (var item in property.Value.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } t && !LooksLikeId(t)) return t;
        }
        return null;
    }

    private static bool LooksLikeId(string s) => s.StartsWith("http", StringComparison.OrdinalIgnoreCase) || Guid.TryParse(s, out _) || s.StartsWith("asset-", StringComparison.Ordinal);

    public static string Shorten(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>"content.updated" → "updated"; with the content it is about.</summary>
    public static string Event(StudioSession session, FeedEvent e)
    {
        var what = e.Type.Contains('.') ? e.Type[(e.Type.IndexOf('.') + 1)..].Replace('-', ' ') : e.Type;
        var about = e.ContentId is { } id ? Content(session, id) : "the post";
        if (e.Type == "content.validation-failed") return $"{about}: refused by Content's validation";
        return e.Stage is { Length: > 0 } stage ? $"{about}: {what} ({Stage(stage)})" : $"{about}: {what}";
    }

    /// <summary>The newsroom stages of a content, in the order work moves through them.</summary>
    public static readonly string[] Stages = ["assigned", "in-progress", "submitted", "in-review", "accepted", "returned"];

    /// <summary>"in-progress" → "in progress".</summary>
    public static string Stage(string stage) => stage.Replace('-', ' ').ToLowerInvariant();

    /// <summary>A unit's name in the UI's language; its id's start when Organization did not name it.</summary>
    public static string Unit(Guid unitId, string? nameEn, string? nameAr)
    {
        var arabic = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var name = arabic ? nameAr ?? nameEn : nameEn ?? nameAr;
        return string.IsNullOrWhiteSpace(name) ? $"unit {unitId.ToString()[..8]}" : name;
    }

    /// <summary>A stage's badge style: the theme's palette, by how far the content is.</summary>
    public static string StageClass(string stage) => stage.ToLowerInvariant() switch
    {
        "accepted" or "published" or "approved" => "cd-stage cd-stage--done",
        "rejected" or "returned" => "cd-stage cd-stage--back",
        "submitted" or "review" or "in-review" => "cd-stage cd-stage--review",
        "in-progress" => "cd-stage cd-stage--work",
        _ => "cd-stage"
    };

    public static string When(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm:ss");
}

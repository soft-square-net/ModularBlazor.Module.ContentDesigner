using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using soft.square.net.ContentDesigner.Rendering.Model;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;

/// <summary>
/// <see cref="IFieldsApi"/> over DynamicFields' HTTP API (<c>/api/dynamicfields</c>). Published field versions never
/// change, so they are cached per tenant for a day.
/// </summary>
public sealed class FieldsApi(Gateway gateway, IMemoryCache cache, ITenantContext tenant) : IFieldsApi
{
    private const string Service = "DynamicFields";

    private sealed record PropertyWire(string Key, string? Label, JsonElement Kind, bool Required, bool Multiple, string? SchemaProperty);

    private sealed record StyleWire(string Key, List<string>? Values, bool Responsive);

    private sealed record SnapshotWire(string Key, int Version, string? Title, string? SchemaType, List<PropertyWire>? Properties, List<StyleWire>? Style);

    private sealed record VersionWire(SnapshotWire Field);

    private sealed record IssueWire(string Path, string Message);

    private sealed record ReportWire(bool Valid, List<IssueWire>? Issues);

    private sealed record JsonLdWire(JsonElement JsonLd, List<IssueWire>? Skipped);

    public async Task<FieldVersionInfo?> GetFieldVersionAsync(string key, int version, CancellationToken ct)
    {
        var cacheKey = $"contentdesigner-field|{tenant.TenantId}|{key}|{version}";
        if (version > 0 && cache.TryGetValue(cacheKey, out FieldVersionInfo? hit)) return hit;

        var wire = await Gateway.ReadAsync<VersionWire>(Service,
            await gateway.SendAsync(Service, HttpMethod.Get, $"/api/dynamicfields/fields/{Uri.EscapeDataString(key)}/versions/{version}", null, ct), ct);
        if (wire?.Field is not { } field) return null;

        var info = new FieldVersionInfo(field.Key, field.Version, field.SchemaType ?? "",
            [.. (field.Properties ?? []).Select(p => new FieldValueInfo(p.Key, p.Label ?? p.Key, Kind(p.Kind), p.Required, p.Multiple, p.SchemaProperty))],
            [.. (field.Style ?? []).Select(s => new FieldStyleInfo(s.Key, s.Values ?? [], s.Responsive))],
            string.IsNullOrWhiteSpace(field.Title) ? null : field.Title);
        if (version > 0)
            cache.Set(cacheKey, info, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1), Size = 1 });
        return info;
    }

    public async Task<IReadOnlyList<ContentIssue>> ValidateAsync(JsonNode root, CancellationToken ct)
    {
        using var response = await gateway.SendAsync(Service, HttpMethod.Post, "/api/dynamicfields/content/validate", new { root }, ct);
        if (!response.IsSuccessStatusCode) throw await Gateway.FailureAsync(Service, response, ct);
        var report = await response.Content.ReadFromJsonAsync<ReportWire>(Gateway.Json, ct);
        return [.. (report?.Issues ?? []).Select(i => new ContentIssue(i.Path, i.Message))];
    }

    public async Task<JsonLdResult?> BuildJsonLdAsync(JsonNode root, string? url, string? language, IReadOnlyDictionary<string, RenderAsset>? assets, CancellationToken ct)
    {
        var body = new
        {
            root,
            assets = assets?.ToDictionary(a => a.Key, a => new { url = a.Value.Url, width = a.Value.Width, height = a.Value.Height, encodingFormat = a.Value.EncodingFormat }),
            url,
            language
        };
        using var response = await gateway.SendAsync(Service, HttpMethod.Post, "/api/dynamicfields/content/jsonld", body, ct);
        // An anonymous caller may not read DynamicFields, and a kind may have no published version: no JSON-LD then.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw await Gateway.FailureAsync(Service, response, ct);
        var wire = await response.Content.ReadFromJsonAsync<JsonLdWire>(Gateway.Json, ct);
        return wire is null ? null : new JsonLdResult(wire.JsonLd.GetRawText(), [.. (wire.Skipped ?? []).Select(i => new ContentIssue(i.Path, i.Message))]);
    }

    /// <summary>A property kind, written as its name (Text, Asset, ...) or its number.</summary>
    private static string Kind(JsonElement kind) => kind.ValueKind switch
    {
        JsonValueKind.String => kind.GetString() ?? "Text",
        JsonValueKind.Number when kind.TryGetInt32(out var n) => n switch
        {
            0 => "Text", 1 => "RichText", 2 => "Integer", 3 => "Decimal", 4 => "Boolean", 5 => "Date", 6 => "DateTime",
            7 => "Url", 8 => "Choice", 9 => "Asset", 10 => "Keyword", 11 => "Term", _ => "Text"
        },
        _ => "Text"
    };
}

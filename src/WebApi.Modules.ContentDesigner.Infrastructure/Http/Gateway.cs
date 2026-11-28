using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;

/// <summary>
/// Calls another module's API through the gateway (<c>ContentDesigner:ApiBaseUrl</c>), never in process: modules load
/// in isolated contexts. The call carries the caller's tenant (its <c>X-Tenant</c> header, else the resolved tenant)
/// and the caller's access token; an anonymous caller (a public page) uses the service client when one is configured.
/// </summary>
public sealed class Gateway(HttpClient http, IHttpContextAccessor accessor, ITenantContext tenant, ServiceTokenSource service,
    IOptions<ContentDesignerOptions> options)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary><paramref name="bearer"/>, when given, is the token the call carries instead of the caller's.</summary>
    public async Task<HttpResponseMessage> SendAsync(string service, HttpMethod method, string path, object? body, CancellationToken ct,
        string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, options.Value.ApiBaseUrl.TrimEnd('/') + path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);

        var incoming = accessor.HttpContext?.Request;
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        else if (incoming?.Headers.Authorization.ToString() is { Length: > 0 } authorization)
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        else if (await ServiceTokenAsync(ct) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var tenantId = incoming?.Headers[TenancyNames.Header].ToString() is { Length: > 0 } header ? header : tenant.TenantId;
        if (tenantId is not null) request.Headers.TryAddWithoutValidation(TenancyNames.Header, tenantId);

        try { return await http.SendAsync(request, ct); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new ExternalApiException(service, null, e.Message);
        }
    }

    /// <summary>The module's service token; null when no service client is configured or it cannot get one.</summary>
    public async Task<string?> ServiceTokenAsync(CancellationToken ct)
    {
        try { return await service.GetAsync(ct); }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or JsonException) { return null; }
    }

    /// <summary>The body as <typeparamref name="T"/>, null on 404; any other failure throws <see cref="ExternalApiException"/>.</summary>
    public static async Task<T?> ReadAsync<T>(string service, HttpResponseMessage response, CancellationToken ct) where T : class
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw await FailureAsync(service, response, ct);
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
    }

    public static async Task<ExternalApiException> FailureAsync(string service, HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        var detail = text;
        List<ContentValidationProblem> problems = [];
        try
        {
            using var problem = JsonDocument.Parse(text);
            if (problem.RootElement.TryGetProperty("detail", out var d) && d.GetString() is { Length: > 0 } s) detail = s;
            else if (problem.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } title) detail = title;
            problems = Problems(problem.RootElement);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { }
        return new ExternalApiException(service, (int)response.StatusCode, detail.Length > 300 ? detail[..300] : detail, problems);
    }

    /// <summary>The <c>problems</c> of a 422 (<c>[{ path, message, nodeId }]</c>): DynamicFields' findings, as Content passes them on.</summary>
    private static List<ContentValidationProblem> Problems(JsonElement problem)
    {
        List<ContentValidationProblem> found = [];
        if (problem.ValueKind != JsonValueKind.Object || !problem.TryGetProperty("problems", out var list) || list.ValueKind != JsonValueKind.Array)
            return found;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string? Text(string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (Text("message") is { Length: > 0 } message) found.Add(new ContentValidationProblem(Text("path") ?? "", message, Text("nodeId")));
        }
        return found;
    }
}

/// <summary>
/// The module's own access token (client credentials, <c>ContentDesigner:Service*</c> settings), for calls made for an
/// anonymous visitor. None when the settings are empty. Cached until a minute before it expires.
/// </summary>
public sealed class ServiceTokenSource(IHttpClientFactory clients, IOptions<ContentDesignerOptions> options)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    public async Task<string?> GetAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.ServiceAuthority) || string.IsNullOrWhiteSpace(o.ServiceClientId) || string.IsNullOrWhiteSpace(o.ServiceClientSecret))
            return null;
        if (_token is not null && _expires > DateTimeOffset.UtcNow.AddMinutes(1)) return _token;
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && _expires > DateTimeOffset.UtcNow.AddMinutes(1)) return _token;
            using var http = clients.CreateClient(nameof(ServiceTokenSource));
            using var response = await http.PostAsync($"{o.ServiceAuthority.TrimEnd('/')}/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = o.ServiceClientId,
                ["client_secret"] = o.ServiceClientSecret,
                ["scope"] = "api"
            }), ct);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _token = json.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("The identity service returned no token.");
            _expires = DateTimeOffset.UtcNow.AddSeconds(json.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 300);
            return _token;
        }
        finally { _lock.Release(); }
    }
}

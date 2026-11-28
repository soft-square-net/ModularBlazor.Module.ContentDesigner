using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases;
using soft.square.net.WebApi.Modules.ContentDesigner.UseCases.Ports;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Http;

/// <summary>
/// <see cref="IOrganizationApi"/> over Organization's local read API (the deployment's replica of the org tree), through
/// the gateway (<c>{ContentDesigner:ApiBaseUrl}/api/organization/</c>) with <see cref="HttpUnitDirectory"/>, never in
/// process. The call carries the caller's access token and tenant. Names only, so every failure (Organization not
/// installed, unreachable, or refusing the caller) answers empty: the studio then shows ids.
/// </summary>
public sealed class OrganizationApi(IHttpClientFactory clients, IHttpContextAccessor accessor, ITenantContext tenant,
    IOptions<ContentDesignerOptions> options, ILogger<OrganizationApi> logger) : IOrganizationApi
{
    public const string ClientName = "contentdesigner-organization";
    public const string Route = "/api/organization/";

    public Task<IReadOnlyList<UnitName>> ListUnitsAsync(CancellationToken ct) =>
        AskAsync<UnitName>(async directory =>
            [.. (await directory.ListAsync(ct: ct)).Select(u => new UnitName(u.Id, u.NameEn, u.NameAr))], ct);

    public Task<IReadOnlyList<UnitMemberName>> MembersAsync(Guid unitId, CancellationToken ct) =>
        AskAsync<UnitMemberName>(async directory =>
            [.. (await directory.MembersOfAsync(unitId, ct: ct))
                .Where(m => !string.IsNullOrEmpty(m.Subject) && !string.IsNullOrWhiteSpace(m.DisplayName))
                .Select(m => new UnitMemberName(m.Subject!, m.DisplayName!))], ct);

    private async Task<IReadOnlyList<T>> AskAsync<T>(Func<IUnitDirectory, Task<IReadOnlyList<T>>> ask, CancellationToken ct)
    {
        var baseUrl = options.Value.ApiBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) return [];
        try
        {
            using var http = clients.CreateClient(ClientName);
            http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + Route);
            var incoming = accessor.HttpContext?.Request;
            if (incoming?.Headers.Authorization.ToString() is { Length: > 0 } authorization)
                http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
            var tenantId = incoming?.Headers[TenancyNames.Header].ToString() is { Length: > 0 } header ? header : tenant.TenantId;
            if (tenantId is not null) http.DefaultRequestHeaders.TryAddWithoutValidation(TenancyNames.Header, tenantId);
            return await ask(new HttpUnitDirectory(http));
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or NotSupportedException or UriFormatException
                                      || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogDebug(e, "Organization did not answer; showing ids instead of names.");
            return [];
        }
    }
}

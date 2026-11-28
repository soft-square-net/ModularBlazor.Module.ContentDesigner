// ContentDesigner playground: a small platform with this repo's module in it, for local development. Two loops:
//
//   dev (default, launch profile "dev"): the WebApi loads the module, and its client parts, straight from their build
//     output (Modules:Local and ClientModules:Local, Development only). Change code, rebuild, restart the "webapi" resource in the dashboard (or F5 again): the new
//     build is installed and active, with no packing or uploading.
//   install (launch profile "install"): the platform starts without the module, as in production. Every build zips
//     the module to playground/out/contentdesigner.zip and copies its client parts to playground/out/client; upload the zip on
//     the client's Host Modules page (http://localhost:5070, sign in as the dev platform admin) and activate it, then
//     the client parts on the Client Modules page: the API client's dll first, then Module.ContentDesigner.zip (the UI
//     and its private ContentDesigner.Rendering.dll). This runs the real install path before a release.
//
//   postgres ─► identity (5090)    tenant store + tenant databases + identity database, one server
//            └► webapi (5080)  ◄── blzwebclient (5070)
//
// The platform's projects build from the ModularBlazor.Platform checkout next to this repo (ContentDesigner.Playground.csproj).
// It uses the same ports as the platform's Hybird lab (the client and identity are registered for them), so stop the
// lab, and any other module's playground, first. Data stays in the Docker volume "contentdesigner-playground-data" and in
// playground/.data (the module catalogs).

using System.Reflection;

var builder = DistributedApplication.CreateBuilder(args);

var mode = (builder.Configuration["Playground:Mode"] ?? "dev").Trim().ToLowerInvariant();
if (mode is not ("dev" or "install"))
    throw new InvalidOperationException($"Playground:Mode '{mode}' is not supported: use dev or install.");

// Fixed by the platform: the identity service registers the client's callbacks on 5070 and the client calls the API on
// 5080 (the Hybird gateway's port; here the one WebApi answers on it directly).
const int IdentityPort = 5090;
const int WebApiPort = 5080;
const int ClientPort = 5070;
const string Issuer = "http://localhost:5090/";
const string ClientOrigin = "http://localhost:5070";

// One PostgreSQL server, named like the lab's ("tenant-data"): the tenant store, the identity database and the tenants'
// databases. In Development the platform seeds the tenants acme and globex on it.
var tenantData = builder.AddPostgres("tenant-data")
    .WithDataVolume("contentdesigner-playground-data")
    .WithPgAdmin();
var tenantStore = tenantData.AddDatabase("TenantStore", databaseName: "PlaygroundTenants");
var identityDb = tenantData.AddDatabase("identity-db", databaseName: "identity");

// Development settings of the identity service: dev accounts (admin@modular.local, ...) and the dev client secrets.
var identity = builder.AddProject<Projects.WebApi_Identity_Server>("identity", launchProfileName: null)
    .WithHttpEndpoint(port: IdentityPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Identity__Issuer", Issuer)
    .WithReference(identityDb, connectionName: "identity").WaitFor(identityDb)
    .WithHttpHealthCheck("/health");

var data = Metadata("PlaygroundData");
var catalog = Path.Combine(data, mode == "dev" ? "modules" : "modules-install");
// The client-modules core module keeps its catalog in a folder relative to the WebApi, i.e. inside the platform
// checkout; keep the playground's in playground/.data instead (a host override, Modules:Settings:{id}:{key}).
var clientCatalog = Path.Combine(data, mode == "dev" ? "client-modules" : "client-modules-install");
var webApi = builder.AddProject<Projects.WebApi>("webapi", launchProfileName: null)
    .WithHttpEndpoint(port: WebApiPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Modules__Pool", "playground")
    .WithEnvironment("Modules__Role", "Api")
    // Every module the platform ships plus this one; the catalog lives in this repo, not in the platform checkout.
    .WithEnvironment("Modules__Path", catalog)
    .WithEnvironment("Modules__Settings__client-modules__ClientModules__Path", clientCatalog)
    .WithReference(tenantStore).WaitFor(tenantStore)
    .WithEnvironment("DatabaseServers__TenantStore__Provider", "PostgreSql")
    .WithReference(tenantData)
    .WithEnvironment("DatabaseServers__tenant-data__Provider", "PostgreSql")
    .WithEnvironment("Auth__Authority", Issuer)
    .WithEnvironment("Auth__Audience", "modular-webapi")
    .WithEnvironment("Cors__AllowedOrigins__0", ClientOrigin)
    // Development only (Access:DevGrants): the module's permissions for the dev accounts of tenant acme, written as
    // ordinary grants (source "dev") the first time each one calls the API there. acme-admin holds them on the whole
    // tenant. editor@ reads on the whole tenant, but arranges and publishes only in one unit (a unit:{id} scope, set
    // below with Playground:EditorUnit), as a shift would grant.
    .WithEnvironment("Access__DevGrants__0__Tenant", "acme")
    .WithEnvironment("Access__DevGrants__0__Email", "acme-admin@modular.local")
    .WithEnvironment("Access__DevGrants__0__Permissions__0", "contentdesigner.*")
    .WithEnvironment("Access__DevGrants__1__Tenant", "acme")
    .WithEnvironment("Access__DevGrants__1__Email", "editor@modular.local")
    .WithEnvironment("Access__DevGrants__1__Permissions__0", "contentdesigner.read")
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");
if (mode == "dev")
{
    // The server part from its build output (Modules:Local), and the client parts too (the client-modules module's
    // ClientModules:Local, set through a host override): every start installs what changed, the API client first.
    // Sign in on http://localhost:5070 as acme-admin@modular.local and open ContentDesigner (/contentdesigner).
    const string ClientLocal = "Modules__Settings__client-modules__ClientModules__Local__";
    webApi.WithEnvironment("Modules__Local__contentdesigner", Metadata("ModuleOutput"))
        .WithEnvironment(ClientLocal + "WebApi.Modules.ContentDesigner.Client", Metadata("ApiClientOutput"))
        .WithEnvironment(ClientLocal + "Module.ContentDesigner", Metadata("UiOutput"));

    // editor@'s unit: -- --Playground:EditorUnit=<unit id> (or user secrets / appsettings). The grant covers the unit and
    // the units below it, so posts filed there (or deeper) open for editing; every other post is read only for editor@.
    // To see "your shift ended", revoke these two grants while the studio is open (or plan a shift in the Shifts module
    // with a role that holds contentdesigner.arrange, and check out).
    var editorUnit = builder.Configuration["Playground:EditorUnit"];
    if (string.IsNullOrWhiteSpace(editorUnit))
        Console.WriteLine("editor@modular.local: no Playground:EditorUnit, so no right to arrange or publish any post (the studio opens read only).");
    else if (!Guid.TryParse(editorUnit, out var unit))
        throw new InvalidOperationException($"Playground:EditorUnit '{editorUnit}' is not a unit id.");
    else
    {
        string unitScope = "unit:" + unit.ToString("N"); // a unit's scope, as UnitScopes.Of writes it
        webApi.WithEnvironment("Access__DevGrants__2__Tenant", "acme")
            .WithEnvironment("Access__DevGrants__2__Email", "editor@modular.local")
            .WithEnvironment("Access__DevGrants__2__Scope", unitScope)
            .WithEnvironment("Access__DevGrants__2__Permissions__0", "contentdesigner.arrange")
            .WithEnvironment("Access__DevGrants__2__Permissions__1", "contentdesigner.publish");
    }

    // The modules the designer works with, each loaded from a built checkout next to this repo (Debug build), or
    // from Playground:<Name>=<path>. A module that is not built is skipped with a note; the designer degrades without it.
    //   DynamicFields  the post kinds and block fields (required for designing)
    //   Content        posts (documents) and their contents
    //   Storage        assets (images, video) and the file picker; blobs on the local disk in playground/.data/blobs
    //   Shifts         shifts, whose roles grant rights from check-in to check-out on a unit's scope
    //   Organization   the units: their names in the studio, and where Content files posts and contents. One deployment
    //                  runs it in Central mode with its own connector pointed at itself.
    // Elsa is not wired here yet: it needs its own database, a service client secret and a workflow pool.
    var siblings = new (string Name, string Id, string[] ClientParts, string AdminGrant, string? EditorGrant)[]
    {
        ("DynamicFields", "dynamicfields", ["WebApi.Modules.DynamicFields.Client", "Module.DynamicFields"], "dynamicfields.*", "dynamicfields.read"),
        ("Content", "content", ["WebApi.Modules.Content.Client", "Module.Content"], "content.*", "content.author"),
        ("Storage", "storage", ["WebApi.Modules.Storage.Client", "Module.Storage", "Module.Storage.Shares"], "storage.*", "storage.access"),
        ("Shifts", "shifts", ["WebApi.Modules.Shifts.Client", "Module.Shifts"], "shifts.*", null),
        ("Organization", "organization", ["WebApi.Modules.Organization.Client", "Module.Organization"], "org.*", "org.read"),
    };
    int adminPermission = 1, editorPermission = 1;
    foreach (var (name, id, clientParts, adminGrant, editorGrant) in siblings)
    {
        var root = Path.GetFullPath(builder.Configuration[$"Playground:{name}"] ?? Path.Combine(Metadata("RepoRoot"), "..", $"Module.{name}"));
        var web = Path.Combine(root, "src", $"WebApi.Modules.{name}.Web", "bin", "Debug", "net10.0");
        var parts = clientParts.Select(p => (Part: p, Dll: Path.Combine(root, "src", p, "bin", "Debug", "net10.0", $"soft.square.net.{p}.dll"))).ToArray();
        if (!File.Exists(Path.Combine(web, $"soft.square.net.WebApi.Modules.{name}.Web.dll")) || parts.Any(p => !File.Exists(p.Dll)))
        {
            Console.WriteLine($"{name}: no Debug build in {root}; build it (dotnet build) to load it beside ContentDesigner.");
            continue;
        }

        Console.WriteLine($"{name}: loading the module built in {root}.");
        webApi.WithEnvironment($"Modules__Local__{id}", web + Path.DirectorySeparatorChar)
            .WithEnvironment($"Access__DevGrants__0__Permissions__{adminPermission++}", adminGrant);
        if (editorGrant is not null)
            webApi.WithEnvironment($"Access__DevGrants__1__Permissions__{editorPermission++}", editorGrant);
        foreach (var (part, dll) in parts)
            webApi.WithEnvironment(ClientLocal + part, dll);
        if (id == "storage")
            webApi.WithEnvironment("Modules__Settings__storage__Storage__Local__Root", Path.Combine(data, "blobs"));
        if (id == "organization")
        {
            // DEVELOPMENT ONLY: this secret is a playground value, never reuse it. The identity service's one configurable
            // service client (Identity:ElsaClient; no Elsa runs here) is the connector's.
            const string ConnectorClientId = "organization-connector";
            const string ConnectorSecret = "dev-organization-connector-secret";
            const string Org = "Modules__Settings__organization__Organization__";
            identity.WithEnvironment("Identity__ElsaClient__ClientId", ConnectorClientId)
                .WithEnvironment("Identity__ElsaClient__ClientSecret", ConnectorSecret);
            string centralUrl = "http://localhost:" + WebApiPort + "/api/organization/";
            webApi.WithEnvironment("Platform__DeploymentId", "playground")
                .WithEnvironment("Tenancy__Seeds__Interval", "00:00:15")
                .WithEnvironment(Org + "Mode", "Central")
                .WithEnvironment(Org + "TrustedIssuers", Issuer)
                .WithEnvironment(Org + "ServiceClients", ConnectorClientId)
                .WithEnvironment(Org + "CentralUrl", centralUrl)
                .WithEnvironment(Org + "TokenEndpoint", Issuer + "connect/token")
                .WithEnvironment(Org + "ClientId", ConnectorClientId)
                .WithEnvironment(Org + "ClientScope", "api")
                .WithEnvironment(Org + "ClientSecret", ConnectorSecret)
                .WithEnvironment(Org + "PollInterval", "00:00:05");
        }
    }
}

builder.AddProject<Projects.ModularBlazor_BlzWebClient>("blzwebclient", launchProfileName: null)
    .WithHttpEndpoint(port: ClientPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WaitFor(identity)
    .WaitFor(webApi);

if (mode == "install")
    Console.WriteLine($"Install loop: upload {Metadata("ModulePackage")} on {ClientOrigin}/host-modules and activate it, then the client parts (out/client, API client first) on {ClientOrigin}/client-modules.");

builder.Build().Run();

// Paths the project file computes (AssemblyMetadata in ContentDesigner.Playground.csproj).
static string Metadata(string key) =>
    Assembly.GetEntryAssembly()!.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;

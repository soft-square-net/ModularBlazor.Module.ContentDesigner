# Clean Architecture in this module

This module follows [Ardalis Clean Architecture](https://github.com/ardalis/CleanArchitecture), adapted to what a
ModularBlazor runtime module is: an assembly set that the host loads into a collectible load context, maps under
`/api/contentdesigner`, and can unload again. The layout comes from the platform's Content module.

## The layers

```
src/
  WebApi.Modules.ContentDesigner.Core/            Core       (ModuleLayer Domain)
    Designs/                                 the design model: ViewKind, PartRef, StyleTokens, ViewDesign, DesignViews,
                                             DesignRules (the checks of plan section 4)
    PostDesignAggregate/PostDesign.cs        a post's design: draft views, published views, pins
    TemplateAggregate/                       DocumentTemplate, BlockTemplate
    */Specifications/                        DesignByPostSpec, DesignsPageSpec, DefaultTemplatesSpec, ...
  ContentDesigner.Rendering/                 Razor class library (no Module SDK): block renderers, the responsive merge,
                                             PageRenderer (HtmlRenderer to a whole HTML or AMP page), AmpChecker
  WebApi.Modules.ContentDesigner.UseCases/        UseCases   (ModuleLayer Application)
    ContentDesignerPermissions.cs ContentDesignerOptions.cs ResultExtensions.cs
    Behaviors/LoggingBehavior.cs             Mediator pipeline behaviour
    Ports/ContentApis.cs                     IContentApi, IFieldsApi: the other modules' APIs, as the use cases see them
    Designs/                                 DesignWorkbench (fields, checks, rendering), the design commands and queries
    Templates/                               document and block template commands and queries
    Rendering/RenderPublished.cs             the public page, cached
  WebApi.Modules.ContentDesigner.Infrastructure/  Infrastructure
    Data/ContentDesignerDbContext.cs              a TenantDbContext (schema "contentdesigner", tenant filter, domain events)
    Data/Config/DesignConfigurations.cs      the entity configurations (views as JSON columns)
    Data/Migrations/<Provider>/              one migration set per database engine
    Http/                                    Gateway, ContentApi, FieldsApi: typed HTTP clients through the gateway
    InfrastructureServiceExtensions.cs       AddInfrastructureServices()
  WebApi.Modules.ContentDesigner.Web/             Web: the entry assembly and composition root (ModuleLayer Module)
    ContentDesignerModule.cs                      IRuntimeModule: manifest, permissions, settings, ConfigureServices
    Configurations/                          ServiceConfigs (all registrations), MediatorConfig
    ContentDesignerRoutes.cs                      the only ICarterModule: the route table
    Designs/  Templates/  Rendering/         one endpoint class per route (REPR)
    Extensions/                              ResultExtensions (Result -> HTTP), ContentDesignerProblemsFilter
tests/
  WebApi.Modules.ContentDesigner.UnitTests/       Core rules; Rendering (pages, AMP); UseCases handlers with NSubstitute
  WebApi.Modules.ContentDesigner.IntegrationTests/ the DbContext, mapping and specifications on SQLite
```

| Layer | Holds | Knows about |
| --- | --- | --- |
| Core | Aggregates and their rules (throwing `DomainException`), entity specifications | the platform's SharedKernel (`AggregateRoot`, `IRepository`) and Ardalis.Specification |
| Rendering | Razor components that turn a design and a post into HTML; the AMP checks | ASP.NET Core components only (from the shared framework) |
| UseCases | One folder per use case: a command or query and its handler, returning an `Ardalis.Result`; DTOs; permissions; settings; the ports to other modules | Core, Rendering; the platform's contracts (`IAccessScopes`, `ITenantContext`); Mediator.Abstractions |
| Infrastructure | EF Core: the DbContext, mappings, migrations; the HTTP clients behind the ports | Core, UseCases (the ports); the platform's data layer (`TenantDbContext`, `EfRepository`) |
| Web | The module entry, registrations, routes, endpoints, HTTP mapping of results | everything above; ASP.NET Core, Carter, the Mediator source generator |

## Dependency rules

```
Core  <-  UseCases  <-  Infrastructure  <-  Web
            |                                |
            +-> Rendering   <----------------+
```

- Core references nothing of the module. Rendering references nothing of the module either (it takes its own render
  model). UseCases references Core and Rendering. Infrastructure references UseCases, for the ports it implements
  (`IContentApi`, `IFieldsApi`).
- Other modules are reached over HTTP through the gateway (`Http/Gateway`), never in process: a module may be
  unloaded or run in another pool.
- Web references UseCases and Infrastructure and is the only place that wires them together (`ServiceConfigs`).
- UseCases never touches EF Core: handlers get `IRepository<T>` / `IReadRepository<T>` (the platform registers EF
  repositories for them) and query with specifications from Core.
- Endpoints contain no logic: bind the request, `ISender.Send` a command or query, map the `Result` to HTTP.
- Repositories save immediately (`AddAsync`, `UpdateAsync`, `DeleteAsync`); there is no unit of work. Domain events
  (raised with `Raise(...)` in an aggregate, handled by an `IDomainEventHandler<T>`) go through the platform's
  `TenantDbContext` dispatcher in the same save, not through the mediator.
- Failures a caller should see are `Result` statuses: `NotFound` (404), `Forbidden` (403), `Unauthorized` (401),
  `Conflict` (409), `Invalid` (400). A broken domain rule is a `DomainException`; `ContentDesignerProblemsFilter` turns it,
  and EF concurrency conflicts, into 409 problems.
- Optimistic concurrency: an aggregate that callers edit carries a `Version` (a concurrency token); edits and deletes
  send back the version they read.

### Rules that come from the platform

- **No module type in a host-wide cache.** Do not inject `ILogger<T>` with a module type `T` (the host's logger
  factory caches by type): take `ILoggerFactory` and create the logger by name, as `LoggingBehavior` does. Register
  module services in `ConfigureServices` (the module's own container), never in the host's.
- **Host-shared packages are compile-only.** The Module SDK references WebApi.Contracts, SharedKernel, Data, EF Core,
  Ardalis.Specification and Carter with `ExcludeAssets=runtime`; the host provides them. Never ship a copy.
- **Private packages are fine when they keep no host-side state.** Mediator (source-generated: its handler table is
  code in the Web assembly) and Ardalis.Result are copied into the module output and unload with it. Avoid packages
  the host already ships (FluentValidation, for example): the module would share the host's copy and its caches.
- **Only the entry assembly is scanned.** The runtime maps the `ICarterModule` types of the assembly that holds the
  `IRuntimeModule`, which is Web, so endpoints live there.
- **Check that it unloads.** After the module has served requests, `POST /api/admin/modules/contentdesigner/unload` and
  then `GET /api/admin/modules/unloading` should come back empty within a few seconds.

## Adding a feature

A new action on designs, say "lock":

1. **Core**: the rule on the aggregate, `PostDesign.Lock(by, now)`, throwing `DomainException` when not allowed. A new
   query shape is a specification in `PostDesignAggregate/Specifications`.
2. **UseCases**: `LockDesignCommand` (`ICommand<Result<PostDesignDto>>`) and `LockDesignHandler` in `Designs/`.
   Add a permission to `ContentDesignerPermissions` if it needs a new one.
3. **Web**: `LockDesign` in `Designs/DesignEndpoints.cs`, an `IEndpoint` with a static `HandleAsync` that sends the
   command and returns `Results<Ok<T>, ProblemHttpResult>`; then one line in `ContentDesignerRoutes`. Name it
   `ContentDesigner_LockDesign`: generated clients take their method names from it.
4. **Infrastructure**, if the model changed: the mapping in `Data/Config`, then a migration for every provider (see
   the README) and `dotnet ef migrations has-pending-model-changes` per provider.
5. **Client**: export the OpenAPI document again (README, "Building") and build: `ContentDesignerClient` gains
   `LockDesignAsync`. Use it from a page in `Module.ContentDesigner` (pages inherit `ContentDesignerPageBase`: `Api`, `RunAsync`,
   readable errors).
6. **Tests**: the rule in `UnitTests/Core`, the handler in `UnitTests/UseCases`, a new mapping or specification in
   `IntegrationTests`.

A new aggregate gets its own `<Name>Aggregate` folder in Core, a resource folder in UseCases and in Web, a `DbSet`
and a configuration in Infrastructure, and a migration.

Handlers are found by the Mediator source generator at compile time; there is nothing to register. Services that
several handlers share are registered in `ServiceConfigs`.

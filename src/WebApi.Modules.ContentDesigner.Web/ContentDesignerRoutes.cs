using Carter;
using Designs = soft.square.net.WebApi.Modules.ContentDesigner.Web.Designs;
using Rendering = soft.square.net.WebApi.Modules.ContentDesigner.Web.Rendering;
using Templates = soft.square.net.WebApi.Modules.ContentDesigner.Web.Templates;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Web;

/// <summary>
/// The module's route table, and its only Carter module (the runtime maps the Carter modules of the entry assembly,
/// under <c>/api/contentdesigner</c>). Every endpoint is listed here once, in the order of the OpenAPI document. A new
/// endpoint: add its class under the resource folder, then one line here.
/// </summary>
public sealed class ContentDesignerRoutes : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // Problem details for domain rules and lost races, on the module's own groups (module code, unloads with it).
        var designs = app.MapGroup("").WithTags("Designs").AddEndpointFilter<ContentDesignerProblemsFilter>();
        Designs.List.Map(designs);
        Designs.Get.Map(designs);
        Designs.Open.Map(designs);
        Designs.Save.Map(designs);
        Designs.Arrange.Map(designs);
        Designs.ApplyTemplate.Map(designs);
        Designs.Check.Map(designs);
        Designs.Preview.Map(designs);
        Designs.Publish.Map(designs);
        Designs.Unpublish.Map(designs);
        Designs.Delete.Map(designs);
        Designs.Workspace.Map(designs);
        Designs.Events.Map(designs);

        var templates = app.MapGroup("").WithTags("Templates").AddEndpointFilter<ContentDesignerProblemsFilter>();
        Templates.ListDocumentTemplates.Map(templates);
        Templates.GetDocumentTemplate.Map(templates);
        Templates.CreateDocumentTemplate.Map(templates);
        Templates.EditDocumentTemplate.Map(templates);
        Templates.DeleteDocumentTemplate.Map(templates);
        Templates.SetDefaultTemplate.Map(templates);
        Templates.SaveDesignAsTemplate.Map(templates);
        Templates.ListBlockTemplates.Map(templates);
        Templates.GetBlockTemplate.Map(templates);
        Templates.CreateBlockTemplate.Map(templates);
        Templates.EditBlockTemplate.Map(templates);
        Templates.DeleteBlockTemplate.Map(templates);
        Templates.SaveBlockAsTemplate.Map(templates);

        var rendering = app.MapGroup("").WithTags("Rendering").AddEndpointFilter<ContentDesignerProblemsFilter>();
        Rendering.RenderPage.Map(rendering);
        Rendering.RenderAmpPage.Map(rendering);
    }
}

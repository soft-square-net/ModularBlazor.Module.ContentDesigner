namespace soft.square.net.WebApi.Modules.ContentDesigner.UseCases;

/// <summary>
/// The module's permissions, named <c>&lt;module&gt;.&lt;action&gt;</c>. Declare every permission the module uses (the
/// manifest lists <see cref="All"/>); activation fails on an undeclared one. Admins grant them per tenant.
/// </summary>
public static class ContentDesignerPermissions
{
    public const string Read = "contentdesigner.read";
    public const string Arrange = "contentdesigner.arrange";
    public const string Templates = "contentdesigner.templates";
    public const string Publish = "contentdesigner.publish";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "See post designs and templates, run the checks and preview the pages."),
        new(Arrange, "Open a post in the designer, arrange its views and apply templates (on the post's unit, press or category)."),
        new(Templates, "Create, change and delete document and block templates, and choose the default per kind."),
        new(Publish, "Publish and unpublish a post's design (on the post's unit, press or category).")
    ];
}

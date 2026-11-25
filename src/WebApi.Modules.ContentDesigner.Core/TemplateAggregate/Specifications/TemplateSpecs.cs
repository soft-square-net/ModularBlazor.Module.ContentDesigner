using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate.Specifications;

public sealed class DocumentTemplateByIdSpec : SingleResultSpecification<DocumentTemplate>
{
    public DocumentTemplateByIdSpec(int id) => Query.Where(t => t.Id == id);
}

/// <summary>The kind's default template(s): one, unless a race left two (the newest wins).</summary>
public sealed class DefaultTemplatesSpec : Specification<DocumentTemplate>
{
    public DefaultTemplatesSpec(string kind) =>
        Query.Where(t => t.Kind == kind && t.IsDefault).OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id);
}

public sealed class DocumentTemplatesSpec : Specification<DocumentTemplate>
{
    public DocumentTemplatesSpec(string? kind)
    {
        if (!string.IsNullOrWhiteSpace(kind)) Query.Where(t => t.Kind == kind);
        Query.OrderBy(t => t.Kind).ThenBy(t => t.Name).ThenBy(t => t.Id).AsNoTracking();
    }
}

public sealed class BlockTemplateByIdSpec : SingleResultSpecification<BlockTemplate>
{
    public BlockTemplateByIdSpec(int id) => Query.Where(t => t.Id == id);
}

public sealed class BlockTemplatesByIdsSpec : Specification<BlockTemplate>
{
    public BlockTemplatesByIdsSpec(IReadOnlyCollection<int> ids) => Query.Where(t => ids.Contains(t.Id)).AsNoTracking();
}

public sealed class BlockTemplatesSpec : Specification<BlockTemplate>
{
    public BlockTemplatesSpec(string? fieldKey, ViewKind? view)
    {
        if (!string.IsNullOrWhiteSpace(fieldKey)) Query.Where(t => t.FieldKey == fieldKey);
        if (view is { } v) Query.Where(t => t.View == v);
        Query.OrderBy(t => t.FieldKey).ThenBy(t => t.View).ThenBy(t => t.Name).ThenBy(t => t.Id).AsNoTracking();
    }
}

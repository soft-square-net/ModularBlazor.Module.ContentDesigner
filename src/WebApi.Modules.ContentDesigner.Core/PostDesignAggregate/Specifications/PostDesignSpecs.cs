namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate.Specifications;

public sealed class DesignByPostSpec : SingleResultSpecification<PostDesign>
{
    public DesignByPostSpec(int postId) => Query.Where(d => d.PostId == postId);
}

public sealed class DesignByPublicIdSpec : SingleResultSpecification<PostDesign>
{
    public DesignByPublicIdSpec(string publicId) => Query.Where(d => d.PublicId == publicId).AsNoTracking();
}

/// <summary>One page of designs, the latest change first; optionally of one kind.</summary>
public sealed class DesignsPageSpec : Specification<PostDesign>
{
    public DesignsPageSpec(int page, int pageSize, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(kind)) Query.Where(d => d.Kind == kind);
        Query.OrderByDescending(d => d.UpdatedAt).ThenByDescending(d => d.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking();
    }
}

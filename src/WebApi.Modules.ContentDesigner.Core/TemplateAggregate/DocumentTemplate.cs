using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;

/// <summary>
/// A document template: four views for posts of one kind (a DynamicFields root field and the version it was made
/// on). A view may inherit another. One template per kind may be the default, which a post with no design starts from;
/// the use case that makes one the default clears the previous one in the same save.
/// </summary>
public sealed class DocumentTemplate : AggregateRoot<int>, ITenantOwned
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;

    private DocumentTemplate() { } // EF Core

    public string TenantId { get; set; } = "";
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public string Kind { get; private set; } = "";
    public int KindVersion { get; private set; }
    public DesignViews Views { get; private set; } = DesignViews.Starter;
    public bool IsDefault { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public static DocumentTemplate Create(string name, string? description, string kind, int kindVersion, DesignViews views, string by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(by)) throw new DomainException("A template needs its author.");
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > PostDesignAggregate.PostDesign.KindMaxLength) throw new DomainException("A template is for a kind (a root field key).");
        if (kindVersion < 1) throw new DomainException("A template is made on a published version of its kind.");
        var template = new DocumentTemplate { Kind = kind, KindVersion = kindVersion, CreatedBy = by, CreatedAt = now, UpdatedAt = now };
        template.Set(name, description, views);
        return template;
    }

    public void Edit(string name, string? description, DesignViews views, DateTimeOffset now)
    {
        Set(name, description, views);
        UpdatedAt = now;
        Version++;
    }

    public void MakeDefault(bool isDefault, DateTimeOffset now)
    {
        if (IsDefault == isDefault) return;
        IsDefault = isDefault;
        UpdatedAt = now;
        Version++;
    }

    private void Set(string name, string? description, DesignViews views)
    {
        Name = Names.Checked(name, NameMaxLength, "A template");
        if (description is { Length: > DescriptionMaxLength }) throw new DomainException($"A description is at most {DescriptionMaxLength} characters.");
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Views = views.Checked();
    }
}

internal static class Names
{
    public static string Checked(string? name, int max, string what)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > max) throw new DomainException($"{what} needs a name (at most {max} characters).");
        return name.Trim();
    }
}

using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;

/// <summary>
/// The published state of a design: its views, frozen, and what it was checked against. <see cref="Pins"/> holds the
/// revision of each content the checks saw; <see cref="NodeIds"/> maps the design's content ids to the post's node ids,
/// so the public renderer finds them in the published document (which carries node ids only).
/// </summary>
public sealed record PublishedDesign(
    int DesignVersion,
    DesignViews Views,
    int KindVersion,
    IReadOnlyDictionary<string, int> Pins,
    IReadOnlyDictionary<string, string> NodeIds,
    string PublishedBy,
    DateTimeOffset PublishedAt);

/// <summary>
/// The four-view arrangement of one post (a Content post, by its id): one per post. It is made from a document
/// template (or the starter), edited as a draft, and published once the checks pass, which freezes the draft and pins
/// the content revisions it was checked against. Contents attached to the post later land in <c>items</c>.
/// </summary>
public sealed class PostDesign : AggregateRoot<int>, ITenantOwned
{
    public const int KindMaxLength = 100;
    public const int PublicIdMaxLength = 64;

    private PostDesign() { } // EF Core

    public string TenantId { get; set; } = "";

    /// <summary>The Content post's id.</summary>
    public int PostId { get; private set; }

    /// <summary>The post's public id (Content's <c>/public/{publicId}</c>), which the public render routes use.</summary>
    public string PublicId { get; private set; } = "";

    /// <summary>The post's kind: a DynamicFields root field key (<c>post.news</c>) and the version the design was made on.</summary>
    public string Kind { get; private set; } = "";
    public int KindVersion { get; private set; }

    public int? BasedOnTemplateId { get; private set; }
    public DesignViews Draft { get; private set; } = DesignViews.Starter;
    public PublishedDesign? Published { get; private set; }

    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = "";
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Bumped by every change; a concurrency token.</summary>
    public int Version { get; private set; }

    /// <summary>Bumped by every publication; part of the render cache key.</summary>
    public int PublishedVersion { get; private set; }

    public static PostDesign Create(int postId, string publicId, string kind, int kindVersion, DesignViews views,
        int? basedOnTemplateId, string by, DateTimeOffset now)
    {
        if (postId <= 0) throw new DomainException("A design is for a post (its id).");
        if (string.IsNullOrWhiteSpace(by)) throw new DomainException("A design needs its author.");
        if (string.IsNullOrWhiteSpace(publicId) || publicId.Length > PublicIdMaxLength) throw new DomainException("The post has no public id.");
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > KindMaxLength) throw new DomainException("The post has no kind.");
        return new PostDesign
        {
            PostId = postId,
            PublicId = publicId,
            Kind = kind,
            KindVersion = kindVersion,
            BasedOnTemplateId = basedOnTemplateId,
            Draft = views.Checked(),
            CreatedBy = by,
            CreatedAt = now,
            UpdatedBy = by,
            UpdatedAt = now
        };
    }

    /// <summary>Replaces the whole draft.</summary>
    public void ReplaceDraft(DesignViews views, string by, DateTimeOffset now)
    {
        Draft = views.Checked();
        Touch(by, now);
    }

    /// <summary>Arranges one view.</summary>
    public void Arrange(ViewKind view, ViewDesign design, string by, DateTimeOffset now)
    {
        Draft = Draft.With(view, design).Checked();
        Touch(by, now);
    }

    /// <summary>
    /// Takes a template's views (all, or the ones named), and records the template as the design's base. The template
    /// must be for the post's kind.
    /// </summary>
    public void ApplyTemplate(int templateId, string templateKind, DesignViews template, IReadOnlyCollection<ViewKind>? views, string by, DateTimeOffset now)
    {
        if (templateKind != Kind) throw new DomainException($"The template is for '{templateKind}', the post is a '{Kind}'.");
        var draft = Draft;
        foreach (var view in views is { Count: > 0 } ? views : ViewKinds.All)
            draft = draft.With(view, template[view]);
        Draft = draft.Checked();
        BasedOnTemplateId = templateId;
        Touch(by, now);
    }

    /// <summary>The kind version moved on in Content (the post was saved against a newer version).</summary>
    public void Rebase(int kindVersion, string by, DateTimeOffset now)
    {
        if (kindVersion == KindVersion) return;
        KindVersion = kindVersion;
        Touch(by, now);
    }

    /// <summary>
    /// Publishes the draft as it is now. The caller ran the checks and found no errors; <paramref name="pins"/> are the
    /// revisions of the contents those checks saw.
    /// </summary>
    public void Publish(IReadOnlyDictionary<string, int> pins, IReadOnlyDictionary<string, string> nodeIds, string by, DateTimeOffset now)
    {
        PublishedVersion++;
        Published = new PublishedDesign(PublishedVersion, Draft, KindVersion, new Dictionary<string, int>(pins),
            new Dictionary<string, string>(nodeIds), by, now);
        Touch(by, now);
    }

    public void Unpublish(string by, DateTimeOffset now)
    {
        if (Published is null) throw new DomainException("The design is not published.");
        Published = null;
        Touch(by, now);
    }

    private void Touch(string by, DateTimeOffset now)
    {
        UpdatedBy = by;
        UpdatedAt = now;
        Version++;
    }
}

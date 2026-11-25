using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.Designs;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.PostDesignAggregate;
using soft.square.net.WebApi.Modules.ContentDesigner.Core.TemplateAggregate;

namespace soft.square.net.WebApi.Modules.ContentDesigner.Infrastructure.Data.Config;

/// <summary>Column sizes shared by the mappings.</summary>
public static class DataSchemaConstants
{
    /// <summary>A user id (the token's <c>sub</c>).</summary>
    public const int UserIdLength = 128;
}

/// <summary>A value stored as JSON text: converted with System.Text.Json and compared by its JSON.</summary>
internal static class JsonColumn
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> AsJson<T>(this PropertyBuilder<T> property) =>
        property.HasConversion(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<T>(json, Options)!,
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
                value => JsonSerializer.Serialize(value, Options).GetHashCode(),
                value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!));
}

public sealed class PostDesignConfiguration : IEntityTypeConfiguration<PostDesign>
{
    public void Configure(EntityTypeBuilder<PostDesign> design)
    {
        design.ToTable("post_designs");
        design.HasIndex(d => new { d.TenantId, d.PostId }).IsUnique();
        design.HasIndex(d => new { d.TenantId, d.PublicId });
        design.HasIndex(d => new { d.TenantId, d.Kind });
        design.Property(d => d.PublicId).HasMaxLength(PostDesign.PublicIdMaxLength);
        design.Property(d => d.Kind).HasMaxLength(PostDesign.KindMaxLength);
        design.Property(d => d.Draft).AsJson().HasColumnName("DraftJson");
        design.Property(d => d.Published).AsJson().HasColumnName("PublishedJson");
        design.Property(d => d.CreatedBy).HasMaxLength(DataSchemaConstants.UserIdLength);
        design.Property(d => d.UpdatedBy).HasMaxLength(DataSchemaConstants.UserIdLength);
        design.Property(d => d.Version).IsConcurrencyToken();
    }
}

public sealed class DocumentTemplateConfiguration : IEntityTypeConfiguration<DocumentTemplate>
{
    public void Configure(EntityTypeBuilder<DocumentTemplate> template)
    {
        template.ToTable("document_templates");
        template.HasIndex(t => new { t.TenantId, t.Kind });
        template.Property(t => t.Name).HasMaxLength(DocumentTemplate.NameMaxLength);
        template.Property(t => t.Description).HasMaxLength(DocumentTemplate.DescriptionMaxLength);
        template.Property(t => t.Kind).HasMaxLength(PostDesign.KindMaxLength);
        template.Property(t => t.Views).AsJson().HasColumnName("ViewsJson");
        template.Property(t => t.CreatedBy).HasMaxLength(DataSchemaConstants.UserIdLength);
        template.Property(t => t.Version).IsConcurrencyToken();
    }
}

public sealed class BlockTemplateConfiguration : IEntityTypeConfiguration<BlockTemplate>
{
    public void Configure(EntityTypeBuilder<BlockTemplate> template)
    {
        template.ToTable("block_templates");
        template.HasIndex(t => new { t.TenantId, t.FieldKey, t.View });
        template.Property(t => t.Name).HasMaxLength(BlockTemplate.NameMaxLength);
        template.Property(t => t.FieldKey).HasMaxLength(BlockTemplate.FieldKeyMaxLength);
        template.Property(t => t.View).HasConversion<string>().HasMaxLength(16);
        template.Property(t => t.PartOrder).AsJson().HasColumnName("PartOrderJson").HasMaxLength(4000);
        template.Property(t => t.HiddenParts).AsJson().HasColumnName("HiddenPartsJson").HasMaxLength(4000);
        template.Property(t => t.Tokens).AsJson().HasColumnName("TokensJson").HasMaxLength(2000);
        template.Property(t => t.CreatedBy).HasMaxLength(DataSchemaConstants.UserIdLength);
        template.Property(t => t.Version).IsConcurrencyToken();
    }
}

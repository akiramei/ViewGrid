using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Persistence.Configurations;

internal sealed class ImageAssetConfiguration : IEntityTypeConfiguration<ImageAsset>
{
    public void Configure(EntityTypeBuilder<ImageAsset> builder)
    {
        builder.ToTable("image_assets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.OriginalFilename).HasMaxLength(512);
        builder.Property(x => x.StoredRelativePath).HasMaxLength(512).IsRequired();
        builder.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MimeType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FileSizeBytes).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.ComplexProperty(x => x.Size, size =>
        {
            size.Property(p => p.Width).HasColumnName("width").IsRequired();
            size.Property(p => p.Height).HasColumnName("height").IsRequired();
        });

        builder.HasIndex(x => x.FileHash).IsUnique();
        builder.HasIndex(x => x.CreatedAt);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Persistence.Configurations;

internal sealed class ImageCopyConfiguration : IEntityTypeConfiguration<ImageCopy>
{
    public void Configure(EntityTypeBuilder<ImageCopy> builder)
    {
        builder.ToTable("image_copies");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AssetId).IsRequired();
        builder.Property(x => x.CopyName).HasMaxLength(256);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.Ignore(x => x.Characteristics);

        builder.ComplexProperty(x => x.Transform, transform =>
        {
            transform.Property(p => p.Rotation).HasConversion<int>().HasColumnName("rotation").IsRequired();
            transform.Property(p => p.FlipX).HasColumnName("flip_x").IsRequired();
            transform.Property(p => p.FlipY).HasColumnName("flip_y").IsRequired();
        });

        builder.Property(x => x.ScalingMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnName("scaling_mode")
            .IsRequired();

        builder.ComplexProperty(x => x.TrimmingAnchor, ta =>
        {
            ta.Property(p => p.X).HasConversion<string>().HasMaxLength(8).HasColumnName("trim_anchor_x").IsRequired();
            ta.Property(p => p.Y).HasConversion<string>().HasMaxLength(8).HasColumnName("trim_anchor_y").IsRequired();
        });

        builder.ComplexProperty(x => x.Alignment, al =>
        {
            al.Property(p => p.X).HasConversion<string>().HasMaxLength(8).HasColumnName("align_x").IsRequired();
            al.Property(p => p.Y).HasConversion<string>().HasMaxLength(8).HasColumnName("align_y").IsRequired();
        });

        builder.ComplexProperty(x => x.OccupySize, occ =>
        {
            occ.Property(p => p.Width).HasColumnName("occupy_width").IsRequired();
            occ.Property(p => p.Height).HasColumnName("occupy_height").IsRequired();
        });

        builder.HasOne<ImageAsset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.AssetId);
    }
}

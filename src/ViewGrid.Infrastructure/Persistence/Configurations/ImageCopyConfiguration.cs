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

        // 単色余白の自動トリミング設定。集約ビュー <see cref="ImageCopy.AutoCrop"/> は無視し、
        // 原始値 2 つ（uint? / byte?）を直接 nullable 列にマップする。両方 NULL なら OFF。
        builder.Ignore(x => x.AutoCrop);
        builder.Property(x => x.AutoCropTargetColor)
            .HasConversion<long?>()  // uint → long? で SQLite INTEGER に安全保存
            .HasColumnName("auto_crop_color");
        builder.Property(x => x.AutoCropThreshold)
            .HasConversion<int?>()  // byte → int? で SQLite INTEGER に
            .HasColumnName("auto_crop_threshold");

        // 任意矩形トリミング設定。集約ビュー <see cref="ImageCopy.ManualCrop"/> は無視し、
        // 原始値 4 つ（double?）を直接 nullable 列にマップする。4 つすべて NULL なら OFF。
        builder.Ignore(x => x.ManualCrop);
        builder.Property(x => x.ManualCropX).HasColumnName("manual_crop_x");
        builder.Property(x => x.ManualCropY).HasColumnName("manual_crop_y");
        builder.Property(x => x.ManualCropWidth).HasColumnName("manual_crop_width");
        builder.Property(x => x.ManualCropHeight).HasColumnName("manual_crop_height");

        builder.HasOne<ImageAsset>()
            .WithMany()
            .HasForeignKey(x => x.AssetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.AssetId);

        // ProtectedRegion 集約は子テーブル image_copy_regions に独立 entity として保存し、
        // Repository が手動でアセンブルする (本コードベースの shadow-FK 規約に合わせる)。
        // ImageCopy.Regions は ImmutableArray のメモリ上ビューのみ。
        builder.Ignore(x => x.Regions);
    }
}

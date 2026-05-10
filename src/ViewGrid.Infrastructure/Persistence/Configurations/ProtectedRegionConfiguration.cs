using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="ProtectedRegion"/> を子テーブル <c>image_copy_regions</c> にマップする。
/// shadow-FK 規約 (本コードベースの他テーブルと同様、 navigation property を持たず
/// FK 列のみ持つ) に合わせ、 <c>ImageCopy.Regions</c> 側は Ignore (Repository で手動アセンブル)。
/// </summary>
internal sealed class ProtectedRegionConfiguration : IEntityTypeConfiguration<ProtectedRegion>
{
    public void Configure(EntityTypeBuilder<ProtectedRegion> builder)
    {
        builder.ToTable("image_copy_regions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ImageCopyId).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();

        // 元画像座標系 0–1 比率の bbox。 ManualCrop 系と同じく原始値を直接 nullable でない
        // 列にマップする (Region は Rect 必須のため nullable ではない)。
        builder.ComplexProperty(x => x.Rect, rect =>
        {
            rect.Property(p => p.X).HasColumnName("rect_x").IsRequired();
            rect.Property(p => p.Y).HasColumnName("rect_y").IsRequired();
            rect.Property(p => p.Width).HasColumnName("rect_width").IsRequired();
            rect.Property(p => p.Height).HasColumnName("rect_height").IsRequired();
        });

        // FillMode は enum {White=0, Black=1, Transparent=2, Custom=3}。 拡張余地のため文字列ではなく
        // INTEGER として保存 (将来の値追加で renumbering せずに済む)。
        builder.Property(x => x.FillMode)
            .HasConversion<int>()
            .HasColumnName("fill_mode")
            .IsRequired();

        // FillMode = Custom のときに使う ARGB 色 (uint, AARRGGBB)。 プリセット利用時は null。
        builder.Property(x => x.FillColor)
            .HasColumnName("fill_color");

        // セル内 pixel オフセット。 既定 0 (左上)。 負値も許可。
        builder.Property(x => x.OffsetXPx)
            .HasColumnName("offset_x_px")
            .HasDefaultValue(0)
            .IsRequired();
        builder.Property(x => x.OffsetYPx)
            .HasColumnName("offset_y_px")
            .HasDefaultValue(0)
            .IsRequired();

        // region 自身の回転 (90度刻み, 時計回り)。 既定 None=0。
        builder.Property(x => x.Rotation)
            .HasConversion<int>()
            .HasColumnName("region_rotation")
            .HasDefaultValue(Rotation.None)
            .IsRequired();

        // region 自身の水平 / 垂直反転。 既定 false。
        builder.Property(x => x.FlipX)
            .HasColumnName("region_flip_x")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(x => x.FlipY)
            .HasColumnName("region_flip_y")
            .HasDefaultValue(false)
            .IsRequired();

        // ImageCopy 削除時にカスケード削除。 shadow-FK 規約に従い navigation は持たない。
        builder.HasOne<ImageCopy>()
            .WithMany()
            .HasForeignKey(x => x.ImageCopyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ImageCopyId);
        builder.HasIndex(x => new { x.ImageCopyId, x.SortOrder });
    }
}

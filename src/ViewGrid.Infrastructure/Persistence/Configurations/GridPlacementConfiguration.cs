using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Persistence.Configurations;

internal sealed class GridPlacementConfiguration : IEntityTypeConfiguration<GridPlacement>
{
    public void Configure(EntityTypeBuilder<GridPlacement> builder)
    {
        builder.ToTable("grid_placements");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GridId).IsRequired();
        builder.Property(x => x.CopyId).IsRequired();
        builder.Property(x => x.PixelOffsetX).IsRequired();
        builder.Property(x => x.PixelOffsetY).IsRequired();
        builder.Property(x => x.PlacementOrder).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.ComplexProperty(x => x.Position, pos =>
        {
            pos.Property(p => p.X).HasColumnName("grid_x").IsRequired();
            pos.Property(p => p.Y).HasColumnName("grid_y").IsRequired();
        });

        builder.ComplexProperty(x => x.OccupySize, occ =>
        {
            occ.Property(p => p.Width).HasColumnName("occupy_width").IsRequired();
            occ.Property(p => p.Height).HasColumnName("occupy_height").IsRequired();
        });

        builder.HasOne<GridCanvas>()
            .WithMany()
            .HasForeignKey(x => x.GridId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ImageCopy>()
            .WithMany()
            .HasForeignKey(x => x.CopyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.GridId);
        builder.HasIndex(x => x.CopyId);
        builder.HasIndex(x => new { x.GridId, x.PlacementOrder });
    }
}

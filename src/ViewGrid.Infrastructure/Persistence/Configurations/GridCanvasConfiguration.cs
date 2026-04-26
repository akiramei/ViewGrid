using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ViewGrid.Core.Entities;

namespace ViewGrid.Infrastructure.Persistence.Configurations;

internal sealed class GridCanvasConfiguration : IEntityTypeConfiguration<GridCanvas>
{
    public void Configure(EntityTypeBuilder<GridCanvas> builder)
    {
        builder.ToTable("grid_canvases");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(128).IsRequired();
        builder.Property(x => x.GridRows).IsRequired();
        builder.Property(x => x.GridCols).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.ComplexProperty(x => x.CanvasSize, size =>
        {
            size.Property(p => p.Width).HasColumnName("canvas_width").IsRequired();
            size.Property(p => p.Height).HasColumnName("canvas_height").IsRequired();
        });

        // ImmutableArray<int> をカンマ区切り文字列で永続化する。
        // 値オブジェクトとしての等価性比較も差し替える（要素単位の比較）。
        var weightsConverter = new ValueConverter<ImmutableArray<int>, string>(
            v => string.Join(',', v),
            v => string.IsNullOrEmpty(v)
                ? ImmutableArray<int>.Empty
                : ImmutableArray.CreateRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)));
        var weightsComparer = new ValueComparer<ImmutableArray<int>>(
            (a, b) => a.SequenceEqual(b),
            v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
            v => ImmutableArray.CreateRange(v));

        builder.Property(x => x.ColWeights)
            .HasColumnName("col_weights")
            .HasMaxLength(256)
            .HasConversion(weightsConverter, weightsComparer)
            .IsRequired();
        builder.Property(x => x.RowWeights)
            .HasColumnName("row_weights")
            .HasMaxLength(256)
            .HasConversion(weightsConverter, weightsComparer)
            .IsRequired();

        // ImmutableArray<bool> をカンマ区切り 0/1 文字列で永続化。
        // 空文字列は空配列に戻る（要素数 0 = 全列アンロック扱い、後方互換）。
        var locksConverter = new ValueConverter<ImmutableArray<bool>, string>(
            v => string.Join(',', v.Select(b => b ? "1" : "0")),
            v => string.IsNullOrEmpty(v)
                ? ImmutableArray<bool>.Empty
                : ImmutableArray.CreateRange(v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s == "1")));
        var locksComparer = new ValueComparer<ImmutableArray<bool>>(
            (a, b) => a.SequenceEqual(b),
            v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x)),
            v => ImmutableArray.CreateRange(v));

        builder.Property(x => x.ColLocked)
            .HasColumnName("col_locked")
            .HasMaxLength(256)
            .HasConversion(locksConverter, locksComparer)
            .IsRequired();
        builder.Property(x => x.RowLocked)
            .HasColumnName("row_locked")
            .HasMaxLength(256)
            .HasConversion(locksConverter, locksComparer)
            .IsRequired();

        builder.HasIndex(x => x.IsActive);
        builder.HasIndex(x => x.UpdatedAt);
    }
}

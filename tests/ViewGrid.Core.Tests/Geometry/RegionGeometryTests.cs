using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// <see cref="RegionGeometry"/> の純粋関数 2 つの境界・幾何検証。
/// </summary>
public sealed class RegionGeometryTests
{
    // ─── Intersect: 交差なしケース ────────────────────────────────

    [Fact]
    public void Intersect_NoOverlap_LeftOfCrop_ReturnsNull()
    {
        var region = new RegionRectFraction(0.0, 0.0, 0.2, 1.0);
        var crop = new CropFraction(0.5, 0.0, 0.5, 1.0);
        RegionGeometry.Intersect(region, crop).Should().BeNull();
    }

    [Fact]
    public void Intersect_NoOverlap_AboveCrop_ReturnsNull()
    {
        var region = new RegionRectFraction(0.0, 0.0, 1.0, 0.2);
        var crop = new CropFraction(0.0, 0.5, 1.0, 0.5);
        RegionGeometry.Intersect(region, crop).Should().BeNull();
    }

    [Fact]
    public void Intersect_TouchingEdge_ReturnsNull()
    {
        // x1 == x0 (右端が crop 左端と一致するだけ) は交差なしとみなす (面積 0)
        var region = new RegionRectFraction(0.0, 0.0, 0.5, 1.0);
        var crop = new CropFraction(0.5, 0.0, 0.5, 1.0);
        RegionGeometry.Intersect(region, crop).Should().BeNull();
    }

    [Fact]
    public void Intersect_DegenerateCrop_ReturnsNull()
    {
        var region = new RegionRectFraction(0.1, 0.1, 0.5, 0.5);
        var degenerate = new CropFraction(0.5, 0.0, 0.0, 1.0);  // Width = 0
        RegionGeometry.Intersect(region, degenerate).Should().BeNull();
    }

    // ─── Intersect: 交差ありケース ────────────────────────────────

    [Fact]
    public void Intersect_FullCrop_ReturnsRegionUnchanged()
    {
        // Crop 無効 (Full) のときは SourceRect も CropLocalRect も region と一致 (浮動小数誤差込み)
        var region = new RegionRectFraction(0.2, 0.3, 0.4, 0.5);
        var result = RegionGeometry.Intersect(region, CropFraction.Full);

        result.Should().NotBeNull();
        AssertRectsApproximatelyEqual(result!.Value.SourceRect, region);
        AssertRectsApproximatelyEqual(result.Value.CropLocalRect, region);
    }

    [Fact]
    public void Intersect_RegionFullyInsideCrop_RescalesCropLocal()
    {
        // Crop = 右半分 (X=0.5, W=0.5)、 Region = Crop 中央付近 (X=0.6 〜 0.8 = W=0.2)
        var region = new RegionRectFraction(0.6, 0.0, 0.2, 1.0);
        var crop = new CropFraction(0.5, 0.0, 0.5, 1.0);

        var result = RegionGeometry.Intersect(region, crop);
        result.Should().NotBeNull();

        // SourceRect は元画像座標で region と同じ
        result!.Value.SourceRect.X.Should().BeApproximately(0.6, 1e-9);
        result.Value.SourceRect.Width.Should().BeApproximately(0.2, 1e-9);

        // CropLocalRect は Crop 内で 0–1 にリスケール
        // (0.6 - 0.5) / 0.5 = 0.2、 width: 0.2 / 0.5 = 0.4
        result.Value.CropLocalRect.X.Should().BeApproximately(0.2, 1e-9);
        result.Value.CropLocalRect.Width.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void Intersect_PartialOverlap_ClipsAtCropBoundary()
    {
        // Crop = (0.5, 0, 0.5, 1)、 Region = (0.3, 0, 0.4, 1) → 交差は X: 0.5 〜 0.7 = W: 0.2
        var region = new RegionRectFraction(0.3, 0.0, 0.4, 1.0);
        var crop = new CropFraction(0.5, 0.0, 0.5, 1.0);

        var result = RegionGeometry.Intersect(region, crop);
        result.Should().NotBeNull();
        result!.Value.SourceRect.X.Should().BeApproximately(0.5, 1e-9);
        result.Value.SourceRect.Width.Should().BeApproximately(0.2, 1e-9);
        // CropLocal: X (0.5-0.5)/0.5 = 0、 W 0.2/0.5 = 0.4
        result.Value.CropLocalRect.X.Should().BeApproximately(0.0, 1e-9);
        result.Value.CropLocalRect.Width.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void Intersect_OffsetCrop_PreservesYAxis()
    {
        // X / Y 両軸の独立性を検証 (片軸のテストだけだと縮退バグを見落とす)
        var region = new RegionRectFraction(0.1, 0.2, 0.3, 0.4);  // (0.1, 0.2)–(0.4, 0.6)
        var crop = new CropFraction(0.0, 0.1, 0.6, 0.5);          // (0.0, 0.1)–(0.6, 0.6)
        // 交差: X = max(0.1, 0) ~ min(0.4, 0.6) = 0.1–0.4
        //       Y = max(0.2, 0.1) ~ min(0.6, 0.6) = 0.2–0.6 → W=0.3, H=0.4

        var result = RegionGeometry.Intersect(region, crop);
        result.Should().NotBeNull();
        AssertRectsApproximatelyEqual(result!.Value.SourceRect, new RegionRectFraction(0.1, 0.2, 0.3, 0.4));

        // CropLocal Y: (0.2 - 0.1) / 0.5 = 0.2、 H: 0.4 / 0.5 = 0.8
        result.Value.CropLocalRect.Y.Should().BeApproximately(0.2, 1e-9);
        result.Value.CropLocalRect.Height.Should().BeApproximately(0.8, 1e-9);
    }

    // ─── ComputeSourceToCellScale: 回転なし (恒等) ──────────────────

    [Fact]
    public void ComputeSourceToCellScale_Identity_UniformScale_ReturnsSameScaleBothAxes()
    {
        // src 100x200 → dst 50x100 (uniform 0.5x)、 transform は Identity
        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(
            ImageTransform.Identity,
            transformedSrcRectWidth: 100, transformedSrcRectHeight: 200,
            dstRectWidth: 50, dstRectHeight: 100);

        sx.Should().BeApproximately(0.5, 1e-9);
        sy.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void ComputeSourceToCellScale_Identity_NonUniform_FillModeProducesDifferentSxSy()
    {
        // Fill mode 想定: src 100x100 (transformed) → dst 200x50 (異方スケール)
        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(
            ImageTransform.Identity,
            transformedSrcRectWidth: 100, transformedSrcRectHeight: 100,
            dstRectWidth: 200, dstRectHeight: 50);

        sx.Should().BeApproximately(2.0, 1e-9);
        sy.Should().BeApproximately(0.5, 1e-9);
    }

    // ─── ComputeSourceToCellScale: 90° / 270° で軸 swap ────────────────

    [Fact]
    public void ComputeSourceToCellScale_Cw90_SwapsAxes()
    {
        // 親が 90° CW 回転されているとき、 source の X 軸は transformed の Y 軸に対応する。
        // src(transformed) 200x100、 dst 50x100 → sx_transformed=0.25, sy_transformed=1.0
        // 軸 swap で source の (sx, sy) = (sy_transformed, sx_transformed) = (1.0, 0.25)
        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(
            new ImageTransform(Rotation.Cw90, false, false),
            transformedSrcRectWidth: 200, transformedSrcRectHeight: 100,
            dstRectWidth: 50, dstRectHeight: 100);

        sx.Should().BeApproximately(1.0, 1e-9);
        sy.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void ComputeSourceToCellScale_Cw270_SwapsAxes()
    {
        // 270° も同じく source X ↔ transformed Y。 Cw90 と同じ結果になる。
        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(
            new ImageTransform(Rotation.Cw270, false, false),
            transformedSrcRectWidth: 200, transformedSrcRectHeight: 100,
            dstRectWidth: 50, dstRectHeight: 100);

        sx.Should().BeApproximately(1.0, 1e-9);
        sy.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void ComputeSourceToCellScale_Cw180_DoesNotSwap()
    {
        // 180° は X / Y を入れ替えない (符号反転だけ、 scale magnitude は同じ)。
        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(
            new ImageTransform(Rotation.Cw180, false, false),
            transformedSrcRectWidth: 100, transformedSrcRectHeight: 200,
            dstRectWidth: 50, dstRectHeight: 100);

        sx.Should().BeApproximately(0.5, 1e-9);
        sy.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void ComputeSourceToCellScale_FlipDoesNotAffectMagnitude()
    {
        // Flip は scale の符号 (向き) には影響するが、 ここでの scale は magnitude (常に正) なので
        // FlipX / FlipY を立てても結果は変わらない。 region asset は左右反転されない仕様
        // (回転・反転無視) のため、 これで正しい。
        var (sxNoFlip, syNoFlip) = RegionGeometry.ComputeSourceToCellScale(
            ImageTransform.Identity, 100, 200, 50, 100);
        var (sxFlip, syFlip) = RegionGeometry.ComputeSourceToCellScale(
            new ImageTransform(Rotation.None, FlipX: true, FlipY: true), 100, 200, 50, 100);

        sxFlip.Should().BeApproximately(sxNoFlip, 1e-9);
        syFlip.Should().BeApproximately(syNoFlip, 1e-9);
    }

    // ─── ComputeSourceToCellScale: 退化入力 ──────────────────────

    [Fact]
    public void ComputeSourceToCellScale_ZeroSrc_ReturnsZeroZero()
    {
        var (sx, sy) = RegionGeometry.ComputeSourceToCellScale(
            ImageTransform.Identity,
            transformedSrcRectWidth: 0, transformedSrcRectHeight: 100,
            dstRectWidth: 50, dstRectHeight: 100);

        sx.Should().Be(0.0);
        sy.Should().Be(0.0);
    }

    // ─── ヘルパ ────────────────────────────────────────────────

    private static void AssertRectsApproximatelyEqual(
        RegionRectFraction actual, RegionRectFraction expected, double tolerance = 1e-9)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance);
        actual.Y.Should().BeApproximately(expected.Y, tolerance);
        actual.Width.Should().BeApproximately(expected.Width, tolerance);
        actual.Height.Should().BeApproximately(expected.Height, tolerance);
    }
}

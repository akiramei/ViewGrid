using FluentAssertions;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Core.Tests.Geometry;

/// <summary>
/// <see cref="RegionGeometry"/> の純粋関数 2 つの境界・幾何検証。
/// 設計詳細は HANDOFF/pending-designs.md の 「ProtectedRegion Phase 1 設計メモ」 を参照。
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

    // ─── ComputeOverlayPlacement: PhotoBoard 変形なし (恒等) ─────────────

    [Fact]
    public void ComputeOverlayPlacement_Identity_PutsOverlayAtCellLocalCenter()
    {
        // BaseRect = (100, 200, 400, 300)、 PhotoBoard 変形なし
        var item = MakeItem(100, 200, 400, 300, offsetX: 0, offsetY: 0, rotationDeg: 0);
        // cell-local rect = 中央 50% (X=0.25, Y=0.25, W=0.5, H=0.5)
        var cellLocal = new RegionRectFraction(0.25, 0.25, 0.5, 0.5);

        var placement = RegionGeometry.ComputeOverlayPlacement(item, cellLocal);

        // 中心 = BaseRect.X + cellW * 0.5 = 100 + 200 = 300
        //       BaseRect.Y + cellH * 0.5 = 200 + 150 = 350
        placement.CanvasCenterX.Should().BeApproximately(300.0, 1e-9);
        placement.CanvasCenterY.Should().BeApproximately(350.0, 1e-9);
        // サイズ = 0.5 * cell サイズ
        placement.WidthPx.Should().BeApproximately(200.0, 1e-9);
        placement.HeightPx.Should().BeApproximately(150.0, 1e-9);
    }

    // ─── ComputeOverlayPlacement: offset のみ ────────────────────────

    [Fact]
    public void ComputeOverlayPlacement_OffsetOnly_ShiftsCenter()
    {
        var item = MakeItem(100, 200, 400, 300, offsetX: 10, offsetY: -5, rotationDeg: 0);
        var cellLocal = new RegionRectFraction(0.25, 0.25, 0.5, 0.5);

        var placement = RegionGeometry.ComputeOverlayPlacement(item, cellLocal);

        // offset 分だけ中心が動く (回転は 0 なので回転後座標は offset 適用済みと同じ)
        placement.CanvasCenterX.Should().BeApproximately(310.0, 1e-9);
        placement.CanvasCenterY.Should().BeApproximately(345.0, 1e-9);
        // サイズは offset で変わらない
        placement.WidthPx.Should().BeApproximately(200.0, 1e-9);
        placement.HeightPx.Should().BeApproximately(150.0, 1e-9);
    }

    // ─── ComputeOverlayPlacement: rotation around pivot ──────────────

    [Fact]
    public void ComputeOverlayPlacement_Rotation_RotatesAroundPivot()
    {
        // BaseRect = (100, 100, 200, 200)、 中心 (200, 200)、 pivot offset = 0 (中心)、
        // 90° CW 回転、 cell-local rect = 右半分 (X=0.5, Y=0, W=0.5, H=1)
        var item = MakeItem(100, 100, 200, 200, offsetX: 0, offsetY: 0, rotationDeg: 90);
        var cellLocal = new RegionRectFraction(0.5, 0.0, 0.5, 1.0);

        var placement = RegionGeometry.ComputeOverlayPlacement(item, cellLocal);

        // 回転前: cell-local center = (150, 100) → canvas (250, 200)、 pivot (200, 200)
        // 90° CW で (250, 200) → pivot 中心に回転 → (200, 250)
        // (回転は反時計回りが正の数学的慣例だが、 90° の回転で +X 方向 → +Y 方向 は時計回り、
        // 実装は標準の cos / sin を使うため、 +X が +Y にマップされる方向 (時計回り)。)
        placement.CanvasCenterX.Should().BeApproximately(200.0, 1e-9);
        placement.CanvasCenterY.Should().BeApproximately(250.0, 1e-9);
        // overlay は canvas 水平で axis-aligned 描画 → サイズは cell-local pixel size と同一
        placement.WidthPx.Should().BeApproximately(100.0, 1e-9);
        placement.HeightPx.Should().BeApproximately(200.0, 1e-9);
    }

    [Fact]
    public void ComputeOverlayPlacement_NonZeroPivot_RotatesAroundOffsetPivot()
    {
        // pivot offset が 0 でないケース。 pivot がセル中心からずれた位置にあると、
        // 回転中心も移動する。
        var item = MakeItem(0, 0, 200, 200,
            offsetX: 0, offsetY: 0,
            rotationDeg: 180, // 180° = pivot を中心とする点対称
            pivotOffX: 10, pivotOffY: 20);
        // cell-local center = (100, 100)、 pivot = cell center + offset = (110, 120)
        var cellLocal = new RegionRectFraction(0.0, 0.0, 1.0, 1.0);

        var placement = RegionGeometry.ComputeOverlayPlacement(item, cellLocal);

        // 180° で pivot 中心の点対称: (100, 100) → 2*(110,120) - (100,100) = (120, 140)
        placement.CanvasCenterX.Should().BeApproximately(120.0, 1e-9);
        placement.CanvasCenterY.Should().BeApproximately(140.0, 1e-9);
    }

    [Fact]
    public void ComputeOverlayPlacement_OffsetAndRotation_AppliedInOrder()
    {
        // offset → rotation の順で適用される (rotation pivot も offset 適用後の座標で評価)。
        // BaseRect = (0, 0, 100, 100)、 offset = (50, 0)、 rotation 90°、 pivot offset = 0
        var item = MakeItem(0, 0, 100, 100, offsetX: 50, offsetY: 0, rotationDeg: 90);
        var cellLocal = new RegionRectFraction(0.0, 0.0, 1.0, 1.0);

        var placement = RegionGeometry.ComputeOverlayPlacement(item, cellLocal);

        // cell-local center = (50, 50)、 offset 適用後 (100, 50)、 pivot = cell center + offset = (100, 50)
        // pivot を中心に 90° → 自分自身が pivot なので位置不変 (100, 50)
        placement.CanvasCenterX.Should().BeApproximately(100.0, 1e-9);
        placement.CanvasCenterY.Should().BeApproximately(50.0, 1e-9);
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

    private static PhotoBoardItem MakeItem(
        int x, int y, int w, int h,
        double offsetX, double offsetY,
        double rotationDeg,
        double pivotOffX = 0,
        double pivotOffY = 0)
        => new(
            BaseRect: new PixelRect(x, y, w, h),
            OffsetX: offsetX,
            OffsetY: offsetY,
            RotationDeg: rotationDeg,
            RotationPivotOffsetX: pivotOffX,
            RotationPivotOffsetY: pivotOffY,
            FrameSidePx: 0,
            FrameBottomPx: 0,
            FrameAlpha: 0,
            ShadowOffsetX: 0,
            ShadowOffsetY: 0,
            ShadowSigma: 0,
            ShadowAlpha: 0);
}

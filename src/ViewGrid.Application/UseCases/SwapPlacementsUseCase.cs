using ErrorOr;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 2 つの配置の Position を入れ替える。NxM 同士でも、互いの新位置が
/// グリッド境界・他配置・互いに重複しない場合に成立する。
/// </summary>
public sealed class SwapPlacementsUseCase(
    IGridCanvasRepository gridRepository,
    IImageCopyRepository copyRepository,
    IGridPlacementRepository placementRepository)
{
    public async Task<ErrorOr<Success>> ExecuteAsync(Guid aId, Guid bId, CancellationToken ct = default)
    {
        if (aId == bId)
            return Result.Success;

        var a = await placementRepository.FindByIdAsync(aId, ct);
        var b = await placementRepository.FindByIdAsync(bId, ct);
        if (a is null || b is null)
            return Error.NotFound("Placement.NotFound", "入れ替え対象の配置が見つかりません。");

        if (a.GridId != b.GridId)
            return Error.Validation("Placement.DifferentGrid", "別のグリッドに属する配置は入れ替えできません。");

        var aCopy = await copyRepository.FindByIdAsync(a.CopyId, ct);
        var bCopy = await copyRepository.FindByIdAsync(b.CopyId, ct);
        if (aCopy is null || bCopy is null)
            return Error.NotFound("ImageCopy.NotFound", "対象の論理コピーが見つかりません。");

        var grid = await gridRepository.FindByIdAsync(a.GridId, ct);
        if (grid is null)
            return Error.NotFound("Grid.NotFound", $"GridCanvas {a.GridId} が見つかりません。");

        // 既存配置（a, b 自身を除いたもの）を OccupySize 付きで取得。
        // OccupySize は配置単位の固有特性なので各 placement から直接読む。
        var existing = await placementRepository.FindByGridIdAsync(a.GridId, ct);
        var others = new List<ExistingPlacement>(existing.Count);
        foreach (var p in existing)
        {
            if (p.Id == aId || p.Id == bId) continue;
            others.Add(new ExistingPlacement(p.Id, p.Position, p.OccupySize));
        }

        var aOriginal = a.Position;
        var bOriginal = b.Position;

        // 検証 1: a を b 元位置に置けるか（境界 + others と非重複）
        var validateA = PlacementValidator.Validate(
            a.OccupySize, bOriginal, grid.GridRows, grid.GridCols, others);
        if (!validateA.IsValid)
            return MapValidation(validateA);

        // 検証 2: b を a 元位置に置けるか
        var validateB = PlacementValidator.Validate(
            b.OccupySize, aOriginal, grid.GridRows, grid.GridCols, others);
        if (!validateB.IsValid)
            return MapValidation(validateB);

        // 検証 3: a の新位置と b の新位置が互いに重ならないか
        var aNewCells = PlacementValidator.OccupiedCells(bOriginal, a.OccupySize).ToHashSet();
        foreach (var cell in PlacementValidator.OccupiedCells(aOriginal, b.OccupySize))
        {
            if (aNewCells.Contains(cell))
                return Error.Conflict(
                    "Placement.SwapOverlap",
                    "入れ替え後の互いの占有範囲が重複します。");
        }

        // 入れ替え実行
        a.Position = bOriginal;
        b.Position = aOriginal;

        var ua = await placementRepository.UpdateAsync(a, ct);
        if (ua.IsError)
            return ua.Errors;

        var ub = await placementRepository.UpdateAsync(b, ct);
        if (ub.IsError)
        {
            // ロールバック試行: a を元位置に戻す
            a.Position = aOriginal;
            await placementRepository.UpdateAsync(a, ct);
            return ub.Errors;
        }

        return Result.Success;
    }

    private static Error MapValidation(PlacementValidationResult result) => result.Reason switch
    {
        PlacementInvalidReason.OutOfBounds =>
            Error.Validation("Placement.OutOfBounds", "入れ替え先がグリッド範囲を超えています。"),
        PlacementInvalidReason.Conflict =>
            Error.Conflict("Placement.Conflict", "入れ替え先が他の配置と重複しています。"),
        _ => Error.Validation("Placement.Invalid", "入れ替えできません。"),
    };
}

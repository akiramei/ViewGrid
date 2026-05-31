using ErrorOr;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.UseCases;

/// <summary>
/// 2 つの配置の Position を入れ替える。NxM 同士でも、互いの新位置が
/// グリッド境界・他配置と重複しない場合に成立する。
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

        var aOriginal = a.Position;
        var bOriginal = b.Position;

        var others = await LoadOtherPlacementsAsync(a.GridId, aId, bId, ct);

        PlacementValidationResult ValidateAt(OccupySize size, CellPosition position) =>
            PlacementValidator.Validate(size, position, grid.GridRows, grid.GridCols, others);

        // 入れ替え後の各位置が、グリッド境界と他配置に対して妥当であることを検証する。
        // (a を b の元位置へ、b を a の元位置へ。両者の妥当性が取れれば入れ替え可能。)
        if (ValidateAt(a.OccupySize, bOriginal) is { IsValid: false } invalidA)
            return MapValidation(invalidA);
        if (ValidateAt(b.OccupySize, aOriginal) is { IsValid: false } invalidB)
            return MapValidation(invalidB);

        // 入れ替え実行。
        a.Position = bOriginal;
        b.Position = aOriginal;

        var ua = await placementRepository.UpdateAsync(a, ct);
        if (ua.IsError)
            return ua.Errors;

        var ub = await placementRepository.UpdateAsync(b, ct);
        if (ub.IsError)
        {
            a.Position = aOriginal;
            await placementRepository.UpdateAsync(a, ct);
            return ub.Errors;
        }

        return Result.Success;
    }

    private async Task<List<ExistingPlacement>> LoadOtherPlacementsAsync(
        Guid gridId, Guid aId, Guid bId, CancellationToken ct)
    {
        var existing = await placementRepository.FindByGridIdAsync(gridId, ct);
        var others = new List<ExistingPlacement>(existing.Count);
        foreach (var p in existing)
        {
            if (p.Id == aId || p.Id == bId)
                continue;
            others.Add(new ExistingPlacement(p.Id, p.Position, p.OccupySize));
        }
        return others;
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

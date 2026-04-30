using System.Threading;
using System.Threading.Tasks;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Services;

/// <summary>
/// 論理コピーの ManualCrop / AutoCrop 設定から「実効的なクロップ比率」を解決するサービス。
/// <para>
/// 優先順位は <c>ManualCrop != null → AutoCrop != null → null</c>。
/// ManualCrop と AutoCrop が同時に設定されていても ManualCrop が排他的に勝つ
/// （UI 上もラジオ 3 択で切替）。
/// </para>
/// <para>
/// Renderer / View / Use case の 3 経路で同一比率を共有することで、自動と手動の
/// 適用結果が表示・出力で揃うようにする。
/// </para>
/// </summary>
public interface IImageCropResolver
{
    /// <summary>
    /// 指定 ImageCopy の実効クロップ比率を解決する。クロップ無効時は <c>null</c>。
    /// AutoCrop 経路では原画像読込み + 走査が走るため I/O コストあり（実装はキャッシュ前提）。
    /// </summary>
    Task<CropFraction?> ResolveAsync(ImageCopy copy, ImageAsset asset, CancellationToken ct = default);
}

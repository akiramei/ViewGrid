using System;
using System.Threading;
using System.Threading.Tasks;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Services;

/// <summary>
/// 単色余白の自動トリミング走査結果を <see cref="AutoCropFraction"/>（0–1 比率）として
/// 解決するサービス。
/// <para>
/// 比率を返すことで Renderer / View / Use case の 3 経路が同一の走査結果を座標系を
/// 揃えて共有できる（Renderer は原画像サイズ、View はサムネサイズ、Use case は
/// 任意のサイズに比率を掛ける）。
/// </para>
/// <para>
/// 実装はキャッシュを持つことが期待される。結果が <c>null</c> または
/// <see cref="AutoCropFraction.IsFull"/> はクロップ無効を意味する。
/// </para>
/// </summary>
public interface IAutoCropBboxResolver
{
    /// <summary>
    /// 指定アセットの AutoCrop 走査比率を取得する。Cache miss なら原画像を読み込んで走査し、
    /// 結果を保存する。原画像読込みに失敗したら <c>null</c>。
    /// </summary>
    Task<AutoCropFraction?> ResolveAsync(
        Guid assetId,
        string sourceImageAbsolutePath,
        AutoCropSettings settings,
        CancellationToken ct = default);
}

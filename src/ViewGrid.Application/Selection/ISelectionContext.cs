namespace ViewGrid.Application.Selection;

/// <summary>
/// 「いま何が選択されているか」を表す抽象。配置タブの右ペイン下段や、将来の 3 ペイン化時の
/// プロパティパネルなど、文脈依存 UI が型で分岐するための識別子として使う。
/// <para>
/// 具体型は record で網羅: <see cref="NoSelection"/> / <see cref="GridSelection"/> /
/// <see cref="PlacementSelection"/>。これらだけで現状の右ペイン文脈は表現できる。
/// 将来 <c>AssetSelection</c> / <c>VariantSelection</c> 等を追加して拡張する。
/// </para>
/// <para>
/// View 側では <see cref="ContentControl"/> の <see cref="DataTemplates"/> で型ごとの
/// テンプレートを宣言し、<see cref="GridWorkspaceViewModel.CurrentSelection"/> をバインドするだけ
/// で文脈に応じた UI が出るようにする。VM 側の if 文を増やさない設計が目的。
/// </para>
/// </summary>
public interface ISelectionContext
{
}

/// <summary>何も選択されていない状態。空状態案内の表示に使う。</summary>
public sealed record NoSelection : ISelectionContext
{
    public static readonly NoSelection Instance = new();
}

/// <summary>
/// グリッドが選択されているがそのグリッド上の配置は未選択（配置タブの初期状態）。
/// グリッドの基本情報（名前 / 行列 / キャンバスサイズ等）の表示に使う。
/// </summary>
public sealed record GridSelection(Guid GridId) : ISelectionContext;

/// <summary>
/// グリッド上の特定の配置が選択されている状態。Inspector （配置固有プロパティ + 共有特性への
/// ナビゲート）の表示に使う。
/// </summary>
public sealed record PlacementSelection(Guid GridId, Guid PlacementId, Guid CopyId) : ISelectionContext;

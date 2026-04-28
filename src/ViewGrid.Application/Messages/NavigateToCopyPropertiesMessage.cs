using System;

namespace ViewGrid.Application.Messages;

/// <summary>
/// 配置タブの <c>PlacementInspectorView</c> から「特性を編集 →」が押されたときに送出される
/// ナビゲーション要求。受信側（<c>MainWindowViewModel</c>）は準備タブに切り替え、
/// 当該アセットと論理コピーを単一選択にして <c>CopyPropertiesView</c> をフォーカスする。
/// </summary>
/// <param name="AssetId">対象アセットの識別子（準備タブのアセット一覧での選択先）。</param>
/// <param name="CopyId">対象論理コピーの識別子（コピー一覧での選択先）。</param>
public sealed record NavigateToCopyPropertiesMessage(Guid AssetId, Guid CopyId);

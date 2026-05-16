namespace ViewGrid.Application.Messages;

/// <summary>
/// アセットライブラリ（<see cref="ViewGrid.Core.Entities.ImageAsset"/> の集合）に変化が生じた通知。
/// 受信側 (<see cref="ViewGrid.Application.ViewModels.AssetLibraryViewModel"/>) は Assets を
/// リポジトリから再ロードする。 Cascade 削除や外部経由のアセット変更を反映する用途。
///
/// 通常のインポート / 削除フローでは AssetLibraryViewModel 自身が Assets を直接更新するので
/// このメッセージは送らない。 GridWorkspaceViewModel など別 VM がアセット数を変える場合に送る。
/// </summary>
public sealed record AssetLibraryChangedMessage;

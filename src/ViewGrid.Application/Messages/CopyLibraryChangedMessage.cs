namespace ViewGrid.Application.Messages;

/// <summary>
/// 候補ライブラリ（<see cref="ViewGrid.Core.Entities.ImageAsset"/> /
/// <see cref="ViewGrid.Core.Entities.ImageCopy"/> の集合）に変化が生じた通知。
/// 受信側は配置タブの候補一覧を再ロードして、準備タブの操作を即座に反映する。
/// </summary>
public sealed record CopyLibraryChangedMessage;

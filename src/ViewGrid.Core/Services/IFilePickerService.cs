using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ViewGrid.Core.Services;

/// <summary>
/// ファイル選択 UI（OS ダイアログ）を抽象化する。
/// ViewModel 層から UI フレームワーク依存を切り離すために Presentation 層で実装する。
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// 画像ファイルの選択ダイアログを表示し、選ばれたファイルの絶対パスを返す。
    /// キャンセル時は空配列。
    /// </summary>
    Task<IReadOnlyList<string>> PickImagesAsync(CancellationToken ct = default);

    /// <summary>
    /// PNG 保存ダイアログを表示し、保存先の絶対パスを返す。
    /// キャンセル時は <c>null</c>。
    /// </summary>
    /// <param name="suggestedFileName">既定のファイル名（拡張子含む）。</param>
    Task<string?> PickSavePngPathAsync(string suggestedFileName, CancellationToken ct = default);
}

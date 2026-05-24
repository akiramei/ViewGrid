using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.Localization;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブの出力 / プレビューパネル (右カラム下) の VM。 OutputMode (Normal / PhotoBoard) /
/// TrimMode / PhotoBoardStyle / PhotoBoardIntensity の選択と、 プレビュー生成・PNG エクスポートを担う。
/// <para>
/// Phase 4 で <see cref="GridWorkspaceViewModel"/> から抽出。 グリッドコンテキスト
/// (<see cref="GridCanvasItemViewModel.GridId"/> や <see cref="GridCanvasItemViewModel.Name"/>) や
/// 全体ステータス (IsBusy / StatusMessage) は親 (<see cref="IGridOutputContext"/>) から借りる。
/// </para>
/// </summary>
public sealed partial class GridOutputViewModel : ViewModelBase
{
    private readonly IGridOutputContext _context;
    private readonly RenderGridUseCase _renderUseCase;
    private readonly ExportGridUseCase _exportUseCase;
    private readonly IFilePickerService _filePicker;
    private readonly ILocalizationService _loc;
    private readonly ILogger _logger;

    /// <summary>
    /// プレビュー / PNG 出力の最上位モード。 通常 / 写真ボードを切り替える。
    /// 切り出し (<see cref="SelectedTrimMode"/>) とは直交軸で、 両方を組み合わせて使う。
    /// 永続化はせず、 セッション内のオプション扱い (既定 Normal)。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhotoBoardMode))]
    [NotifyPropertyChangedFor(nameof(IsNormalMode))]
    public partial OutputMode SelectedOutputMode { get; set; } = OutputMode.Normal;

    /// <summary>
    /// プレビュー / PNG 出力の切り出し設定。 <see cref="TrimMode.None"/> はキャンバス全面、
    /// <see cref="TrimMode.OccupiedCells"/> は占有セルの bbox で切り出し、
    /// <see cref="TrimMode.DrawnPixels"/> は α&gt;0 のピクセル走査で求めた bbox で切り出し。
    /// PhotoBoard モードでは「合成後の画像」に対して同じセマンティクスで適用される。
    /// 永続化はせず、 セッション内のオプション扱い (既定 None)。
    /// </summary>
    [ObservableProperty]
    public partial TrimMode SelectedTrimMode { get; set; } = TrimMode.None;

    public IReadOnlyList<TrimMode> TrimModeOptions { get; } =
        [TrimMode.None, TrimMode.OccupiedCells, TrimMode.DrawnPixels];

    public IReadOnlyList<OutputMode> OutputModeOptions { get; } =
        [OutputMode.Normal, OutputMode.PhotoBoard];

    public IReadOnlyList<PhotoBoardStyle> PhotoBoardStyleOptions { get; } =
        [PhotoBoardStyle.Natural, PhotoBoardStyle.Rough, PhotoBoardStyle.Scattered];

    /// <summary>
    /// PhotoBoard モードのスタイルプリセット。 各値は係数セット
    /// (<see cref="PhotoBoardStyleCoefficients"/>) を引くキー。 <see cref="OutputMode.Normal"/>
    /// 時は無視される。 永続化はせずセッション内のオプション扱い (既定 Natural)。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStyleNatural))]
    [NotifyPropertyChangedFor(nameof(IsStyleRough))]
    [NotifyPropertyChangedFor(nameof(IsStyleScattered))]
    public partial PhotoBoardStyle SelectedPhotoBoardStyle { get; set; } = PhotoBoardStyle.Natural;

    /// <summary>選択中スタイルがナチュラルかどうか (View 側のスタイルボタン IsChecked 表示)。</summary>
    public bool IsStyleNatural => SelectedPhotoBoardStyle == PhotoBoardStyle.Natural;

    /// <summary>選択中スタイルがラフかどうか。</summary>
    public bool IsStyleRough => SelectedPhotoBoardStyle == PhotoBoardStyle.Rough;

    /// <summary>選択中スタイルがバラ撒きかどうか。</summary>
    public bool IsStyleScattered => SelectedPhotoBoardStyle == PhotoBoardStyle.Scattered;

    /// <summary>
    /// PhotoBoard モードの強度。 <c>0.0</c> で「ほぼ整列」(係数すべて 0 倍)、
    /// <c>0.5</c> でスタイル基準値そのまま、 <c>1.0</c> で「最大効果」(係数 2 倍)。
    /// UI 上では数値非表示で「控えめ ↔ 大胆」 の感覚スライダーとして見せる。
    /// 永続化はせずセッション内のオプション扱い (既定 0.5)。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetPhotoBoardIntensityCommand))]
    public partial double SelectedPhotoBoardIntensity { get; set; } = 0.5;

    /// <summary>
    /// <see cref="SelectedOutputMode"/> が <see cref="OutputMode.PhotoBoard"/> のときに <c>true</c>。
    /// View 側でスタイル / 強度パネルの表示切替に使う。
    /// </summary>
    public bool IsPhotoBoardMode => SelectedOutputMode == OutputMode.PhotoBoard;

    /// <summary>
    /// <see cref="SelectedOutputMode"/> が <see cref="OutputMode.Normal"/> のときに <c>true</c>。
    /// 出力モードの ToggleButton ペアの IsChecked 表示に使う (PhotoBoard モードと排他)。
    /// </summary>
    public bool IsNormalMode => SelectedOutputMode == OutputMode.Normal;

    public GridOutputViewModel(
        IGridOutputContext context,
        RenderGridUseCase renderUseCase,
        ExportGridUseCase exportUseCase,
        IFilePickerService filePicker,
        ILocalizationService loc,
        ILogger logger)
    {
        _context = context;
        _renderUseCase = renderUseCase;
        _exportUseCase = exportUseCase;
        _filePicker = filePicker;
        _loc = loc;
        _logger = logger;
    }

    /// <summary>「既定に戻す」ボタンの活性条件。 スライダーが既定値 (0.5) から
    /// 動いていれば <c>true</c>。 既定位置のときは <c>false</c> でボタンが無効表示になる。</summary>
    private bool CanResetPhotoBoardIntensity() =>
        Math.Abs(SelectedPhotoBoardIntensity - 0.5) > 0.001;

    /// <summary>
    /// スタイル切替時は強度を既定値 (0.5 = そのスタイルのベースライン) に戻す。
    /// 各スタイルは <see cref="PhotoBoardStyleCoefficients"/> ベースラインが大きく異なるため、
    /// 同じ intensity でも見え方が大きく変わる。 「スタイルを選んだ直後はそのスタイルの基準値で
    /// 見える」 状態に揃えることで、 比較の起点が明確になる UX 契約。
    /// </summary>
    partial void OnSelectedPhotoBoardStyleChanged(PhotoBoardStyle value) =>
        SelectedPhotoBoardIntensity = 0.5;

    [RelayCommand]
    private void SelectOutputModeNormal() => SelectedOutputMode = OutputMode.Normal;

    [RelayCommand]
    private void SelectOutputModePhotoBoard() => SelectedOutputMode = OutputMode.PhotoBoard;

    [RelayCommand]
    private void ApplyPhotoBoardStyleNatural() => SelectedPhotoBoardStyle = PhotoBoardStyle.Natural;

    [RelayCommand]
    private void ApplyPhotoBoardStyleRough() => SelectedPhotoBoardStyle = PhotoBoardStyle.Rough;

    [RelayCommand]
    private void ApplyPhotoBoardStyleScattered() => SelectedPhotoBoardStyle = PhotoBoardStyle.Scattered;

    /// <summary>「配置の乱れ」 スライダーを既定値 (0.5) に戻す。 スライダーは正確に
    /// 中央へ戻すのが難しい UI のため、 明示的なリセット手段を提供する。
    /// 既定位置にあるときは <see cref="CanResetPhotoBoardIntensity"/> で無効化される。</summary>
    [RelayCommand(CanExecute = nameof(CanResetPhotoBoardIntensity))]
    private void ResetPhotoBoardIntensity() => SelectedPhotoBoardIntensity = 0.5;

    /// <summary>
    /// 現在の VM 設定からレンダリングオプションを構築する。 PhotoBoard モード時は係数を
    /// スタイル + 強度から派生させる。 Normal 時は coefs=null。
    /// </summary>
    private RenderOptions BuildRenderOptions()
    {
        var coefs = SelectedOutputMode == OutputMode.PhotoBoard
            ? PhotoBoardStyleCoefficients.For(SelectedPhotoBoardStyle, SelectedPhotoBoardIntensity)
            : null;
        return new RenderOptions(
            TrimMode: SelectedTrimMode,
            OutputMode: SelectedOutputMode,
            PhotoBoardCoefficients: coefs);
    }

    /// <summary>
    /// 現在のグリッドをレンダリングして PNG バイト列を返す (プレビュー用)。
    /// 失敗時は <c>null</c> を返し、 親 (<see cref="IGridOutputContext.StatusMessage"/>) にエラーを格納する。
    /// </summary>
    public async Task<byte[]?> RequestPreviewAsync(CancellationToken ct = default)
    {
        var grid = _context.CurrentGrid;
        if (grid is null || _context.IsBusy) return null;

        var sw = Stopwatch.StartNew();
        try
        {
            _context.IsBusy = true;
            var options = BuildRenderOptions();
            var result = await _renderUseCase.ExecuteAsync(grid.GridId, options, ct);
            sw.Stop();
            if (result.IsError)
            {
                _context.StatusMessage = string.Join(", ", result.Errors);
                return null;
            }
            _context.StatusMessage = _loc.Format(
                "Status_PreviewGeneratedFmt",
                sw.ElapsedMilliseconds.ToString("N0", CultureInfo.CurrentCulture),
                result.Value.Length.ToString("N0", CultureInfo.CurrentCulture));
            LogPreviewRendered(_logger, options.TrimMode, options.OutputMode, sw.ElapsedMilliseconds, result.Value.Length);
            return result.Value;
        }
        finally { _context.IsBusy = false; }
    }

    /// <summary>
    /// SaveDialog を出して指定パスへ PNG として書き出す。 プレビュー経由しない高速パス。
    /// </summary>
    [RelayCommand]
    public async Task ExportToPngAsync(CancellationToken ct = default)
    {
        var grid = _context.CurrentGrid;
        if (grid is null || _context.IsBusy) return;

        var suggested = $"{SanitizeFileName(grid.Name)}.png";
        var path = await _filePicker.PickSavePngPathAsync(suggested, ct);
        if (string.IsNullOrEmpty(path)) return;

        var sw = Stopwatch.StartNew();
        try
        {
            _context.IsBusy = true;
            var options = BuildRenderOptions();
            var result = await _exportUseCase.ExecuteAsync(grid.GridId, path, options, ct);
            sw.Stop();
            _context.StatusMessage = result.IsError
                ? string.Join(", ", result.Errors)
                : _loc.Format(
                    "Status_PngExportTimingFmt",
                    sw.ElapsedMilliseconds.ToString("N0", CultureInfo.CurrentCulture),
                    Path.GetFileName(path),
                    result.Value.FileSizeBytes.ToString("N0", CultureInfo.CurrentCulture));
            if (!result.IsError)
                LogPngExported(_logger, options.TrimMode, options.OutputMode, sw.ElapsedMilliseconds, result.Value.FileSizeBytes);
        }
        finally { _context.IsBusy = false; }
    }

    /// <summary>
    /// プレビューで生成した既存 PNG バイト列を、 SaveDialog で選んだパスに書き出す。
    /// </summary>
    public async Task<bool> SavePngBytesAsync(byte[] bytes, CancellationToken ct = default)
    {
        var grid = _context.CurrentGrid;
        if (grid is null || bytes.Length == 0) return false;

        var suggested = $"{SanitizeFileName(grid.Name)}.png";
        var path = await _filePicker.PickSavePngPathAsync(suggested, ct);
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            _context.StatusMessage = _loc.Format(
                "Status_PngExportedFmt",
                Path.GetFileName(path),
                bytes.LongLength.ToString("N0", CultureInfo.CurrentCulture));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context.StatusMessage = _loc.Format("Status_SaveFailedFmt", ex.Message);
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "viewgrid-export" : clean;
    }

    [LoggerMessage(EventId = 5008, Level = LogLevel.Information, Message = "プレビュー生成: trim={TrimMode} output={OutputMode} elapsed={ElapsedMs}ms bytes={Bytes}")]
    private static partial void LogPreviewRendered(ILogger logger, TrimMode trimMode, OutputMode outputMode, long elapsedMs, int bytes);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Information, Message = "PNG 出力: trim={TrimMode} output={OutputMode} elapsed={ElapsedMs}ms bytes={Bytes}")]
    private static partial void LogPngExported(ILogger logger, TrimMode trimMode, OutputMode outputMode, long elapsedMs, long bytes);
}

/// <summary>
/// <see cref="GridOutputViewModel"/> が親 (<see cref="GridWorkspaceViewModel"/>) から借りるコンテキスト。
/// グリッド識別 + 全体ステータス (busy / message) の読み書きだけを露出する小さい contract で、
/// 親 VM 全体への依存を避けてテスト時にもモック差し替え可能にする。
/// </summary>
public interface IGridOutputContext
{
    /// <summary>現在ワークスペースが表示しているグリッド。 未選択時は <c>null</c>。</summary>
    GridCanvasItemViewModel? CurrentGrid { get; }

    /// <summary>グローバル busy フラグ。 プレビュー / エクスポート中に true。</summary>
    bool IsBusy { get; set; }

    /// <summary>グローバルステータスバー表示用メッセージ。 ローカライズ済み文字列または <c>null</c>。</summary>
    string? StatusMessage { get; set; }
}

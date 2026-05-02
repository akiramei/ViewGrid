using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;
using ViewGrid.Core.UseCases;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブで選択された <see cref="PlacementItemViewModel"/> の<b>配置固有プロパティ</b>を編集する。
/// <para>
/// 配置固有プロパティは現状 <see cref="PlacementItemViewModel.PixelOffsetX"/> /
/// <see cref="PlacementItemViewModel.PixelOffsetY"/> のみ。共有特性（Rotation/Flip/Scaling/
/// Trim/Align/Occupy）は <see cref="ImageCopy"/> 単位で持つため、
/// 「特性を編集 →」コマンドで <see cref="NavigateToCopyPropertiesMessage"/> を送出し、
/// 準備タブの <c>CopyPropertiesView</c> に編集を委譲する。
/// </para>
/// <para>
/// この設計により、配置タブの Inspector と準備タブの CopyProperties で共有特性を
/// 二重に編集できる従来構造（共有バナーが必要だった原因）を解消し、
/// Inspector を「配置固有の微調整」だけの最小構成に絞れる。
/// </para>
/// </summary>
public sealed partial class PlacementInspectorViewModel : ObservableObject
{
    private readonly UpdatePlacementOffsetUseCase _offsetUseCase;
    private readonly ForkPlacementVariantUseCase _forkUseCase;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IImageStorage _imageStorage;
    private readonly IUndoRedoService _history;
    private readonly IMessenger _messenger;
    private readonly ILogger<PlacementInspectorViewModel> _logger;

    /// <summary>
    /// 配置タブ Inspector 内に inline embed する共有特性編集（CopyPropertiesView）の VM。
    /// 配置ファースト UI 第 2 段階 Stage 3 で導入。<see cref="AttachAsync"/> 時に
    /// 当該 placement の <see cref="ImageCopy"/> を <see cref="CopyItemViewModel"/> に
    /// 包んで <see cref="CopyPropertiesViewModel.Attach"/> に渡し、Inspector と CopyProperties
    /// が同一バリアントを編集する状態に保つ。VM 自体は MainWindow が保持する singleton-ish
    /// インスタンスをコンストラクタ経由で共有する（準備タブと同じ実体）。準備タブが廃止
    /// される Stage 4 以降は本 Inspector 経由の attach のみが残る。
    /// </summary>
    public CopyPropertiesViewModel CopyProperties { get; }

    /// <summary>1 軸あたりの ΔX/ΔY の上限（VM 側でサニティ丸め）。</summary>
    public const int MaxPixelOffset = 4096;

    private PlacementItemViewModel? _source;
    private GridCanvasItemViewModel? _grid;
    private bool _suppressDirty;

    [ObservableProperty]
    public partial bool HasPlacement { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string HeaderLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PositionLabel { get; set; } = string.Empty;

    /// <summary>
    /// 選択中の配置がキャンバス上で占める描画域のピクセル寸法を示すラベル
    /// （例: "画像描画域: 640×480 px"）。Shift+ドラッグでの微調整時に
    /// 数値の感覚を掴むための参考情報として表示する。
    /// </summary>
    [ObservableProperty]
    public partial string ImageDrawSizeLabel { get; set; } = string.Empty;

    // 編集バッファ（配置固有のみ）
    [ObservableProperty] public partial int PixelOffsetX { get; set; }
    [ObservableProperty] public partial int PixelOffsetY { get; set; }

    /// <summary>
    /// 配置固有 (<see cref="IsDirty"/>) と共有特性 (<see cref="CopyProperties"/>.IsDirty) の
    /// いずれかに未保存編集があるか。Inspector 上部の統一保存バーの CanExecute と
    /// 「●」未保存マーカー可視化に使う。<see cref="CopyProperties"/>.PropertyChanged を
    /// 購読してリアクティブに通知する。
    /// </summary>
    public bool IsAnyDirty => IsDirty || CopyProperties.IsDirty;

    public PlacementInspectorViewModel(
        UpdatePlacementOffsetUseCase offsetUseCase,
        ForkPlacementVariantUseCase forkUseCase,
        IGridPlacementRepository placementRepository,
        IImageCopyRepository copyRepository,
        IImageAssetRepository assetRepository,
        IThumbnailService thumbnailService,
        IImageStorage imageStorage,
        CopyPropertiesViewModel copyProperties,
        IUndoRedoService history,
        IMessenger messenger,
        ILogger<PlacementInspectorViewModel> logger)
    {
        _offsetUseCase = offsetUseCase;
        _forkUseCase = forkUseCase;
        _placementRepository = placementRepository;
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _thumbnailService = thumbnailService;
        _imageStorage = imageStorage;
        CopyProperties = copyProperties;
        _history = history;
        _messenger = messenger;
        _logger = logger;
        PropertyChanged += OnAnyPropertyChanged;
        // 共有特性側の IsDirty 変化を統一バーの CanExecute / 未保存マーカーへ転送
        CopyProperties.PropertyChanged += OnCopyPropertiesPropertyChanged;
    }

    private void OnCopyPropertiesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CopyPropertiesViewModel.IsDirty)) return;
        OnPropertyChanged(nameof(IsAnyDirty));
        SaveAllCommand.NotifyCanExecuteChanged();
        RevertAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>編集対象の placement を差し替える。null で無効状態。
    /// <paramref name="grid"/> を渡すと描画域サイズ（<see cref="ImageDrawSizeLabel"/>）
    /// を計算する（null なら空ラベル）。新しい source の <c>PropertyChanged</c> を購読し、
    /// 外部（Shift+ドラッグ等）から <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> が変更されたら Inspector の表示にも追従させる。
    /// <para>
    /// Stage 3 以降: 当該 placement のバリアント (<see cref="ImageCopy"/>) を
    /// <see cref="CopyItemViewModel"/> に包んで <see cref="CopyPropertiesViewModel.Attach"/>
    /// に渡し、Inspector 内に inline embed した共有特性編集タブが当該バリアントを
    /// 直接編集できる状態にする。
    /// </para>
    /// </summary>
    public async Task AttachAsync(
        PlacementItemViewModel? source,
        GridCanvasItemViewModel? grid = null,
        CancellationToken ct = default)
    {
        if (_source is not null)
            _source.PropertyChanged -= OnSourcePropertyChanged;

        _source = source;
        _grid = grid;

        if (_source is not null)
            _source.PropertyChanged += OnSourcePropertyChanged;

        _suppressDirty = true;
        try
        {
            if (source is null)
            {
                HasPlacement = false;
                HeaderLabel = string.Empty;
                PositionLabel = string.Empty;
                ImageDrawSizeLabel = string.Empty;
                PixelOffsetX = 0;
                PixelOffsetY = 0;
            }
            else
            {
                HasPlacement = true;
                HeaderLabel = source.Label;
                PositionLabel = $"位置: ({source.GridX},{source.GridY}) / 占有: {source.OccupyWidth}×{source.OccupyHeight}";
                ImageDrawSizeLabel = ComputeImageDrawSizeLabel(source, grid);
                PixelOffsetX = source.PixelOffsetX;
                PixelOffsetY = source.PixelOffsetY;
            }
            IsDirty = false;
            StatusMessage = null;
        }
        finally
        {
            _suppressDirty = false;
        }

        // 共有特性編集を当該 placement のバリアントに同期する（Stage 3）。
        // CopyItemViewModel 構築には asset / thumb / source path / sizes が必要。
        if (source is null)
        {
            CopyProperties.Attach(null);
        }
        else
        {
            try
            {
                var copy = await _copyRepository.FindByIdAsync(source.CopyId, ct);
                var asset = copy is null
                    ? null
                    : await _assetRepository.FindByIdAsync(copy.AssetId, ct);
                if (copy is null || asset is null)
                {
                    CopyProperties.Attach(null);
                }
                else
                {
                    var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
                    var sourcePath = _imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
                    var item = new CopyItemViewModel(copy, thumb, sourcePath, asset.Size.Width, asset.Size.Height);
                    CopyProperties.Attach(item);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                CopyProperties.Attach(null);
            }
        }

        ForkVariantCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 選択中の配置がキャンバス上で占める描画域（セル矩形、PixelOffset=0、ScalingMode 不問）の
    /// ピクセル寸法ラベルを生成する。<paramref name="grid"/> が <c>null</c>、または計算に必要な
    /// 寸法情報が欠けている場合は空文字列を返す。
    /// </summary>
    private static string ComputeImageDrawSizeLabel(PlacementItemViewModel source, GridCanvasItemViewModel? grid)
    {
        if (grid is null) return string.Empty;
        if (grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0 || grid.Cols <= 0 || grid.Rows <= 0)
            return string.Empty;

        var canvas = new PixelSize(grid.CanvasWidth, grid.CanvasHeight);
        var rect = PlacementGeometry.ComputeDestRect(
            canvas, grid.Cols, grid.Rows,
            grid.ColWeights, grid.RowWeights,
            new CellPosition(source.GridX, source.GridY),
            new OccupySize(Math.Max(1, source.OccupyWidth), Math.Max(1, source.OccupyHeight)));
        return $"画像描画域: {rect.Width}×{rect.Height} px";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        // _source は外部要因で null 化される可能性があるためローカルキャプチャ。
        var source = _source;
        var grid = _grid;
        if (source is null || grid is null || !IsDirty) return;

        var clampedX = Math.Clamp(PixelOffsetX, -MaxPixelOffset, MaxPixelOffset);
        var clampedY = Math.Clamp(PixelOffsetY, -MaxPixelOffset, MaxPixelOffset);

        // before の値は DB の永続化済み値から取得（Shift+ドラッグで _source が書き換わっている可能性がある）
        var current = await _placementRepository.FindByIdAsync(source.PlacementId, ct);
        if (current is null)
        {
            StatusMessage = $"GridPlacement {source.PlacementId} が見つかりません。";
            return;
        }
        if (current.PixelOffsetX == clampedX && current.PixelOffsetY == clampedY)
        {
            // 値変化なし — 履歴に積まない
            _suppressDirty = true;
            try { IsDirty = false; StatusMessage = "保存しました（変更なし）。"; }
            finally { _suppressDirty = false; }
            return;
        }

        var description = $"ピクセル微調整: 「{source.Label}」 ΔX={clampedX}, ΔY={clampedY}";
        var command = new UpdatePlacementOffsetCommand(
            _offsetUseCase, grid.GridId, source.PlacementId,
            current.PixelOffsetX, current.PixelOffsetY, clampedX, clampedY, description);
        var execResult = await _history.ExecuteAsync(command, ct);
        if (execResult.IsError)
        {
            StatusMessage = string.Join(", ", execResult.Errors);
            return;
        }

        // VM 側の表示用 PlacementItemViewModel にも同期。
        // _suppressDirty スコープ内で代入することで、source の PropertyChanged が
        // OnSourcePropertyChanged → OnAnyPropertyChanged を経由して IsDirty を再度立てるのを防ぐ。
        _suppressDirty = true;
        try
        {
            source.PixelOffsetX = clampedX;
            source.PixelOffsetY = clampedY;
            IsDirty = false;
            StatusMessage = "保存しました。";
        }
        finally
        {
            _suppressDirty = false;
        }

        LogSaved(_logger, source.PlacementId);
    }

    /// <summary>
    /// 「リセット」ボタン。Inspector 数値編集 / Shift+ドラッグ / Ctrl+Arrow いずれの
    /// チャネルでも編集中バッファを破棄して<b>DB の永続化値</b>に戻す。
    /// <para>
    /// 単に <c>AttachAsync(_source)</c> で再ロードするだけだと <c>_source</c> 自体が
    /// Shift+ドラッグ等で書き換わっているため意味がない。DB から PlacementId で読み直して
    /// <c>source.PixelOffset</c> と Inspector Buffer の両方を永続化値に再セットする。
    /// View（Border 描画）も <c>source.PixelOffsetX/Y</c> の PropertyChanged で追従する。
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRevert))]
    public async Task RevertAsync(CancellationToken ct = default)
    {
        var source = _source;
        if (source is null) return;

        var current = await _placementRepository.FindByIdAsync(source.PlacementId, ct);
        if (current is null)
        {
            StatusMessage = $"GridPlacement {source.PlacementId} が見つかりません。";
            return;
        }

        _suppressDirty = true;
        try
        {
            // source を DB 値に戻す（View が PropertyChanged で追従する）
            source.PixelOffsetX = current.PixelOffsetX;
            source.PixelOffsetY = current.PixelOffsetY;
            // Inspector Buffer は OnSourcePropertyChanged 経由で同期されるが、
            // 既に同値だった場合に変更通知が出ないこともあるので明示的に上書き。
            PixelOffsetX = current.PixelOffsetX;
            PixelOffsetY = current.PixelOffsetY;
            IsDirty = false;
            StatusMessage = null;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private bool CanSave() => HasPlacement && IsDirty;
    private bool CanRevert() => HasPlacement && IsDirty;

    /// <summary>
    /// ΔX/ΔY を 0 に戻す。「0 にリセット」ボタン用。値を 0 に戻すだけの操作なので
    /// 常に有効（配置が選択されていない場合は <see cref="_source"/> が null で
    /// 反映されないだけで害はない）。
    /// </summary>
    [RelayCommand]
    public void ResetPixelOffset()
    {
        PixelOffsetX = 0;
        PixelOffsetY = 0;
    }

    /// <summary>
    /// 「この配置だけ別バリアントに分岐」コマンド。
    /// <para>
    /// 元の <see cref="ImageCopy"/> を複製した独立バリアントに当該 Placement の参照を付け替える。
    /// 同じ元バリアントを参照する他の配置には影響しない。fork 後、ユーザーは新バリアントの
    /// 名前 / 特性を編集して「○○ 用」「縦書き版」のように差別化できる。
    /// </para>
    /// <para>
    /// 履歴に積まれる単位は「fork 1 回 = 1 ステップ」。Undo すると Placement の参照は元バリアント
    /// に戻り、新バリアント自体は削除される（残骸が残らない）。
    /// </para>
    /// <para>
    /// fork 完了後は <see cref="CopyLibraryChangedMessage"/> を送出して候補リスト・配置リストの
    /// 自動再ロードを誘発する。VM 側でその後の選択保持は呼び出し側
    /// (<see cref="GridWorkspaceViewModel.ReloadFromMessageAsync"/>) に委ねる。
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanForkVariant))]
    public async Task ForkVariantAsync(CancellationToken ct = default)
    {
        var source = _source;
        var grid = _grid;
        if (source is null || grid is null) return;

        // 元バリアント名はラベル（"アセット名 / バリアント名" の形式）から取得しても良いが、
        // Description にはバリアント名のみ載せたいので Repository から確実に取得する。
        var sourceCopy = await _copyRepository.FindByIdAsync(source.CopyId, ct);
        if (sourceCopy is null)
        {
            StatusMessage = $"ImageCopy {source.CopyId} が見つかりません。";
            return;
        }
        var sourceLabel = string.IsNullOrWhiteSpace(sourceCopy.CopyName)
            ? Localization.Terminology.VariantUnnamed
            : sourceCopy.CopyName!;
        var description = $"バリアントを分岐: 「{sourceLabel}」 → 派生";

        var command = new ForkPlacementVariantCommand(
            _forkUseCase, _copyRepository, _placementRepository,
            source.PlacementId, grid.GridId, description);

        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return;
        }

        StatusMessage = "別バリアントに分岐しました。準備タブで名前と特性を編集できます。";
        // 候補リスト・配置の再ロードを誘発（CopyId 付け替え後の整合性を取る）。
        _messenger.Send(new CopyLibraryChangedMessage());
        LogForked(_logger, source.PlacementId, command.CreatedCopyId ?? Guid.Empty);
    }

    private bool CanForkVariant() => HasPlacement;

    /// <summary>
    /// Shift+ドラッグ・Ctrl+Arrow からの <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> 変更を Inspector の表示にも反映させ、IsDirty を立てる。
    /// <para>
    /// これらの UI チャネルは「Inspector 数値直接入力の直感的な代替」と位置づけ、
    /// 編集中バッファ（Inspector）→ IsDirty → 保存ボタン／Revert のフローに統一する。
    /// 連続操作（Shift+ドラッグ中・Ctrl+→ 連打）は保存ボタン押下時に 1 履歴エントリへまとまる。
    /// </para>
    /// <para>
    /// Save 後の <c>source.PixelOffset</c> 同期更新で本ハンドラが再入するが、Save 内で
    /// <see cref="_suppressDirty"/>=true でガードしているので IsDirty 連鎖は起きない。
    /// </para>
    /// </summary>
    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PlacementItemViewModel src || src != _source) return;
        if (e.PropertyName is nameof(PlacementItemViewModel.PixelOffsetX))
            PixelOffsetX = src.PixelOffsetX;
        else if (e.PropertyName is nameof(PlacementItemViewModel.PixelOffsetY))
            PixelOffsetY = src.PixelOffsetY;
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsDirty))
        {
            // IsDirty 自体の変化は IsAnyDirty / SaveAll/RevertAll の CanExecute に転送する
            OnPropertyChanged(nameof(IsAnyDirty));
            SaveAllCommand.NotifyCanExecuteChanged();
            RevertAllCommand.NotifyCanExecuteChanged();
            return;
        }

        if (_suppressDirty) return;
        if (e.PropertyName is nameof(HasPlacement)
            or nameof(StatusMessage) or nameof(HeaderLabel) or nameof(PositionLabel)
            or nameof(ImageDrawSizeLabel) or nameof(IsAnyDirty))
            return;

        if (!IsDirty) IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 配置固有 + 共有特性 を一括で保存する統一保存コマンド。配置タブ Inspector の
    /// 上部統一バーから発火。両方ダーティなら配置側 → 共有側の順に保存する
    /// （EF Core の DbContext は同時クエリを許容しないため必ず直列）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveAll))]
    public async Task SaveAllAsync(CancellationToken ct = default)
    {
        if (IsDirty) await SaveAsync(ct);
        if (CopyProperties.IsDirty) await CopyProperties.SaveAsync(ct);
    }

    /// <summary>配置固有 + 共有特性 を一括でリセットする。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAll))]
    public async Task RevertAllAsync(CancellationToken ct = default)
    {
        if (IsDirty) await RevertAsync(ct);
        if (CopyProperties.IsDirty) CopyProperties.Revert();
    }

    private bool CanSaveAll() => IsAnyDirty;

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "配置インスペクタからピクセル微調整を保存: {PlacementId}")]
    private static partial void LogSaved(ILogger logger, Guid placementId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information,
        Message = "配置のバリアントを分岐: placement={PlacementId} newCopy={NewCopyId}")]
    private static partial void LogForked(ILogger logger, Guid placementId, Guid newCopyId);
}

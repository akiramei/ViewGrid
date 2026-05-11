using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.AutoSave;
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
/// 配置タブで選択された <see cref="PlacementItemViewModel"/> の編集を司る。
/// <para>
/// <b>配置固有</b>: <see cref="PlacementItemViewModel.PixelOffsetX"/> /
/// <see cref="PlacementItemViewModel.PixelOffsetY"/> をこの VM 自身で扱う。
/// <b>共有特性</b>（Rotation/Flip/Scaling/Trim/Align/Occupy）は <see cref="ImageCopy"/> 単位で
/// 持つため、Stage 3 以降は <see cref="CopyProperties"/> サブ VM (CopyPropertiesViewModel) を
/// inline embed で同居させ、attach 時に当該 placement の variant を渡す。
/// </para>
/// <para>
/// 統一保存 (<see cref="SaveAllAsync"/>) で配置固有 + 共有特性を 1 ボタン操作で永続化する。
/// </para>
/// </summary>
public sealed partial class PlacementInspectorViewModel : ObservableObject, IDisposable
{
    /// <summary>auto-save の debounce 時間。 IsAnyDirty 立ち上がりからこの時間静止すれば 1 回だけ保存。</summary>
    internal static readonly TimeSpan AutoSaveDebounce = TimeSpan.FromMilliseconds(1000);

    private readonly UpdatePlacementOffsetUseCase _offsetUseCase;
    private readonly UpdatePlacementOccupySizeUseCase _occupyUseCase;
    private readonly ForkPlacementVariantUseCase _forkUseCase;
    private readonly IGridPlacementRepository _placementRepository;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IImageAssetRepository _assetRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IImageStorage _imageStorage;
    private readonly IUndoRedoService _history;
    private readonly IMessenger _messenger;
    private readonly IAppSettingsService _appSettings;
    private readonly ILogger<PlacementInspectorViewModel> _logger;
    private readonly SaveCoordinator _autoSave;
    private bool _disposed;

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
    /// 占有セル幅（W）の編集バッファ。配置固有特性として placement に持たせる。
    /// 同じバリアントを別グリッド・別セルに配置しても、それぞれで独立に変えられる。
    /// </summary>
    [ObservableProperty] public partial int OccupyWidth { get; set; } = 1;

    /// <summary>占有セル高さ（H）の編集バッファ。</summary>
    [ObservableProperty] public partial int OccupyHeight { get; set; } = 1;

    /// <summary>
    /// 配置固有 (<see cref="IsDirty"/>) と共有特性 (<see cref="CopyProperties"/>.IsDirty) の
    /// いずれかに未保存編集があるか。Inspector 上部の統一保存バーの CanExecute と
    /// 「●」未保存マーカー可視化に使う。<see cref="CopyProperties"/>.PropertyChanged を
    /// 購読してリアクティブに通知する。
    /// </summary>
    public bool IsAnyDirty => IsDirty || CopyProperties.IsDirty;

    public PlacementInspectorViewModel(
        UpdatePlacementOffsetUseCase offsetUseCase,
        UpdatePlacementOccupySizeUseCase occupyUseCase,
        ForkPlacementVariantUseCase forkUseCase,
        IGridPlacementRepository placementRepository,
        IImageCopyRepository copyRepository,
        IImageAssetRepository assetRepository,
        IThumbnailService thumbnailService,
        IImageStorage imageStorage,
        CopyPropertiesViewModel copyProperties,
        IUndoRedoService history,
        IMessenger messenger,
        IAppSettingsService appSettings,
        ILogger<PlacementInspectorViewModel> logger)
    {
        _offsetUseCase = offsetUseCase;
        _occupyUseCase = occupyUseCase;
        _forkUseCase = forkUseCase;
        _placementRepository = placementRepository;
        _copyRepository = copyRepository;
        _assetRepository = assetRepository;
        _thumbnailService = thumbnailService;
        _imageStorage = imageStorage;
        CopyProperties = copyProperties;
        _history = history;
        _messenger = messenger;
        _appSettings = appSettings;
        _logger = logger;
        _autoSave = new SaveCoordinator(
            AutoSaveDebounce,
            isEnabled: () => _appSettings.Current.EnableAutoSave,
            isDirty: () => IsAnyDirty,
            signatureProvider: ComputeAutoSaveSignature,
            saveAction: TrySaveAllAsync);
        PropertyChanged += OnAnyPropertyChanged;
        // 共有特性側の IsDirty 変化を統一バーの CanExecute / 未保存マーカーへ転送
        CopyProperties.PropertyChanged += OnCopyPropertiesPropertyChanged;
        _appSettings.Changed += OnAppSettingsChanged;

        // 新規 region 追加時の初期 OffsetXPx/Y を 「親側塗り位置と一致」 にするための closure。
        // _source / _grid は attach のたびに更新されるが、 Func は invocation 時に this 経由で
        // 最新値を読むので 1 回登録すれば足りる。 attach なし / 不足データなら null 返却で
        // CopyProperties 側が (0, 0) フォールバックする。
        CopyProperties.DefaultRegionOffsetResolver = ResolveDefaultRegionOffset;
    }

    /// <summary>
    /// 新規 region の初期 cell-local オフセットを 「親側塗り矩形の左上に重なる」 値として返す。
    /// 現在 attach 中の placement / grid 情報がない、 または source / canvas / 交差が退化のときは
    /// <c>null</c> を返して caller の (0, 0) フォールバックに任せる。
    /// </summary>
    private (int OffsetX, int OffsetY)? ResolveDefaultRegionOffset(RegionRectFraction regionRect)
    {
        var source = _source;
        var grid = _grid;
        if (source is null || grid is null) return null;
        if (grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0) return null;
        if (source.SourceWidth <= 0 || source.SourceHeight <= 0) return null;

        return Core.Geometry.RegionGeometry.ComputeOffsetMatchingParentFill(
            new PixelSize(grid.CanvasWidth, grid.CanvasHeight),
            grid.Cols, grid.Rows,
            grid.ColWeights, grid.RowWeights,
            source.Position, source.OccupySize,
            source.PixelOffsetX, source.PixelOffsetY,
            new ImageTransform(source.Rotation, source.FlipX, source.FlipY),
            source.ScalingMode, source.Alignment,
            source.EffectiveCropFraction,
            source.SourceWidth, source.SourceHeight,
            regionRect);
    }

    private void OnAppSettingsChanged(object? sender, Core.Settings.AppSettings settings)
    {
        // 設定 OFF にされた瞬間に保留中の auto-save をキャンセル。 既に進行中の保存は完遂させる。
        if (!settings.EnableAutoSave) _autoSave.Cancel();
    }

    /// <summary>
    /// 現在の編集バッファの signature。 placement/copy 識別子 + Inspector 4 値 + CopyProperties の主要 9 値を連結。
    /// <see cref="SaveCoordinator"/> が「同 signature の失敗 retry」 判定に使う。
    /// 識別子を含めることで、 別 placement/copy を選び直したあとに同じ値の編集をしても
    /// 「以前失敗した同じ編集」 とは別物として扱われ、 retry が走る。
    /// </summary>
    private string ComputeAutoSaveSignature()
    {
        // ToString は Globalization Invariant でないが、 比較用途で同 thread の文字列化なので問題なし。
        var placementId = _source?.PlacementId.ToString() ?? "null";
        var copyId = _source?.CopyId.ToString() ?? "null";
        return string.Concat(
            placementId, "@", copyId, "|",
            PixelOffsetX, "|", PixelOffsetY, "|",
            OccupyWidth, "|", OccupyHeight, "|",
            CopyProperties.Rotation, "|",
            CopyProperties.FlipX, "|", CopyProperties.FlipY, "|",
            CopyProperties.ScalingMode, "|",
            CopyProperties.AlignX, "|", CopyProperties.AlignY, "|",
            CopyProperties.AutoCropEnabled, "|",
            CopyProperties.AutoCropPreset, "|",
            CopyProperties.AutoCropThreshold, "|",
            CopyProperties.AutoCropCustomColorHex, "|",
            CopyProperties.ManualCropEnabled, "|",
            CopyProperties.ManualCropPixelX, "|", CopyProperties.ManualCropPixelY, "|",
            CopyProperties.ManualCropPixelWidth, "|", CopyProperties.ManualCropPixelHeight);
    }

    /// <summary>
    /// 保留中の auto-save を即実行 + その完了を待つ。 グリッド切替 / placement 切替 / アプリ終了時に呼ぶ。
    /// </summary>
    public Task FlushAutoSaveAsync(CancellationToken ct = default) => _autoSave.FlushAsync(ct);

    private void OnCopyPropertiesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CopyPropertiesViewModel.IsDirty))
        {
            OnPropertyChanged(nameof(IsAnyDirty));
            SaveAllCommand.NotifyCanExecuteChanged();
            RevertAllCommand.NotifyCanExecuteChanged();
        }

        // 連続編集で debounce が毎回 reset されるよう、 CopyProperties が dirty 状態なら
        // 各 PropertyChanged で notify を試みる。 Coordinator 側で設定 OFF / 同 signature 失敗
        // / dirty なし は内部 gate でスキップされる。
        if (CopyProperties.IsDirty)
            _autoSave.NotifyEdited();
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
        await FlushAndCommitOldSourceIfNeededAsync(source, ct);

        if (_source is not null)
            _source.PropertyChanged -= OnSourcePropertyChanged;

        // 別 placement に切り替わるなら、 旧 placement の auto-save 失敗 signature をクリアして
        // 新 placement での retry を許可する (signature 内に PlacementId が含まれるので新 placement の
        // 編集は別 signature にはなるが、 万一の余白として明示的にクリアしておく)。
        if (!ReferenceEquals(_source, source))
            _autoSave.ResetFailureGuard();

        _source = source;
        _grid = grid;

        if (_source is not null)
            _source.PropertyChanged += OnSourcePropertyChanged;

        InitializeEditingBufferFor(source, grid);
        await AttachCopyPropertiesForAsync(source, ct);

        ForkVariantCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 別 placement への切替前に、 保留中の auto-save を確実に flush し、 必要なら直接 commit する。
    /// 通常は呼び出し側 (GridWorkspaceViewModel.FlushThenAttachAsync) でも flush しているが、
    /// AttachAsync を直接呼ぶ経路 (テスト含む) でも旧 placement の編集が失われないよう、 ここで
    /// 防御層として再 flush する (Codex P1 指摘: 「pending save が flush される前に _source を書き換える」)。
    /// off→on transition (auto-save OFF 中に edit → ON 化 → 別 placement 選択) では dispatcher に
    /// タイマーが無く FlushAsync は no-op なので、 auto-save が現時点で ON のときに限り IsAnyDirty なら
    /// 直接 TrySaveAllAsync を呼ぶ (Codex P2 指摘、 GridCanvasList.FlushAndCommitOnSwitchAsync と同方針)。
    /// auto-save が OFF のままの場合は手動保存待ちの設計を尊重し、 直接 commit はしない。
    /// </summary>
    private async Task FlushAndCommitOldSourceIfNeededAsync(PlacementItemViewModel? newSource, CancellationToken ct)
    {
        if (_source is null || ReferenceEquals(_source, newSource)) return;

        try { await _autoSave.FlushAsync(ct); }
        catch { /* 失敗しても切替は続行 (StatusMessage に反映済み想定) */ }

        // auto-save 設定が現在 ON のときだけ off→on fallback で直接 commit する。 OFF のときは
        // 「ユーザーが Save ボタンを押すまで永続化しない」 manual-save 挙動を維持する。
        if (_appSettings.Current.EnableAutoSave && IsAnyDirty)
        {
            try { await TrySaveAllAsync(ct); }
            catch { /* 同上 */ }
        }
    }

    /// <summary>
    /// 編集バッファ (Header/Position/ImageDrawSize ラベル + PixelOffset/Occupy 数値) を新 source に
    /// 合わせて初期化する。 <c>_suppressDirty</c> スコープ内で代入することで、 設定中の値変化が
    /// IsDirty を立てる連鎖を防ぐ。
    /// </summary>
    private void InitializeEditingBufferFor(PlacementItemViewModel? source, GridCanvasItemViewModel? grid)
    {
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
                OccupyWidth = 1;
                OccupyHeight = 1;
            }
            else
            {
                HasPlacement = true;
                HeaderLabel = source.Label;
                PositionLabel = $"位置: ({source.GridX},{source.GridY}) / 占有: {source.OccupyWidth}×{source.OccupyHeight}";
                ImageDrawSizeLabel = ComputeImageDrawSizeLabel(source, grid);
                PixelOffsetX = source.PixelOffsetX;
                PixelOffsetY = source.PixelOffsetY;
                OccupyWidth = Math.Max(1, source.OccupyWidth);
                OccupyHeight = Math.Max(1, source.OccupyHeight);
            }
            IsDirty = false;
            StatusMessage = null;
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    /// <summary>
    /// 共有特性編集 (CopyProperties) を当該 placement のバリアントに同期する (Stage 3 以降の挙動)。
    /// 取得失敗 / 関連エンティティ不在のときは <c>CopyProperties.Attach(null)</c> で無効状態へ。
    /// <see cref="OperationCanceledException"/> のみ伝播 (上位 ct ハンドリング用)。
    /// </summary>
    private async Task AttachCopyPropertiesForAsync(PlacementItemViewModel? source, CancellationToken ct)
    {
        if (source is null)
        {
            CopyProperties.Attach(null);
            return;
        }

        try
        {
            var copy = await _copyRepository.FindByIdAsync(source.CopyId, ct);
            var asset = copy is null
                ? null
                : await _assetRepository.FindByIdAsync(copy.AssetId, ct);
            if (copy is null || asset is null)
            {
                CopyProperties.Attach(null);
                return;
            }

            var thumb = _thumbnailService.TryResolveAbsolutePath(asset.FileHash);
            var sourcePath = _imageStorage.ResolveAbsolutePath(asset.StoredRelativePath);
            var item = new CopyItemViewModel(copy, thumb, sourcePath, asset.Size.Width, asset.Size.Height);
            CopyProperties.Attach(item);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            CopyProperties.Attach(null);
        }
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

    /// <summary>
    /// 「保存」 ボタン用の RelayCommand エントリ。 戻り値は捨てて UI 互換性を維持する
    /// (CommunityToolkit.Mvvm の <c>AsyncRelayCommand</c> は Task 戻り想定)。
    /// auto-save 経路は <see cref="TrySaveAsync"/> を直接呼ぶ。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken ct = default)
        => _ = await TrySaveAsync(ct);

    /// <summary>
    /// 配置固有の保存実装。 成功 (no-op 含む) で <c>true</c>、 検証/IO 失敗で <c>false</c>。
    /// 失敗時は <see cref="IsDirty"/> を維持し <see cref="StatusMessage"/> にエラーを格納する
    /// (auto-save 経路はこの戻り値を見て同 signature の再 schedule をブロックする)。
    /// </summary>
    internal async Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        // _source は外部要因で null 化される可能性があるためローカルキャプチャ。
        var source = _source;
        var grid = _grid;
        if (source is null || grid is null || !IsDirty) return true;

        var clampedX = Math.Clamp(PixelOffsetX, -MaxPixelOffset, MaxPixelOffset);
        var clampedY = Math.Clamp(PixelOffsetY, -MaxPixelOffset, MaxPixelOffset);
        var newOccupy = new OccupySize(Math.Max(1, OccupyWidth), Math.Max(1, OccupyHeight));

        // before の値は DB の永続化済み値から取得（Shift+ドラッグで _source が書き換わっている可能性がある）
        var current = await _placementRepository.FindByIdAsync(source.PlacementId, ct);
        if (current is null)
        {
            StatusMessage = $"GridPlacement {source.PlacementId} が見つかりません。";
            return false;
        }

        return await CommitDirtyChangesAsync(source, grid, current, clampedX, clampedY, newOccupy, ct);
    }

    /// <summary>
    /// 値変化判定 → 永続化 (OccupySize → PixelOffset の順) → source 反映 までを担当する内部ヘルパ。
    /// 変化なしなら <c>true</c> + 「変更なし」 メッセージで早期 return。
    /// 永続化失敗は <see cref="StatusMessage"/> にエラーを格納済みで <c>false</c> を返す。
    /// OccupySize で失敗した場合は PixelOffset の変更も保留して IsDirty を維持する (旧挙動と同一)。
    /// </summary>
    private async Task<bool> CommitDirtyChangesAsync(
        PlacementItemViewModel source, GridCanvasItemViewModel grid, GridPlacement current,
        int clampedX, int clampedY, OccupySize newOccupy, CancellationToken ct)
    {
        var offsetChanged = current.PixelOffsetX != clampedX || current.PixelOffsetY != clampedY;
        var occupyChanged = current.OccupySize != newOccupy;

        if (!offsetChanged && !occupyChanged)
        {
            // 値変化なし — 履歴に積まない
            ResetDirtyWithMessage("保存しました（変更なし）。");
            return true;
        }

        if (occupyChanged && !await TryPersistOccupyAsync(source, grid, current.OccupySize, newOccupy, ct))
            return false;

        if (offsetChanged && !await TryPersistOffsetAsync(source, grid, current, clampedX, clampedY, ct))
            return false;

        ApplySavedValuesToSource(source, grid, offsetChanged, occupyChanged, clampedX, clampedY, newOccupy);
        LogSaved(_logger, source.PlacementId);
        return true;
    }

    /// <summary>
    /// 占有サイズを履歴経由で永続化する。 失敗時は <see cref="StatusMessage"/> にエラーを格納し <c>false</c> を返す。
    /// </summary>
    private async Task<bool> TryPersistOccupyAsync(
        PlacementItemViewModel source, GridCanvasItemViewModel grid,
        OccupySize before, OccupySize after, CancellationToken ct)
    {
        var occupyDesc = $"占有セル変更: 「{source.Label}」 {before.Width}×{before.Height} → {after.Width}×{after.Height}";
        var command = new UpdatePlacementOccupySizeCommand(
            _occupyUseCase, grid.GridId, source.PlacementId, before, after, occupyDesc);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }
        return true;
    }

    /// <summary>
    /// ピクセルオフセットを履歴経由で永続化する。 失敗時は <see cref="StatusMessage"/> にエラー格納 + <c>false</c>。
    /// </summary>
    private async Task<bool> TryPersistOffsetAsync(
        PlacementItemViewModel source, GridCanvasItemViewModel grid,
        GridPlacement current, int clampedX, int clampedY, CancellationToken ct)
    {
        var description = $"ピクセル微調整: 「{source.Label}」 ΔX={clampedX}, ΔY={clampedY}";
        var command = new UpdatePlacementOffsetCommand(
            _offsetUseCase, grid.GridId, source.PlacementId,
            current.PixelOffsetX, current.PixelOffsetY, clampedX, clampedY, description);
        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 保存成功後、 表示用 <see cref="PlacementItemViewModel"/> と Inspector ラベルを反映する。
    /// <c>_suppressDirty</c> スコープ内で更新することで、 source.PropertyChanged → OnSourcePropertyChanged
    /// → OnAnyPropertyChanged 経由で IsDirty が再び立つのを防ぐ。
    /// </summary>
    private void ApplySavedValuesToSource(
        PlacementItemViewModel source, GridCanvasItemViewModel grid,
        bool offsetChanged, bool occupyChanged,
        int clampedX, int clampedY, OccupySize newOccupy)
    {
        _suppressDirty = true;
        try
        {
            if (offsetChanged)
            {
                source.PixelOffsetX = clampedX;
                source.PixelOffsetY = clampedY;
            }
            if (occupyChanged)
            {
                source.OccupySize = newOccupy;
                // PositionLabel は占有も含むので再計算
                PositionLabel = $"位置: ({source.GridX},{source.GridY}) / 占有: {source.OccupyWidth}×{source.OccupyHeight}";
                ImageDrawSizeLabel = ComputeImageDrawSizeLabel(source, grid);
            }
            IsDirty = false;
            StatusMessage = "保存しました。";
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void ResetDirtyWithMessage(string message)
    {
        _suppressDirty = true;
        try { IsDirty = false; StatusMessage = message; }
        finally { _suppressDirty = false; }
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
            source.OccupySize = current.OccupySize;
            // Inspector Buffer は OnSourcePropertyChanged 経由で同期されるが、
            // 既に同値だった場合に変更通知が出ないこともあるので明示的に上書き。
            PixelOffsetX = current.PixelOffsetX;
            PixelOffsetY = current.PixelOffsetY;
            OccupyWidth = current.OccupySize.Width;
            OccupyHeight = current.OccupySize.Height;
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
        else if (e.PropertyName is nameof(PlacementItemViewModel.OccupySize))
        {
            OccupyWidth = Math.Max(1, src.OccupyWidth);
            OccupyHeight = Math.Max(1, src.OccupyHeight);
        }
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsDirty))
        {
            // IsDirty 自体の変化は IsAnyDirty / SaveAll/RevertAll の CanExecute に転送する。
            // auto-save の Schedule は編集系プロパティ変更経路 (下) で行うので、 ここでは呼ばない
            // (二重 Schedule を避ける + 連続編集で IsDirty が立ったままでも下で毎回 Schedule される)。
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
        // 連続編集で debounce が毎回 reset されるよう、 編集系プロパティ変更ごとに Coordinator に
        // 通知する (Coordinator 側で設定 OFF / 同 signature 失敗 / dirty なし は内部 gate でスキップ)。
        _autoSave.NotifyEdited();
    }

    /// <summary>
    /// 配置固有 + 共有特性 を一括で保存する統一保存コマンド。配置タブ Inspector の
    /// 上部統一バーから発火。両方ダーティなら配置側 → 共有側の順に保存する
    /// （EF Core の DbContext は同時クエリを許容しないため必ず直列）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveAll))]
    public async Task SaveAllAsync(CancellationToken ct = default)
        => _ = await TrySaveAllAsync(ct);

    /// <summary>
    /// 統一保存の bool 戻り値版。 配置固有・共有特性の両方が成功で <c>true</c>、 いずれか失敗で <c>false</c>。
    /// auto-save 経路はこちらを直接呼ぶ。
    /// </summary>
    internal async Task<bool> TrySaveAllAsync(CancellationToken ct = default)
    {
        var inspectorOk = !IsDirty || await TrySaveAsync(ct);
        var copyOk = !CopyProperties.IsDirty || await CopyProperties.TrySaveAsync(ct);
        return inspectorOk && copyOk;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _appSettings.Changed -= OnAppSettingsChanged;
        CopyProperties.PropertyChanged -= OnCopyPropertiesPropertyChanged;
        PropertyChanged -= OnAnyPropertyChanged;
        if (_source is not null)
            _source.PropertyChanged -= OnSourcePropertyChanged;
        _autoSave.Dispose();
        CopyProperties.Dispose();
    }
}

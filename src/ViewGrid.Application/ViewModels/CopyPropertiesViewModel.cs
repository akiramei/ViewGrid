using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 選択された論理コピーの特性を編集する。Attach で値を読み込み、
/// 変更があれば IsDirty を立て、Save で永続化する。
/// </summary>
public sealed partial class CopyPropertiesViewModel : ViewModelBase, IDisposable
{
    private readonly UpdateImageCopyUseCase _updateUseCase;
    private readonly IUndoRedoService _history;
    private readonly IMessenger _messenger;
    private readonly IImageColorPicker _colorPicker;
    private readonly IAutoCropBboxResolver _autoCropResolver;
    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _loc;
    private readonly ILogger<CopyPropertiesViewModel> _logger;

    private CopyItemViewModel? _source;
    private bool _suppressDirty;

    /// <summary>テスト専用: 現在 attach されている source を覗くためのアクセサ。
    /// プロダクションコードからは使わない（<see cref="HasCopy"/> や個別プロパティで判定する）。</summary>
    internal CopyItemViewModel? AttachedSourceForTests => _source;

    /// <summary>
    /// AutoCrop プレビュー計算の進行中タスクをキャンセルするための CTS。
    /// 閾値スライダーや HEX 入力で連続変更されるたびに古い計算を打ち切り、
    /// 最新の入力結果だけが反映されるようにする。
    /// </summary>
    private CancellationTokenSource? _autoCropPreviewCts;

    [ObservableProperty]
    public partial bool HasCopy { get; set; }

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    /// <summary>
    /// 複数選択中の案内文。<c>null</c> 以外なら View 上に表示し、編集 UI は disabled になる。
    /// 外部（<see cref="MainWindowViewModel"/>）が選択件数に応じて設定する。
    /// </summary>
    [ObservableProperty]
    public partial string? MultiSelectMessage { get; set; }

    // 編集バッファ
    // CopyName はリスト上のインラインリネーム + 新規作成フライアウトに移管したため、特性タブの編集対象から除外。
    // 履歴 Description 用に元の名前を参照する必要があるときは _source.CopyName を直接読む。
    [ObservableProperty] public partial Rotation Rotation { get; set; }
    [ObservableProperty] public partial bool FlipX { get; set; }
    [ObservableProperty] public partial bool FlipY { get; set; }
    [ObservableProperty] public partial ScalingMode ScalingMode { get; set; } = ScalingMode.UniformContain;
    [ObservableProperty] public partial AnchorX AlignX { get; set; } = AnchorX.Center;
    [ObservableProperty] public partial AnchorY AlignY { get; set; } = AnchorY.Center;
    // OccupySize はバリアント単位の共有特性ではなく、配置 (GridPlacement) 単位の固有特性に
    // 移管された。本タブでは編集 UI を持たない（PlacementInspector の「配置固有」セクションで
    // 編集する）。新規配置時は ImageCopy のデフォルト OccupySize がそのまま継承される。

    /// <summary>単色余白の自動トリミング機能の ON/OFF。OFF なら <see cref="AutoCropPreset"/> /
    /// <see cref="AutoCropThreshold"/> は無視され、保存時に AutoCrop=null となる。</summary>
    [ObservableProperty] public partial bool AutoCropEnabled { get; set; }

    /// <summary>対象色プリセット（白/黒/透明/カスタム）。<see cref="AutoCropPreset.Custom"/> 選択時は
    /// <see cref="AutoCropCustomColorHex"/> の値（または画像クリックピッカーで採取した色）を使う。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoCropCustom))]
    public partial AutoCropPreset AutoCropPreset { get; set; } = AutoCropPreset.White;

    /// <summary>許容色差（Chebyshev、0–128）。0 で完全一致のみ余白扱い（既定）。
    /// nullable 化の理由は <see cref="OccupyWidth"/> と同じ（null 状態を許容して
    /// バインディング例外を回避し、保存・換算時に 0 へ coerce）。</summary>
    [ObservableProperty] public partial int? AutoCropThreshold { get; set; }

    /// <summary>カスタム対象色の HEX 表記（<c>#RRGGBB</c>）。<see cref="AutoCropPreset.Custom"/>
    /// 選択時のみ <c>BuildAutoCropFromInputs</c> で参照される。
    /// 画像クリックピッカーで色を採取するとここに反映される。</summary>
    [ObservableProperty] public partial string AutoCropCustomColorHex { get; set; } = "#FFFFFF";

    /// <summary>任意矩形トリミング機能の ON/OFF。OFF なら永続化時に ManualCrop=null（OFF）となる。
    /// AutoCrop と排他で、こちらを ON にすると AutoCropEnabled が自動的に OFF になる。</summary>
    [ObservableProperty] public partial bool ManualCropEnabled { get; set; }

    // ManualCropPixel* は int? で保持。 編集 UI 内の真実をピクセル整数に揃え、
    // 永続化形式 (ManualCropFraction) との境界だけで分数 / 整数を変換する責務分離。
    // 一時的な null（入力中の空白）はバインディング層で受けて、IsManualCropDefined や Save 時に
    // 0 として扱う（換算は既に 0 で「未確定」として扱う仕様）。

    /// <summary>矩形左上 X（元画像ピクセル整数）。永続化時に SourceWidth で割って 0–1 比率に換算。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial int? ManualCropPixelX { get; set; }

    /// <summary>矩形左上 Y（元画像ピクセル整数）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial int? ManualCropPixelY { get; set; }

    /// <summary>矩形幅（元画像ピクセル整数）。0 のときは「未確定」状態（手動ラジオを選んだ直後など）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial int? ManualCropPixelWidth { get; set; }

    /// <summary>矩形高さ（元画像ピクセル整数）。0 のときは未確定。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial int? ManualCropPixelHeight { get; set; }

    // ─── ProtectedRegion (保護領域) タブ ────────────────────────────
    // 0 件以上の region を ListBox で一覧表示し、 Editor (Task 9) で 8 ハンドル編集する。
    // Phase 1 では FillMode.White 一択で UI には出さない。 並べ替えも Phase 2 で検討。

    /// <summary>「保護領域」 タブ ListBox にバインドする region 一覧。 順序 = SortOrder。</summary>
    public ObservableCollection<ProtectedRegionItemViewModel> RegionItems { get; } = new();

    /// <summary>ListBox の選択行。 編集 / 削除コマンドの対象。</summary>
    [ObservableProperty]
    public partial ProtectedRegionItemViewModel? SelectedRegion { get; set; }

    /// <summary>RegionItems が空のとき true。 「未登録です」 メッセージの表示用。</summary>
    public bool HasNoRegions => RegionItems.Count == 0;

    /// <summary>「塗りモード」 ComboBox の選択肢 (5 値)。 ItemTemplate で日本語ラベル化。</summary>
    public IReadOnlyList<ProtectedRegionFillMode> RegionFillModeOptions { get; } = new[]
    {
        ProtectedRegionFillMode.White,
        ProtectedRegionFillMode.Black,
        ProtectedRegionFillMode.Transparent,
        ProtectedRegionFillMode.None,
        ProtectedRegionFillMode.Custom,
    };

    /// <summary>
    /// 新規 region 追加時に「親側塗りと同じ位置に asset を初期配置する」 ための offset 計算 callback。
    /// <see cref="PlacementInspectorViewModel"/> が attach 時にセットし、 placement / grid 情報を
    /// 使って <see cref="RegionGeometry.ComputeOffsetMatchingParentFill"/> を呼び出す。 callback が
    /// <c>null</c> または計算不可 (戻り値 null) のときは (0, 0) フォールバックする。
    /// </summary>
    /// <remarks>
    /// CopyPropertiesViewModel 自体は placement / grid を知らないため、 Inspector が外から注入する。
    /// テストではセットしないことで従来の (0, 0) 既定挙動を維持できる。
    /// </remarks>
    public Func<RegionRectFraction, (int OffsetX, int OffsetY)?>? DefaultRegionOffsetResolver { get; set; }

    /// <summary>
    /// 選択中の region 用のサムネ画像クリックピッカー。 サムネ表示 (Stretch.Uniform) 上の
    /// クリック座標を原画像座標に換算し、 採取した ARGB を <see cref="ProtectedRegionItemViewModel.FillColor"/>
    /// に書き戻す。 同時に <see cref="ProtectedRegionItemViewModel.FillMode"/> を Custom に切り替える。
    /// AutoCrop 側の <see cref="PickColorFromThumbnailAsync"/> と同じパターン。
    /// </summary>
    public async Task PickColorForSelectedRegionAsync(
        int thumbX, int thumbY, int thumbWidth, int thumbHeight, CancellationToken ct = default)
    {
        if (SelectedRegion is not { } region) return;
        var path = SourceImagePath;
        if (string.IsNullOrEmpty(path)) return;
        if (SourceWidth <= 0 || SourceHeight <= 0) return;
        if (thumbWidth <= 0 || thumbHeight <= 0) return;

        var srcX = (int)Math.Clamp((double)thumbX / thumbWidth * SourceWidth, 0, SourceWidth - 1);
        var srcY = (int)Math.Clamp((double)thumbY / thumbHeight * SourceHeight, 0, SourceHeight - 1);

        var argb = await _colorPicker.PickColorAsync(path, srcX, srcY, ct);
        if (argb is not { } color) return;

        // 採取した色を FillColor に格納し、 FillMode を Custom に揃える
        // (採取直後にプリセットへ戻された場合の挙動はユーザー操作に委ねる)。
        region.FillColor = color;
        region.FillMode = ProtectedRegionFillMode.Custom;
    }

    /// <summary>矩形が確定しているか（W&gt;0 かつ H&gt;0）。「手動」ラジオ ON 直後でドラッグ前は false。
    /// 数値入力フィールドや矩形ハンドルの IsEnabled、Save 時の永続化判定に使う。</summary>
    public bool IsManualCropDefined =>
        (ManualCropPixelWidth ?? 0) > 0 && (ManualCropPixelHeight ?? 0) > 0;

    /// <summary>
    /// 「OFF / 自動 / 手動」ラジオの「OFF」用バインド。両方 OFF なら true。
    /// setter で true をセットされると AutoCrop / ManualCrop を両方 OFF にする
    /// （RadioButton の IsChecked にバインドして OFF ラジオをユーザーが選んだ時の挙動）。
    /// </summary>
    public bool IsCropOff
    {
        get => !AutoCropEnabled && !ManualCropEnabled;
        set
        {
            if (value)
            {
                AutoCropEnabled = false;
                ManualCropEnabled = false;
            }
            OnPropertyChanged();
        }
    }

    /// <summary>サムネイルの絶対パス（表示用）。Attach 時に <see cref="CopyItemViewModel.ThumbnailPath"/>
    /// からセットされる。AutoCrop の画像クリックピッカーでクリック対象として表示するが、色は
    /// <see cref="SourceImagePath"/> から採取する（サムネ WebP 圧縮で色が変化するため）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial string? ThumbnailPath { get; set; }

    /// <summary>原画像（圧縮なし）の絶対パス。色採取はここから行うことで AutoCrop 走査と一致する。</summary>
    [ObservableProperty]
    public partial string? SourceImagePath { get; set; }

    /// <summary>原画像のピクセル幅。サムネクリック座標 → 原画像座標への換算に使う。</summary>
    [ObservableProperty]
    public partial int SourceWidth { get; set; }

    /// <summary>原画像のピクセル高さ。同上。</summary>
    [ObservableProperty]
    public partial int SourceHeight { get; set; }

    /// <summary>カスタム HEX / 画像ピッカーを表示するかの派生プロパティ。</summary>
    public bool IsAutoCropCustom => AutoCropPreset == AutoCropPreset.Custom;

    /// <summary>
    /// AutoCrop の走査結果（0–1 比率）。プレビュー overlay の bbox 計算に使う。
    /// <c>null</c> のときは「クロップ範囲なし（全画素対象色 or 対象色不在）」または
    /// プレビュー無効状態。<see cref="HasAutoCropPreview"/> も合わせて見る。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoCropPreview))]
    public partial AutoCropFraction? AutoCropPreviewFraction { get; set; }

    /// <summary>
    /// プレビュー走査結果のユーザー向け説明文。
    /// 「対象色によるクロップ範囲なし（全領域）」など、bbox=null のときに状況を伝える。
    /// </summary>
    [ObservableProperty]
    public partial string? AutoCropPreviewMessage { get; set; }

    /// <summary>プレビュー overlay を描画すべきか。<see cref="AutoCropPreviewFraction"/> が非 null なら true。</summary>
    public bool HasAutoCropPreview => AutoCropPreviewFraction is not null;

    /// <summary>サムネが利用可能か（画像クリックピッカーの有効性）。</summary>
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath);

    /// <summary>
    /// <see cref="AlignX"/> / <see cref="AlignY"/> が renderer に効くか。
    /// <see cref="ScalingMode.Fill"/> 以外では常に効く（画像 ≤ セルなら配置位置、
    /// 画像 &gt; セルなら表示部分の選択を、同じ Alignment アンカーで決める）。
    /// 旧版は TrimmingAnchor を別個に持っていたが、CSS background-position 等の
    /// 業界標準に倣い 1 アンカーに統合した。
    /// </summary>
    public bool IsAlignmentActive => ScalingMode != ScalingMode.Fill;

    partial void OnScalingModeChanged(ScalingMode value)
    {
        OnPropertyChanged(nameof(IsAlignmentActive));
    }

    /// <summary>排他連動: AutoCrop ON にすると ManualCrop は OFF。
    /// ラジオ 3 択（OFF/自動/手動）の意味論を VM 内で保証。</summary>
    partial void OnAutoCropEnabledChanged(bool value)
    {
        if (value && ManualCropEnabled)
        {
            ManualCropEnabled = false;
        }
        OnPropertyChanged(nameof(IsCropOff));
        TriggerAutoCropPreviewUpdate();
    }

    /// <summary>プリセット変更時にプレビュー再計算 + Custom 切替時の派生プロパティ通知。</summary>
    partial void OnAutoCropPresetChanged(AutoCropPreset value) => TriggerAutoCropPreviewUpdate();

    /// <summary>閾値変更時にプレビュー再計算（スライダー連続変更でもキャンセル合流で最新だけ反映）。</summary>
    partial void OnAutoCropThresholdChanged(int? value) => TriggerAutoCropPreviewUpdate();

    /// <summary>カスタム HEX 変更時にプレビュー再計算（採取 / 手入力 両方）。</summary>
    partial void OnAutoCropCustomColorHexChanged(string value) => TriggerAutoCropPreviewUpdate();

    /// <summary>排他連動: ManualCrop ON にすると AutoCrop は OFF。</summary>
    partial void OnManualCropEnabledChanged(bool value)
    {
        if (value && AutoCropEnabled)
        {
            AutoCropEnabled = false;
        }
        OnPropertyChanged(nameof(IsCropOff));
    }

    // XAML バインディング用の選択肢
    public IReadOnlyList<Rotation> RotationOptions { get; } =
        [Rotation.None, Rotation.Cw90, Rotation.Cw180, Rotation.Cw270];

    public IReadOnlyList<AnchorX> AnchorXOptions { get; } =
        [AnchorX.Left, AnchorX.Center, AnchorX.Right];

    public IReadOnlyList<AnchorY> AnchorYOptions { get; } =
        [AnchorY.Top, AnchorY.Center, AnchorY.Bottom];

    public IReadOnlyList<ScalingMode> ScalingModeOptions { get; } =
    [
        ScalingMode.None,
        ScalingMode.UniformContain,
        ScalingMode.UniformContainShrinkOnly,
        ScalingMode.UniformContainEnlargeOnly,
        ScalingMode.UniformCover,
        ScalingMode.Fill,
    ];

    public IReadOnlyList<AutoCropPreset> AutoCropPresetOptions { get; } =
    [
        AutoCropPreset.White,
        AutoCropPreset.Black,
        AutoCropPreset.Transparent,
        AutoCropPreset.Custom,
    ];

    public CopyPropertiesViewModel(
        UpdateImageCopyUseCase updateUseCase,
        IUndoRedoService history,
        IMessenger messenger,
        IImageColorPicker colorPicker,
        IAutoCropBboxResolver autoCropResolver,
        IAppSettingsService settings,
        ILocalizationService loc,
        ILogger<CopyPropertiesViewModel> logger)
    {
        _updateUseCase = updateUseCase;
        _history = history;
        _messenger = messenger;
        _colorPicker = colorPicker;
        _autoCropResolver = autoCropResolver;
        _settings = settings;
        _loc = loc;
        _logger = logger;
        PropertyChanged += OnAnyPropertyChanged;
        RegionItems.CollectionChanged += OnRegionItemsCollectionChanged;
    }

    /// <summary>編集対象を差し替える。null で無効状態。</summary>
    public void Attach(CopyItemViewModel? source)
    {
        _source = source;
        _suppressDirty = true;
        try
        {
            if (source is null)
            {
                HasCopy = false;
                Rotation = Rotation.None;
                FlipX = false;
                FlipY = false;
                ScalingMode = ScalingMode.UniformContain;
                AlignX = AnchorX.Center;
                AlignY = AnchorY.Center;
                AutoCropEnabled = false;
                AutoCropPreset = _settings.Current.DefaultAutoCropPreset;
                AutoCropThreshold = 0;
                AutoCropCustomColorHex = "#FFFFFF";
                ManualCropEnabled = false;
                ManualCropPixelX = 0;
                ManualCropPixelY = 0;
                ManualCropPixelWidth = 0;
                ManualCropPixelHeight = 0;
                ThumbnailPath = null;
                SourceImagePath = null;
                SourceWidth = 0;
                SourceHeight = 0;
                ClearRegionItems();
            }
            else
            {
                HasCopy = true;
                Rotation = source.Rotation;
                FlipX = source.FlipX;
                FlipY = source.FlipY;
                ScalingMode = source.ScalingMode;
                AlignX = source.Alignment.X;
                AlignY = source.Alignment.Y;
                ThumbnailPath = source.ThumbnailPath;
                SourceImagePath = source.SourceImagePath;
                SourceWidth = source.SourceWidth;
                SourceHeight = source.SourceHeight;
                if (source.AutoCrop is { } ac)
                {
                    AutoCropEnabled = true;
                    AutoCropPreset = MapToPreset(ac);
                    AutoCropThreshold = ac.Threshold;
                    // Custom プリセットの場合は HEX 表示も復元
                    AutoCropCustomColorHex = AutoCropPreset == AutoCropPreset.Custom
                        ? FormatHex(ac.TargetColorArgb)
                        : "#FFFFFF";
                }
                else
                {
                    AutoCropEnabled = false;
                    AutoCropPreset = _settings.Current.DefaultAutoCropPreset;
                    AutoCropThreshold = 0;
                    AutoCropCustomColorHex = "#FFFFFF";
                }
                if (source.ManualCrop is { } mc && source.SourceWidth > 0 && source.SourceHeight > 0)
                {
                    ManualCropEnabled = true;
                    // fraction → 整数ピクセルへ Math.Round で丸めて round-trip 安定化
                    ManualCropPixelX = (int)Math.Round(mc.X * source.SourceWidth);
                    ManualCropPixelY = (int)Math.Round(mc.Y * source.SourceHeight);
                    ManualCropPixelWidth = (int)Math.Round(mc.Width * source.SourceWidth);
                    ManualCropPixelHeight = (int)Math.Round(mc.Height * source.SourceHeight);
                }
                else
                {
                    ManualCropEnabled = false;
                    ManualCropPixelX = 0;
                    ManualCropPixelY = 0;
                    ManualCropPixelWidth = 0;
                    ManualCropPixelHeight = 0;
                }
                LoadRegionItemsFromSource(source);
            }
            IsDirty = false;
            StatusMessage = null;
            // Attach 内では _suppressDirty=true により partial method 経由のプレビュー再計算が
            // 抑止されている。終了後に 1 回だけ呼び出して、AutoCropEnabled / 設定の最終状態に
            // 基づいてプレビューを更新する（無効化のときは null へリセットされる）。
            AutoCropPreviewFraction = null;
            AutoCropPreviewMessage = null;
        }
        finally
        {
            _suppressDirty = false;
        }
        TriggerAutoCropPreviewUpdate();
    }

    /// <summary>
    /// 「保存」 ボタン用の RelayCommand エントリ。 auto-save 経路は <see cref="TrySaveAsync"/> を直接呼ぶ。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken ct = default)
        => _ = await TrySaveAsync(ct);

    /// <summary>
    /// 共有特性の保存実装。 成功 (no-op 含む) で <c>true</c>、 検証/IO 失敗で <c>false</c>。
    /// 失敗時は <see cref="IsDirty"/> を維持し <see cref="StatusMessage"/> にエラーを格納する。
    /// </summary>
    internal async Task<bool> TrySaveAsync(CancellationToken ct = default)
    {
        var source = _source;
        if (source is null || !IsDirty)
            return true;

        var before = BuildBeforeSnapshot(source);
        var after = BuildAfterSnapshot();

        // Description は Save 時点での名前を表示用に使う（リネーム結果の追跡は UpdateImageCopyCommand
        // のリネーム経路が別に表示するため、こちらは固定の「特性編集: 「{name}」」だけで良い）。
        var nameLabel = string.IsNullOrWhiteSpace(source.CopyName) ? "(無名)" : source.CopyName!;
        var description = $"特性編集: 「{nameLabel}」";
        var command = new UpdateImageCopyCommand(_updateUseCase, source.CopyId, before, after, description);
        var execResult = await _history.ExecuteAsync(command, ct);
        if (execResult.IsError)
        {
            // ErrorOr.Error の自動 ToString は record dump 形式（"Error { Code=..., Description=..., ... }"）で
            // ユーザーには冗長すぎるため Description のみを連結する。検証エラーがそのまま画面に出る経路。
            StatusMessage = string.Join(", ", execResult.Errors.Select(e => e.Description));
            return false;
        }

        ApplyAfterToSource(source, after);
        ResetDirtyAndShowSavedMessage();

        _messenger.Send(new CopyLibraryChangedMessage());
        LogSaved(_logger, source.CopyId);
        return true;
    }

    /// <summary>
    /// 保存前の永続化済み値スナップショット。 <c>CopyName</c> は本タブの編集対象外なので
    /// <c>null</c> + <c>ClearCopyName=false</c> で「変更しない」 を明示する (Use Case 側で current 値を保持)。
    /// AutoCrop / ManualCrop は null のとき Clear*=true で「OFF へ戻す」 を明示。
    /// </summary>
    private static UpdateImageCopyChanges BuildBeforeSnapshot(CopyItemViewModel source) => new()
    {
        Transform = new ImageTransform(source.Rotation, source.FlipX, source.FlipY),
        ScalingMode = source.ScalingMode,
        Alignment = source.Alignment,
        // OccupySize は本タブの編集対象外（配置固有に移管）。null = 変更しない。
        AutoCrop = source.AutoCrop,
        ClearAutoCrop = source.AutoCrop is null,
        ManualCrop = source.ManualCrop,
        ClearManualCrop = source.ManualCrop is null,
        // Regions は配列を丸ごと保持 (空配列も Empty として明示)。 これで Undo が
        // 「Region なし状態」 にも正しく復元できる (UpdateImageCopyChanges.Regions の
        // 仕様: null=変更しない / Empty=明示空 / 非空=置換)。
        Regions = source.Regions,
    };

    /// <summary>
    /// 編集バッファから保存後の値スナップショットを組み立てる。 <c>CopyName</c> は触らない
    /// (インラインリネームとは独立)。
    /// </summary>
    private UpdateImageCopyChanges BuildAfterSnapshot()
    {
        var afterAutoCrop = AutoCropEnabled ? BuildAutoCropFromInputs() : (AutoCropSettings?)null;
        var afterManualCrop = BuildAfterManualCrop();
        return new UpdateImageCopyChanges
        {
            Transform = new ImageTransform(Rotation, FlipX, FlipY),
            ScalingMode = ScalingMode,
            Alignment = new Alignment(AlignX, AlignY),
            // OccupySize は本タブで触らない（配置固有）。null で「変更しない」を明示。
            AutoCrop = afterAutoCrop,
            ClearAutoCrop = afterAutoCrop is null,
            ManualCrop = afterManualCrop,
            ClearManualCrop = afterManualCrop is null,
            Regions = BuildAfterRegions(),
        };
    }

    /// <summary>
    /// <see cref="RegionItems"/> の現状から永続化用 <see cref="ProtectedRegion"/> 配列を再構築する。
    /// SortOrder = 配列 index、 ImageCopyId = source.CopyId を割り当てる (新規追加分の Id は VM 側で
    /// 既に Guid.NewGuid() されているのでここでは引き継ぐだけ)。
    /// </summary>
    private ImmutableArray<ProtectedRegion> BuildAfterRegions()
    {
        var source = _source;
        if (source is null) return ImmutableArray<ProtectedRegion>.Empty;
        if (RegionItems.Count == 0) return ImmutableArray<ProtectedRegion>.Empty;

        var builder = ImmutableArray.CreateBuilder<ProtectedRegion>(RegionItems.Count);
        for (var i = 0; i < RegionItems.Count; i++)
        {
            var item = RegionItems[i];
            builder.Add(new ProtectedRegion
            {
                Id = item.Id,
                ImageCopyId = source.CopyId,
                Rect = item.Rect,
                FillMode = item.FillMode,
                FillColor = item.FillMode == ProtectedRegionFillMode.Custom ? item.FillColor : null,
                OffsetXPx = item.OffsetXPx,
                OffsetYPx = item.OffsetYPx,
                Rotation = item.Rotation,
                FlipX = item.FlipX,
                FlipY = item.FlipY,
                SortOrder = i,
            });
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// ManualCrop は「ラジオで手動を選んでいて、 かつ矩形が確定している」 ときのみ永続化。
    /// 「手動」選択 + 未確定 (W=0 or H=0) は実質 OFF として保存する (ユーザー認識と一致)。
    /// </summary>
    private ManualCropFraction? BuildAfterManualCrop()
    {
        if (!CanBuildManualCropFraction()) return null;
        return new ManualCropFraction(
            PixelToFraction(ManualCropPixelX, SourceWidth),
            PixelToFraction(ManualCropPixelY, SourceHeight),
            PixelToFraction(ManualCropPixelWidth, SourceWidth),
            PixelToFraction(ManualCropPixelHeight, SourceHeight));
    }

    /// <summary>ManualCrop の永続化条件: 「手動」 ラジオ + 矩形確定 + 元画像サイズが既知。</summary>
    private bool CanBuildManualCropFraction() =>
        ManualCropEnabled && IsManualCropDefined && SourceWidth > 0 && SourceHeight > 0;

    /// <summary>
    /// 整数ピクセル値を 0–1 の比率に換算する。 入力 <paramref name="pixel"/> が <c>null</c> なら 0 として扱う
    /// (ManualCrop* の編集中 null も保存時に 0 へ coerce する仕様に合わせる)。
    /// </summary>
    private static double PixelToFraction(int? pixel, int total) => (pixel ?? 0) / (double)total;

    /// <summary>
    /// 保存成功後の値を <see cref="_source"/> に反映してリスト表示を最新化する。
    /// CopyName は特性タブで触らないので維持。 OccupySize は配置固有に移管されたので反映なし。
    /// </summary>
    private static void ApplyAfterToSource(CopyItemViewModel source, UpdateImageCopyChanges after)
    {
        source.Rotation = after.Transform!.Value.Rotation;
        source.FlipX = after.Transform.Value.FlipX;
        source.FlipY = after.Transform.Value.FlipY;
        source.ScalingMode = after.ScalingMode!.Value;
        source.Alignment = after.Alignment!.Value;
        source.AutoCrop = after.AutoCrop;
        source.ManualCrop = after.ManualCrop;
        // Regions は after で必ず Empty 以上の配列が来る (BuildAfterRegions が ImmutableArray.Empty を返す)。
        // null の防御は念のため。
        source.Regions = after.Regions ?? source.Regions;
    }

    private void ResetDirtyAndShowSavedMessage()
    {
        _suppressDirty = true;
        try
        {
            IsDirty = false;
            StatusMessage = _loc["Status_Saved"];
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRevert))]
    public void Revert()
    {
        Attach(_source);
    }

    private bool CanSave() => HasCopy && IsDirty;
    private bool CanRevert() => HasCopy && IsDirty;

    /// <summary>
    /// 編集バッファ（プリセット + 閾値 + Custom HEX）から <see cref="AutoCropSettings"/> を組み立てる。
    /// White/Black/Transparent プリセットは static factory の色を使い、Custom は HEX 解析。
    /// </summary>
    private AutoCropSettings BuildAutoCropFromInputs()
    {
        var threshold = (byte)Math.Clamp(AutoCropThreshold ?? 0, 0, 128);
        return AutoCropPreset switch
        {
            AutoCropPreset.Black => new AutoCropSettings(AutoCropSettings.Black.TargetColorArgb, threshold),
            AutoCropPreset.Transparent => new AutoCropSettings(AutoCropSettings.Transparent.TargetColorArgb, threshold),
            AutoCropPreset.Custom => new AutoCropSettings(ParseHexColorOrDefault(AutoCropCustomColorHex), threshold),
            _ => new AutoCropSettings(AutoCropSettings.White.TargetColorArgb, threshold),
        };
    }

    /// <summary>
    /// 永続化済み <see cref="AutoCropSettings"/> から、編集バッファ用の <see cref="AutoCropPreset"/> を逆引きする。
    /// 完全一致のプリセットがあればそれを返し、無ければ <see cref="AutoCropPreset.Custom"/>。
    /// </summary>
    private static AutoCropPreset MapToPreset(AutoCropSettings settings)
    {
        if (settings.TargetColorArgb == AutoCropSettings.White.TargetColorArgb)
            return AutoCropPreset.White;
        if (settings.TargetColorArgb == AutoCropSettings.Black.TargetColorArgb)
            return AutoCropPreset.Black;
        if (settings.TargetColorArgb == AutoCropSettings.Transparent.TargetColorArgb)
            return AutoCropPreset.Transparent;
        return AutoCropPreset.Custom;
    }

    /// <summary>
    /// "#RRGGBB" または "#AARRGGBB" の HEX 文字列を ARGB 32-bit に解析する（α 省略時は 0xFF）。
    /// 解析失敗時は白 <c>0xFFFFFFFF</c> を返す（UI 入力ミスでクラッシュさせない）。
    /// </summary>
    internal static uint ParseHexColorOrDefault(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return AutoCropSettings.White.TargetColorArgb;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return 0xFF000000u | rgb;
        if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return argb;
        return AutoCropSettings.White.TargetColorArgb;
    }

    /// <summary>ARGB 32-bit を "#RRGGBB" 形式の HEX 文字列にフォーマット（α は捨てる）。</summary>
    internal static string FormatHex(uint argb)
    {
        var rgb = argb & 0x00FFFFFFu;
        return $"#{rgb:X6}";
    }

    /// <summary>
    /// サムネ画像のクリック位置から色を採取して <see cref="AutoCropPreset.Custom"/> +
    /// <see cref="AutoCropCustomColorHex"/> に反映する。
    /// <para>
    /// View 側はサムネ <see cref="Avalonia.Media.Imaging.Bitmap.PixelSize"/> 上のクリック座標
    /// （<paramref name="thumbX"/>, <paramref name="thumbY"/>）と寸法
    /// （<paramref name="thumbWidth"/>, <paramref name="thumbHeight"/>）を渡す。本メソッドは
    /// それを原画像座標に等比換算し、<see cref="SourceImagePath"/> から実際の色を採取する。
    /// </para>
    /// <para>
    /// サムネは WebP 圧縮 + ダウンサンプルされており、AutoCrop 走査が原画像で行われる現状仕様と
    /// 色が乖離する（threshold=0 で一致しない）。本メソッドが原画像から採取することで、
    /// 採取色 = 走査対象色になり、ピッカーが意図通りに機能する。
    /// </para>
    /// </summary>
    public async Task PickColorFromThumbnailAsync(
        int thumbX, int thumbY, int thumbWidth, int thumbHeight, CancellationToken ct = default)
    {
        var path = SourceImagePath;
        if (string.IsNullOrEmpty(path)) return;
        if (SourceWidth <= 0 || SourceHeight <= 0) return;
        if (thumbWidth <= 0 || thumbHeight <= 0) return;

        // サムネ座標 → 原画像座標（等比換算）。サムネは max 1024px の縮小版なので、
        // 単色領域内の数ピクセルずれは結果に影響しない（ユーザーが狙う「外周単色」が同じ色なら）。
        var srcX = (int)Math.Clamp((double)thumbX / thumbWidth * SourceWidth, 0, SourceWidth - 1);
        var srcY = (int)Math.Clamp((double)thumbY / thumbHeight * SourceHeight, 0, SourceHeight - 1);

        var argb = await _colorPicker.PickColorAsync(path, srcX, srcY, ct);
        if (argb is not { } color) return;

        // 自動的に Custom プリセットに切り替え + HEX を更新（IsDirty が立つ）
        AutoCropPreset = AutoCropPreset.Custom;
        AutoCropCustomColorHex = FormatHex(color);
    }

    /// <summary>
    /// AutoCrop プレビューの再計算を起動する。AutoCropEnabled / Preset / Threshold /
    /// Custom HEX / Source パスの変更時に呼ばれる。
    /// 進行中の計算があれば <see cref="_autoCropPreviewCts"/> でキャンセルし、
    /// 最新入力に基づく結果だけが <see cref="AutoCropPreviewFraction"/> に反映される。
    /// async void にしないため fire-and-forget で <see cref="RecalculateAutoCropPreviewAsync"/> を呼ぶ。
    /// </summary>
    private void TriggerAutoCropPreviewUpdate()
    {
        // _suppressDirty は Attach 中の一括設定。Attach 終了後にまとめて 1 回呼ぶよう、
        // 中間状態の partial method 通知では preview 計算しない。
        if (_suppressDirty) return;

        _ = RecalculateAutoCropPreviewAsync();
    }

    /// <summary>
    /// プレビュー走査を実行して <see cref="AutoCropPreviewFraction"/> を更新する。
    /// AutoCrop OFF / source 未設定 / asset 未設定なら null + メッセージ null（プレビュー非表示）。
    /// 走査が <c>null</c> を返したら「クロップ範囲なし」を意味するメッセージを表示する。
    /// </summary>
    private async Task RecalculateAutoCropPreviewAsync()
    {
        // 古い計算をキャンセル。Cancel() は ResolveAsync 内の await ポイントで例外として伝播する。
        _autoCropPreviewCts?.Cancel();
        _autoCropPreviewCts?.Dispose();
        _autoCropPreviewCts = new CancellationTokenSource();
        var ct = _autoCropPreviewCts.Token;

        var assetId = _source?.AssetId;
        var sourcePath = SourceImagePath;
        if (!AutoCropEnabled || assetId is null || string.IsNullOrEmpty(sourcePath))
        {
            AutoCropPreviewFraction = null;
            AutoCropPreviewMessage = null;
            return;
        }

        var settings = BuildAutoCropFromInputs();
        try
        {
            var fraction = await _autoCropResolver.ResolveAsync(assetId.Value, sourcePath, settings, ct);
            if (ct.IsCancellationRequested) return;

            if (fraction is null)
            {
                // Resolver が null を返すケース:
                //   1. 走査結果が AutoCropFraction.Full（全領域 = クロップなし）
                //   2. 原画像読込失敗 / cache miss + ファイル不在
                // どちらも UX 上は「クロップ範囲なし」で扱う。原画像読込失敗は HasThumbnail=false の
                // ケースなので、AutoCropEnabled かつ HasThumbnail=true のときは (1) と解釈してよい。
                AutoCropPreviewFraction = null;
                AutoCropPreviewMessage = _loc["Status_AutoCropNoTarget"];
            }
            else
            {
                AutoCropPreviewFraction = fraction;
                AutoCropPreviewMessage = null;
            }
        }
        catch (OperationCanceledException)
        {
            // 連続変更で次の計算に上書きされる。何もしない。
        }
        catch (Exception ex)
        {
            // ファイル I/O 等の予期せぬエラー。プレビューだけ無効化、Save 経路は別途エラー処理。
            AutoCropPreviewFraction = null;
            AutoCropPreviewMessage = $"プレビュー計算に失敗: {ex.Message}";
        }
    }

    // ─── ProtectedRegion コマンド + 内部同期 ─────────────────────────────

    /// <summary>
    /// 新規 region を追加する。 矩形は <see cref="ProtectedRegionItemViewModel.DefaultRect"/>
    /// (画像中央 20%) で初期化され、 直後に Editor (Task 9) で形を整える運用。
    /// <para>
    /// 初期 <c>OffsetXPx</c> / <c>OffsetYPx</c> は <see cref="DefaultRegionOffsetResolver"/> で
    /// 算出する (親側塗り位置と一致するように)。 resolver 未設定 / 計算不可なら (0, 0)。
    /// </para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddRegion))]
    private void AddRegion()
    {
        var defaultRect = ProtectedRegionItemViewModel.DefaultRect;
        var (offsetX, offsetY) = DefaultRegionOffsetResolver?.Invoke(defaultRect) ?? (0, 0);
        var item = new ProtectedRegionItemViewModel(
            Guid.NewGuid(),
            defaultRect,
            ProtectedRegionFillMode.White,
            fillColor: null,
            offsetXPx: offsetX,
            offsetYPx: offsetY);
        RegionItems.Add(item);
        SelectedRegion = item;
    }

    private bool CanAddRegion() => HasCopy;

    /// <summary>選択中の region を削除する。 SelectedRegion=null なら何もしない。</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveRegion))]
    private void RemoveRegion()
    {
        if (SelectedRegion is not { } target) return;
        RegionItems.Remove(target);
        SelectedRegion = null;
    }

    private bool CanRemoveRegion() => SelectedRegion is not null;

    /// <summary>
    /// 選択中の region を 1 つ上に移動する (= SortOrder を 1 つ下げる)。 リスト先頭ならば何もしない。
    /// </summary>
    /// <remarks>
    /// 永続化時の <see cref="ProtectedRegion.SortOrder"/> は <see cref="BuildAfterRegions"/> で
    /// 配列 index = SortOrder として再採番されるため、 ここでは <see cref="ObservableCollection{T}.Move"/>
    /// で配列順を入れ替えるだけで十分。 同 SelectedRegion は参照同一のまま保持される。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanMoveSelectedRegionUp))]
    private void MoveSelectedRegionUp()
    {
        if (SelectedRegion is not { } target) return;
        var index = RegionItems.IndexOf(target);
        if (index <= 0) return;
        RegionItems.Move(index, index - 1);
    }

    private bool CanMoveSelectedRegionUp() =>
        SelectedRegion is not null && RegionItems.IndexOf(SelectedRegion) > 0;

    /// <summary>選択中の region を 1 つ下に移動する (= SortOrder を 1 つ上げる)。 リスト末尾ならば何もしない。</summary>
    [RelayCommand(CanExecute = nameof(CanMoveSelectedRegionDown))]
    private void MoveSelectedRegionDown()
    {
        if (SelectedRegion is not { } target) return;
        var index = RegionItems.IndexOf(target);
        if (index < 0 || index >= RegionItems.Count - 1) return;
        RegionItems.Move(index, index + 1);
    }

    private bool CanMoveSelectedRegionDown()
    {
        if (SelectedRegion is null) return false;
        var index = RegionItems.IndexOf(SelectedRegion);
        return index >= 0 && index < RegionItems.Count - 1;
    }

    /// <summary>
    /// View 側の Editor (Task 9) が rect 編集を完了したときに呼ぶ。 <paramref name="item"/> の
    /// <see cref="ProtectedRegionItemViewModel.Rect"/> を更新し、 IsDirty 連動も自動で発火する
    /// (item 自身の PropertyChanged → <see cref="OnRegionItemPropertyChanged"/>)。
    /// </summary>
    public void UpdateRegionRect(ProtectedRegionItemViewModel item, RegionRectFraction newRect)
    {
        ArgumentNullException.ThrowIfNull(item);
        // 自分の管理下にない item の更新は誤呼び出し。 IsDirty 連動の購読も
        // Add 時にしか張らないので、 ここで外部 item を黙って書き換えると整合が崩れる。
        if (!RegionItems.Contains(item))
            throw new InvalidOperationException("Region item is not in the current collection.");
        item.Rect = newRect;
    }

    /// <summary>
    /// Attach 内で source.Regions から RegionItems を再構築する。 _suppressDirty=true 中に呼ばれる
    /// 前提なので IsDirty は変動しない。 既存 item の PropertyChanged 購読も解除してから差し替える。
    /// </summary>
    private void LoadRegionItemsFromSource(CopyItemViewModel source)
    {
        ClearRegionItems();
        if (source.Regions.IsDefaultOrEmpty) return;
        foreach (var region in source.Regions)
        {
            var item = ProtectedRegionItemViewModel.From(region);
            // CollectionChanged で自動的に PropertyChanged 購読される
            RegionItems.Add(item);
        }
        SelectedRegion = null;
    }

    /// <summary>RegionItems の全要素をクリアし、 各 item の PropertyChanged 購読も解除する。</summary>
    private void ClearRegionItems()
    {
        foreach (var item in RegionItems)
            item.PropertyChanged -= OnRegionItemPropertyChanged;
        RegionItems.Clear();
        SelectedRegion = null;
    }

    /// <summary>
    /// <see cref="RegionItems"/> の Add / Remove / Reset で呼ばれる。 新規追加は per-item の
    /// PropertyChanged を購読、 削除は解除。 dirty も連動 (Attach 中は _suppressDirty で抑止)。
    /// </summary>
    private void OnRegionItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (ProtectedRegionItemViewModel item in e.NewItems)
                item.PropertyChanged += OnRegionItemPropertyChanged;
        if (e.OldItems is not null)
            foreach (ProtectedRegionItemViewModel item in e.OldItems)
                item.PropertyChanged -= OnRegionItemPropertyChanged;

        // HasNoRegions は collection サイズに依存する派生プロパティ。 コレクション変更で必ず通知。
        OnPropertyChanged(nameof(HasNoRegions));

        // Add / Remove / Move いずれでも SelectedRegion の index が変わり得るので、
        // 上下移動コマンドの CanExecute を再評価する (リスト端での灰色化を即時反映)。
        MoveSelectedRegionUpCommand.NotifyCanExecuteChanged();
        MoveSelectedRegionDownCommand.NotifyCanExecuteChanged();

        if (_suppressDirty) return;
        MarkRegionsDirty();
    }

    /// <summary>Region item の編集対象プロパティ変更で IsDirty 連動。</summary>
    private void OnRegionItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirty) return;
        if (e.PropertyName is nameof(ProtectedRegionItemViewModel.Rect)
            or nameof(ProtectedRegionItemViewModel.FillMode)
            or nameof(ProtectedRegionItemViewModel.FillColor)
            or nameof(ProtectedRegionItemViewModel.OffsetXPx)
            or nameof(ProtectedRegionItemViewModel.OffsetYPx)
            or nameof(ProtectedRegionItemViewModel.Rotation)
            or nameof(ProtectedRegionItemViewModel.FlipX)
            or nameof(ProtectedRegionItemViewModel.FlipY))
            MarkRegionsDirty();
    }

    private void MarkRegionsDirty()
    {
        if (!IsDirty) IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    /// <summary>SelectedRegion 変動で削除 / 上下移動コマンドの CanExecute を再評価。</summary>
    partial void OnSelectedRegionChanged(ProtectedRegionItemViewModel? value)
    {
        RemoveRegionCommand.NotifyCanExecuteChanged();
        MoveSelectedRegionUpCommand.NotifyCanExecuteChanged();
        MoveSelectedRegionDownCommand.NotifyCanExecuteChanged();
    }

    /// <summary>HasCopy 変動で AddRegion コマンドの CanExecute を再評価。</summary>
    partial void OnHasCopyChanged(bool value)
    {
        AddRegionCommand.NotifyCanExecuteChanged();
    }

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirty)
            return;

        // メタ状態の変化はダーティ化しない（編集バッファ以外の表示用プロパティ）。
        // AutoCropPreview* は Attach 直後の TriggerAutoCropPreviewUpdate (非同期) で更新されるため、
        // ここを除外しないと「セル選択しただけで未保存」になる回帰が出る。
        // SelectedRegion は ListBox の選択状態のみで永続化対象ではないので、 単なるリスト選択で
        // dirty が立って auto-save が走らないよう除外する。
        if (e.PropertyName is nameof(IsDirty) or nameof(HasCopy)
            or nameof(StatusMessage) or nameof(MultiSelectMessage)
            or nameof(AutoCropPreviewFraction) or nameof(AutoCropPreviewMessage)
            or nameof(HasAutoCropPreview)
            or nameof(SelectedRegion))
            return;

        if (!IsDirty)
            IsDirty = true;

        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "論理コピー特性を保存: {CopyId}")]
    private static partial void LogSaved(ILogger logger, System.Guid copyId);

    public void Dispose()
    {
        _autoCropPreviewCts?.Cancel();
        _autoCropPreviewCts?.Dispose();
        _autoCropPreviewCts = null;
    }
}

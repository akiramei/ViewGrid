using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
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
        ILogger<CopyPropertiesViewModel> logger)
    {
        _updateUseCase = updateUseCase;
        _history = history;
        _messenger = messenger;
        _colorPicker = colorPicker;
        _autoCropResolver = autoCropResolver;
        _logger = logger;
        PropertyChanged += OnAnyPropertyChanged;
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
                AutoCropPreset = AutoCropPreset.White;
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
                    AutoCropPreset = AutoCropPreset.White;
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

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (_source is null || !IsDirty)
            return;

        // before snapshot: source の現在値（保存前 = ロード時 / 直前 Save 時の永続化済み値）。
        // CopyName は本タブの編集対象外（インラインリネーム経由）なので CopyName=null + ClearCopyName=false で
        // 「変更しない」を明示する（UpdateImageCopyUseCase 側で current 値を保持する）。
        // AutoCrop / ManualCrop は: null のときは Clear*=true で「OFF へ戻す」を明示する。
        var before = new UpdateImageCopyChanges
        {
            Transform = new ImageTransform(_source.Rotation, _source.FlipX, _source.FlipY),
            ScalingMode = _source.ScalingMode,
            Alignment = _source.Alignment,
            // OccupySize は本タブの編集対象外（配置固有に移管）。null = 変更しない。
            AutoCrop = _source.AutoCrop,
            ClearAutoCrop = _source.AutoCrop is null,
            ManualCrop = _source.ManualCrop,
            ClearManualCrop = _source.ManualCrop is null,
        };

        // after: 編集バッファから組み立て。CopyName は触らない（インラインリネームとは独立）。
        var afterAutoCrop = AutoCropEnabled ? BuildAutoCropFromInputs() : (AutoCropSettings?)null;
        // ManualCrop は「ラジオで手動を選んでいて、かつ矩形が確定している」ときのみ永続化。
        // 「手動」選択 + 未確定（W=0 or H=0）は実質 OFF として保存する（ユーザー認識と一致）。
        var afterManualCrop = (ManualCropEnabled && IsManualCropDefined && SourceWidth > 0 && SourceHeight > 0)
            ? new ManualCropFraction(
                (ManualCropPixelX ?? 0) / (double)SourceWidth,
                (ManualCropPixelY ?? 0) / (double)SourceHeight,
                (ManualCropPixelWidth ?? 0) / (double)SourceWidth,
                (ManualCropPixelHeight ?? 0) / (double)SourceHeight)
            : (ManualCropFraction?)null;
        var after = new UpdateImageCopyChanges
        {
            Transform = new ImageTransform(Rotation, FlipX, FlipY),
            ScalingMode = ScalingMode,
            Alignment = new Alignment(AlignX, AlignY),
            // OccupySize は本タブで触らない（配置固有）。null で「変更しない」を明示。
            AutoCrop = afterAutoCrop,
            ClearAutoCrop = afterAutoCrop is null,
            ManualCrop = afterManualCrop,
            ClearManualCrop = afterManualCrop is null,
        };

        // Description は Save 時点での名前を表示用に使う（リネーム結果の追跡は UpdateImageCopyCommand
        // のリネーム経路が別に表示するため、こちらは固定の「特性編集: 「{name}」」だけで良い）。
        var nameLabel = string.IsNullOrWhiteSpace(_source.CopyName) ? "(無名)" : _source.CopyName!;
        var description = $"特性編集: 「{nameLabel}」";
        var command = new UpdateImageCopyCommand(_updateUseCase, _source.CopyId, before, after, description);
        var execResult = await _history.ExecuteAsync(command, ct);
        if (execResult.IsError)
        {
            // ErrorOr.Error の自動 ToString は record dump 形式（"Error { Code=..., Description=..., ... }"）で
            // ユーザーには冗長すぎるため Description のみを連結する。検証エラーがそのまま画面に出る経路。
            StatusMessage = string.Join(", ", execResult.Errors.Select(e => e.Description));
            return;
        }

        // source にも反映してリスト表示を最新化する（after の値で）。
        // CopyName は特性タブで触らないので _source の現状値を維持する。
        _source.Rotation = after.Transform!.Value.Rotation;
        _source.FlipX = after.Transform.Value.FlipX;
        _source.FlipY = after.Transform.Value.FlipY;
        _source.ScalingMode = after.ScalingMode!.Value;
        _source.Alignment = after.Alignment!.Value;
        // OccupySize は本タブの編集対象外なので _source への反映も行わない。
        _source.AutoCrop = after.AutoCrop;
        _source.ManualCrop = after.ManualCrop;

        _suppressDirty = true;
        try
        {
            IsDirty = false;
            StatusMessage = "保存しました。";
        }
        finally
        {
            _suppressDirty = false;
        }

        _messenger.Send(new CopyLibraryChangedMessage());
        LogSaved(_logger, _source.CopyId);
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
                AutoCropPreviewMessage = "対象色によるクロップ範囲がありません（または対象色が見つかりません）";
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

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirty)
            return;

        // メタ状態の変化はダーティ化しない（編集バッファ以外の表示用プロパティ）。
        // AutoCropPreview* は Attach 直後の TriggerAutoCropPreviewUpdate (非同期) で更新されるため、
        // ここを除外しないと「セル選択しただけで未保存」になる回帰が出る。
        if (e.PropertyName is nameof(IsDirty) or nameof(HasCopy)
            or nameof(StatusMessage) or nameof(MultiSelectMessage)
            or nameof(AutoCropPreviewFraction) or nameof(AutoCropPreviewMessage)
            or nameof(HasAutoCropPreview))
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

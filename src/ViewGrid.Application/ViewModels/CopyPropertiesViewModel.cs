using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 選択された論理コピーの特性を編集する。Attach で値を読み込み、
/// 変更があれば IsDirty を立て、Save で永続化する。
/// </summary>
public sealed partial class CopyPropertiesViewModel : ViewModelBase
{
    private readonly UpdateImageCopyUseCase _updateUseCase;
    private readonly IUndoRedoService _history;
    private readonly IMessenger _messenger;
    private readonly IImageColorPicker _colorPicker;
    private readonly ILogger<CopyPropertiesViewModel> _logger;

    private CopyItemViewModel? _source;
    private bool _suppressDirty;

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
    [ObservableProperty] public partial string? CopyName { get; set; }
    [ObservableProperty] public partial Rotation Rotation { get; set; }
    [ObservableProperty] public partial bool FlipX { get; set; }
    [ObservableProperty] public partial bool FlipY { get; set; }
    [ObservableProperty] public partial ScalingMode ScalingMode { get; set; } = ScalingMode.UniformContain;
    [ObservableProperty] public partial AnchorX AlignX { get; set; } = AnchorX.Center;
    [ObservableProperty] public partial AnchorY AlignY { get; set; } = AnchorY.Center;
    [ObservableProperty] public partial int OccupyWidth { get; set; } = 1;
    [ObservableProperty] public partial int OccupyHeight { get; set; } = 1;

    /// <summary>単色余白の自動トリミング機能の ON/OFF。OFF なら <see cref="AutoCropPreset"/> /
    /// <see cref="AutoCropThreshold"/> は無視され、保存時に AutoCrop=null となる。</summary>
    [ObservableProperty] public partial bool AutoCropEnabled { get; set; }

    /// <summary>対象色プリセット（白/黒/透明/カスタム）。<see cref="AutoCropPreset.Custom"/> 選択時は
    /// <see cref="AutoCropCustomColorHex"/> の値（または画像クリックピッカーで採取した色）を使う。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoCropCustom))]
    public partial AutoCropPreset AutoCropPreset { get; set; } = AutoCropPreset.White;

    /// <summary>許容色差（Chebyshev、0–128）。0 で完全一致のみ余白扱い。</summary>
    [ObservableProperty] public partial int AutoCropThreshold { get; set; } = 8;

    /// <summary>カスタム対象色の HEX 表記（<c>#RRGGBB</c>）。<see cref="AutoCropPreset.Custom"/>
    /// 選択時のみ <c>BuildAutoCropFromInputs</c> で参照される。
    /// 画像クリックピッカーで色を採取するとここに反映される。</summary>
    [ObservableProperty] public partial string AutoCropCustomColorHex { get; set; } = "#FFFFFF";

    /// <summary>任意矩形トリミング機能の ON/OFF。OFF なら永続化時に ManualCrop=null（OFF）となる。
    /// AutoCrop と排他で、こちらを ON にすると AutoCropEnabled が自動的に OFF になる。</summary>
    [ObservableProperty] public partial bool ManualCropEnabled { get; set; }

    /// <summary>矩形左上 X（元画像ピクセル）。永続化時に SourceWidth で割って 0–1 比率に換算。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial double ManualCropPixelX { get; set; }

    /// <summary>矩形左上 Y（元画像ピクセル）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial double ManualCropPixelY { get; set; }

    /// <summary>矩形幅（元画像ピクセル）。0 のときは「未確定」状態（手動ラジオを選んだ直後など）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial double ManualCropPixelWidth { get; set; }

    /// <summary>矩形高さ（元画像ピクセル）。0 のときは未確定。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualCropDefined))]
    public partial double ManualCropPixelHeight { get; set; }

    /// <summary>矩形が確定しているか（W&gt;0 かつ H&gt;0）。「手動」ラジオ ON 直後でドラッグ前は false。
    /// 数値入力フィールドや矩形ハンドルの IsEnabled、Save 時の永続化判定に使う。</summary>
    public bool IsManualCropDefined =>
        ManualCropPixelWidth > 0.0 && ManualCropPixelHeight > 0.0;

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
    }

    /// <summary>排他連動: ManualCrop ON にすると AutoCrop は OFF。</summary>
    partial void OnManualCropEnabledChanged(bool value)
    {
        if (value && AutoCropEnabled)
        {
            AutoCropEnabled = false;
        }
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
        ILogger<CopyPropertiesViewModel> logger)
    {
        _updateUseCase = updateUseCase;
        _history = history;
        _messenger = messenger;
        _colorPicker = colorPicker;
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
                CopyName = null;
                Rotation = Rotation.None;
                FlipX = false;
                FlipY = false;
                ScalingMode = ScalingMode.UniformContain;
                AlignX = AnchorX.Center;
                AlignY = AnchorY.Center;
                OccupyWidth = 1;
                OccupyHeight = 1;
                AutoCropEnabled = false;
                AutoCropPreset = AutoCropPreset.White;
                AutoCropThreshold = 8;
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
                CopyName = source.CopyName;
                Rotation = source.Rotation;
                FlipX = source.FlipX;
                FlipY = source.FlipY;
                ScalingMode = source.ScalingMode;
                AlignX = source.Alignment.X;
                AlignY = source.Alignment.Y;
                OccupyWidth = source.OccupySize.Width;
                OccupyHeight = source.OccupySize.Height;
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
                    AutoCropThreshold = 8;
                    AutoCropCustomColorHex = "#FFFFFF";
                }
                if (source.ManualCrop is { } mc && source.SourceWidth > 0 && source.SourceHeight > 0)
                {
                    ManualCropEnabled = true;
                    ManualCropPixelX = mc.X * source.SourceWidth;
                    ManualCropPixelY = mc.Y * source.SourceHeight;
                    ManualCropPixelWidth = mc.Width * source.SourceWidth;
                    ManualCropPixelHeight = mc.Height * source.SourceHeight;
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
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (_source is null || !IsDirty)
            return;

        // before snapshot: source の現在値（保存前 = ロード時 / 直前 Save 時の永続化済み値）。
        // Undo で「null 名前に戻す」を明示するため、CopyName が null のときは ClearCopyName=true で立てる。
        // AutoCrop / ManualCrop も同様: null のときは Clear*=true で「OFF へ戻す」を明示する。
        var before = new UpdateImageCopyChanges
        {
            CopyName = _source.CopyName,
            ClearCopyName = _source.CopyName is null,
            Transform = new ImageTransform(_source.Rotation, _source.FlipX, _source.FlipY),
            ScalingMode = _source.ScalingMode,
            Alignment = _source.Alignment,
            OccupySize = _source.OccupySize,
            AutoCrop = _source.AutoCrop,
            ClearAutoCrop = _source.AutoCrop is null,
            ManualCrop = _source.ManualCrop,
            ClearManualCrop = _source.ManualCrop is null,
        };

        // after: 編集バッファから組み立て。空文字 → null は「明示的に名前を消す」操作で、
        // Redo でも同じ「null へ更新」が必要になるので ClearCopyName=true。
        var afterCopyName = string.IsNullOrWhiteSpace(CopyName) ? null : CopyName;
        var afterAutoCrop = AutoCropEnabled ? BuildAutoCropFromInputs() : (AutoCropSettings?)null;
        // ManualCrop は「ラジオで手動を選んでいて、かつ矩形が確定している」ときのみ永続化。
        // 「手動」選択 + 未確定（W=0 or H=0）は実質 OFF として保存する（ユーザー認識と一致）。
        var afterManualCrop = (ManualCropEnabled && IsManualCropDefined && SourceWidth > 0 && SourceHeight > 0)
            ? new ManualCropFraction(
                ManualCropPixelX / SourceWidth,
                ManualCropPixelY / SourceHeight,
                ManualCropPixelWidth / SourceWidth,
                ManualCropPixelHeight / SourceHeight)
            : (ManualCropFraction?)null;
        var after = new UpdateImageCopyChanges
        {
            CopyName = afterCopyName,
            ClearCopyName = afterCopyName is null,
            Transform = new ImageTransform(Rotation, FlipX, FlipY),
            ScalingMode = ScalingMode,
            Alignment = new Alignment(AlignX, AlignY),
            OccupySize = BuildOccupySizeOrDefault(),
            AutoCrop = afterAutoCrop,
            ClearAutoCrop = afterAutoCrop is null,
            ManualCrop = afterManualCrop,
            ClearManualCrop = afterManualCrop is null,
        };

        // Description は「Save 時点での名前 → after の名前」を含める。改名されたケースで履歴上わかりやすい。
        var beforeNameLabel = string.IsNullOrWhiteSpace(_source.CopyName) ? "(無名)" : _source.CopyName;
        var afterNameLabel = string.IsNullOrWhiteSpace(afterCopyName) ? "(無名)" : afterCopyName;
        var description = string.Equals(beforeNameLabel, afterNameLabel, StringComparison.Ordinal)
            ? $"特性編集: 「{beforeNameLabel}」"
            : $"特性編集: 「{beforeNameLabel}」→「{afterNameLabel}」";
        var command = new UpdateImageCopyCommand(_updateUseCase, _source.CopyId, before, after, description);
        var execResult = await _history.ExecuteAsync(command, ct);
        if (execResult.IsError)
        {
            StatusMessage = string.Join(", ", execResult.Errors);
            return;
        }

        // source にも反映してリスト表示を最新化する（after の値で）
        _source.CopyName = after.CopyName;
        _source.Rotation = after.Transform!.Value.Rotation;
        _source.FlipX = after.Transform.Value.FlipX;
        _source.FlipY = after.Transform.Value.FlipY;
        _source.ScalingMode = after.ScalingMode!.Value;
        _source.Alignment = after.Alignment!.Value;
        _source.OccupySize = after.OccupySize!.Value;
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

    private OccupySize BuildOccupySizeOrDefault()
    {
        var w = OccupyWidth < 1 ? 1 : OccupyWidth;
        var h = OccupyHeight < 1 ? 1 : OccupyHeight;
        return new OccupySize(w, h);
    }

    /// <summary>
    /// 編集バッファ（プリセット + 閾値 + Custom HEX）から <see cref="AutoCropSettings"/> を組み立てる。
    /// White/Black/Transparent プリセットは static factory の色を使い、Custom は HEX 解析。
    /// </summary>
    private AutoCropSettings BuildAutoCropFromInputs()
    {
        var threshold = (byte)Math.Clamp(AutoCropThreshold, 0, 128);
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

    private void OnAnyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDirty)
            return;

        // メタ状態の変化はダーティ化しない（編集バッファ以外の表示用プロパティ）
        if (e.PropertyName is nameof(IsDirty) or nameof(HasCopy)
            or nameof(StatusMessage) or nameof(MultiSelectMessage))
            return;

        if (!IsDirty)
            IsDirty = true;

        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "論理コピー特性を保存: {CopyId}")]
    private static partial void LogSaved(ILogger logger, System.Guid copyId);
}

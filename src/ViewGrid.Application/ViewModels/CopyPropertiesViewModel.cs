using System;
using System.Collections.Generic;
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

    /// <summary>対象色プリセット（白/黒/透明）。Phase 1 では <see cref="AutoCropPreset.Custom"/> は未使用。</summary>
    [ObservableProperty] public partial AutoCropPreset AutoCropPreset { get; set; } = AutoCropPreset.White;

    /// <summary>許容色差（Chebyshev、0–128）。0 で完全一致のみ余白扱い。</summary>
    [ObservableProperty] public partial int AutoCropThreshold { get; set; } = 8;

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
    ];

    public CopyPropertiesViewModel(
        UpdateImageCopyUseCase updateUseCase,
        IUndoRedoService history,
        IMessenger messenger,
        ILogger<CopyPropertiesViewModel> logger)
    {
        _updateUseCase = updateUseCase;
        _history = history;
        _messenger = messenger;
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
                if (source.AutoCrop is { } ac)
                {
                    AutoCropEnabled = true;
                    AutoCropPreset = MapToPreset(ac);
                    AutoCropThreshold = ac.Threshold;
                }
                else
                {
                    AutoCropEnabled = false;
                    AutoCropPreset = AutoCropPreset.White;
                    AutoCropThreshold = 8;
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
        // AutoCrop も同様: null のときは ClearAutoCrop=true で「OFF へ戻す」を明示する。
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
        };

        // after: 編集バッファから組み立て。空文字 → null は「明示的に名前を消す」操作で、
        // Redo でも同じ「null へ更新」が必要になるので ClearCopyName=true。
        var afterCopyName = string.IsNullOrWhiteSpace(CopyName) ? null : CopyName;
        var afterAutoCrop = AutoCropEnabled ? BuildAutoCropFromInputs() : (AutoCropSettings?)null;
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
    /// 編集バッファ（プリセット + 閾値）から <see cref="AutoCropSettings"/> を組み立てる。
    /// プリセットの色情報は static factory（<see cref="AutoCropSettings.White"/> 等）から取得し、
    /// 閾値だけ <see cref="AutoCropThreshold"/> でオーバーライドする。
    /// </summary>
    private AutoCropSettings BuildAutoCropFromInputs()
    {
        var threshold = (byte)Math.Clamp(AutoCropThreshold, 0, 128);
        return AutoCropPreset switch
        {
            AutoCropPreset.Black => new AutoCropSettings(AutoCropSettings.Black.TargetColorArgb, threshold),
            AutoCropPreset.Transparent => new AutoCropSettings(AutoCropSettings.Transparent.TargetColorArgb, threshold),
            // White / Custom（Phase 1 では Custom 未使用のため White と同じ扱い）
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

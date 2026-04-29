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
    [ObservableProperty] public partial AnchorX TrimAnchorX { get; set; } = AnchorX.Center;
    [ObservableProperty] public partial AnchorY TrimAnchorY { get; set; } = AnchorY.Center;
    [ObservableProperty] public partial AnchorX AlignX { get; set; } = AnchorX.Center;
    [ObservableProperty] public partial AnchorY AlignY { get; set; } = AnchorY.Center;
    [ObservableProperty] public partial int OccupyWidth { get; set; } = 1;
    [ObservableProperty] public partial int OccupyHeight { get; set; } = 1;

    /// <summary>
    /// <see cref="ScalingMode.None"/> のときだけ <see cref="TrimAnchorX"/> /
    /// <see cref="TrimAnchorY"/> が renderer に効く。それ以外のモード（Uniform 系・Cover・Fill）では
    /// 効かないため、UI でも IsEnabled で連動グレーアウトして「いまどっちが効いているか」を可視化する。
    /// </summary>
    public bool IsTrimAnchorActive => ScalingMode == ScalingMode.None;

    /// <summary>
    /// <see cref="ScalingMode.None"/> 以外のときだけ <see cref="AlignX"/> / <see cref="AlignY"/> が
    /// renderer に効く（None では <see cref="TrimAnchorX"/> / <see cref="TrimAnchorY"/> が効くため
    /// Alignment は効かない）。<see cref="IsTrimAnchorActive"/> の対称。
    /// </summary>
    public bool IsAlignmentActive => ScalingMode != ScalingMode.None;

    partial void OnScalingModeChanged(ScalingMode value)
    {
        OnPropertyChanged(nameof(IsTrimAnchorActive));
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
                TrimAnchorX = AnchorX.Center;
                TrimAnchorY = AnchorY.Center;
                AlignX = AnchorX.Center;
                AlignY = AnchorY.Center;
                OccupyWidth = 1;
                OccupyHeight = 1;
            }
            else
            {
                HasCopy = true;
                CopyName = source.CopyName;
                Rotation = source.Rotation;
                FlipX = source.FlipX;
                FlipY = source.FlipY;
                ScalingMode = source.ScalingMode;
                TrimAnchorX = source.TrimmingAnchor.X;
                TrimAnchorY = source.TrimmingAnchor.Y;
                AlignX = source.Alignment.X;
                AlignY = source.Alignment.Y;
                OccupyWidth = source.OccupySize.Width;
                OccupyHeight = source.OccupySize.Height;
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
        var before = new UpdateImageCopyChanges
        {
            CopyName = _source.CopyName,
            ClearCopyName = _source.CopyName is null,
            Transform = new ImageTransform(_source.Rotation, _source.FlipX, _source.FlipY),
            ScalingMode = _source.ScalingMode,
            TrimmingAnchor = _source.TrimmingAnchor,
            Alignment = _source.Alignment,
            OccupySize = _source.OccupySize,
        };

        // after: 編集バッファから組み立て。空文字 → null は「明示的に名前を消す」操作で、
        // Redo でも同じ「null へ更新」が必要になるので ClearCopyName=true。
        var afterCopyName = string.IsNullOrWhiteSpace(CopyName) ? null : CopyName;
        var after = new UpdateImageCopyChanges
        {
            CopyName = afterCopyName,
            ClearCopyName = afterCopyName is null,
            Transform = new ImageTransform(Rotation, FlipX, FlipY),
            ScalingMode = ScalingMode,
            TrimmingAnchor = new TrimmingAnchor(TrimAnchorX, TrimAnchorY),
            Alignment = new Alignment(AlignX, AlignY),
            OccupySize = BuildOccupySizeOrDefault(),
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
        _source.TrimmingAnchor = after.TrimmingAnchor!.Value;
        _source.Alignment = after.Alignment!.Value;
        _source.OccupySize = after.OccupySize!.Value;

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

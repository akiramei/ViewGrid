using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
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
        IMessenger messenger,
        ILogger<CopyPropertiesViewModel> logger)
    {
        _updateUseCase = updateUseCase;
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

        var changes = new UpdateImageCopyChanges
        {
            CopyName = string.IsNullOrWhiteSpace(CopyName) ? null : CopyName,
            Transform = new ImageTransform(Rotation, FlipX, FlipY),
            ScalingMode = ScalingMode,
            TrimmingAnchor = new TrimmingAnchor(TrimAnchorX, TrimAnchorY),
            Alignment = new Alignment(AlignX, AlignY),
            OccupySize = BuildOccupySizeOrDefault(),
        };

        var result = await _updateUseCase.ExecuteAsync(_source.CopyId, changes, ct);
        if (result.IsError)
        {
            StatusMessage = string.Join(", ", result.Errors);
            return;
        }

        // source にも反映してリスト表示を最新化する
        var updated = result.Value;
        _source.CopyName = updated.CopyName;
        _source.Rotation = updated.Transform.Rotation;
        _source.FlipX = updated.Transform.FlipX;
        _source.FlipY = updated.Transform.FlipY;
        _source.ScalingMode = updated.ScalingMode;
        _source.TrimmingAnchor = updated.TrimmingAnchor;
        _source.Alignment = updated.Alignment;
        _source.OccupySize = updated.OccupySize;

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
        LogSaved(_logger, updated.Id);
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

        // メタ状態の変化はダーティ化しない
        if (e.PropertyName is nameof(IsDirty) or nameof(HasCopy) or nameof(StatusMessage))
            return;

        if (!IsDirty)
            IsDirty = true;

        SaveCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "論理コピー特性を保存: {CopyId}")]
    private static partial void LogSaved(ILogger logger, System.Guid copyId);
}

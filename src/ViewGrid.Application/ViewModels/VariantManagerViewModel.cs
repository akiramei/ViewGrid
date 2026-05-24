using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ViewGrid.Application.History;
using ViewGrid.Application.History.Commands;
using ViewGrid.Application.Localization;
using ViewGrid.Application.Messages;
using ViewGrid.Application.UseCases;
using ViewGrid.Core.Interfaces;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// 配置タブの候補リスト上での「バリアント新規作成 (フライアウト)」「バリアント削除」「インラインリネーム」
/// を司る VM。 Phase 4 で <see cref="GridWorkspaceViewModel"/> から抽出。
/// <para>
/// 候補リスト (Candidates / CandidateGroups / SelectedCandidate) や IsBusy / StatusMessage の
/// 読み書きは親 (<see cref="IVariantManagerContext"/>) を経由する。 候補リストの状態を一元管理
/// するのは親 VM 側で、 本 VM は CRUD 操作と作成フライアウト state (<see cref="IsCreatingVariant"/> /
/// <see cref="DraftVariantName"/>) のみを所有する。
/// </para>
/// </summary>
public sealed partial class VariantManagerViewModel : ViewModelBase
{
    private readonly CreateLogicalCopyUseCase _createCopyUseCase;
    private readonly UpdateImageCopyUseCase _updateCopyUseCase;
    private readonly DeleteImageAssetUseCase _deleteAssetUseCase;
    private readonly IImageCopyRepository _copyRepository;
    private readonly IUndoRedoService _history;
    private readonly IMessenger _messenger;
    private readonly ILocalizationService _loc;
    private readonly ILogger<VariantManagerViewModel> _logger;
    private IVariantManagerContext? _context;

    /// <summary>
    /// <see cref="AttachContext"/> で attach された親 (<see cref="GridWorkspaceViewModel"/>) へのアクセサ。
    /// 未 attach のままメソッドが呼ばれた場合は明確な例外を出して構築順の誤りを早期検出する。
    /// </summary>
    private IVariantManagerContext Context => _context
        ?? throw new InvalidOperationException(
            $"{nameof(VariantManagerViewModel)}.{nameof(AttachContext)} must be called before using this VM.");

    /// <summary>
    /// 「+ 新規バリアント」 フライアウトを開いているか。 <c>true</c> の間だけ View 側で名前入力 TextBox と
    /// 確定/キャンセルボタンが表示される (<see cref="GridCanvasListViewModel.IsCreating"/> と同パターン)。
    /// 生成先のアセットは <see cref="IVariantManagerContext.SelectedCandidate"/> の AssetId。
    /// </summary>
    [ObservableProperty]
    public partial bool IsCreatingVariant { get; set; }

    /// <summary>
    /// 新規作成フライアウトの名前ドラフト。 空白だけ / 空文字なら「バリアント N」 自動採番、
    /// 値があればそれを <see cref="CreateLogicalCopyUseCase"/> に渡す。
    /// </summary>
    [ObservableProperty]
    public partial string DraftVariantName { get; set; } = string.Empty;

    public VariantManagerViewModel(
        CreateLogicalCopyUseCase createCopyUseCase,
        UpdateImageCopyUseCase updateCopyUseCase,
        DeleteImageAssetUseCase deleteAssetUseCase,
        IImageCopyRepository copyRepository,
        IUndoRedoService history,
        IMessenger messenger,
        ILocalizationService loc,
        ILogger<VariantManagerViewModel> logger)
    {
        _createCopyUseCase = createCopyUseCase;
        _updateCopyUseCase = updateCopyUseCase;
        _deleteAssetUseCase = deleteAssetUseCase;
        _copyRepository = copyRepository;
        _history = history;
        _messenger = messenger;
        _loc = loc;
        _logger = logger;
    }

    /// <summary>
    /// 親 (<see cref="GridWorkspaceViewModel"/>) を attach する 2-phase 初期化。
    /// 詳細は <see cref="GridOutputViewModel.AttachContext"/> 参照。
    /// </summary>
    public void AttachContext(IVariantManagerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_context is not null)
            throw new InvalidOperationException($"{nameof(VariantManagerViewModel)} is already attached.");
        _context = context;
    }

    /// <summary>
    /// 「+ 新規バリアント」 フライアウトを開く。 <see cref="DraftVariantName"/> を空にリセットして、
    /// View 側で名前入力 TextBox を表示する。 SelectedCandidate 未選択 / IsBusy 中は no-op。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBeginCreateVariant))]
    public void BeginCreateVariant()
    {
        if (Context.SelectedCandidate is null || Context.IsBusy) return;
        DraftVariantName = string.Empty;
        IsCreatingVariant = true;
    }

    private bool CanBeginCreateVariant() => Context.SelectedCandidate is not null && !Context.IsBusy;

    /// <summary>新規作成フライアウトを閉じる (作成しない)。</summary>
    [RelayCommand]
    public void CancelCreateVariant()
    {
        IsCreatingVariant = false;
        DraftVariantName = string.Empty;
    }

    /// <summary>
    /// 新規作成フライアウトの確定。 <see cref="DraftVariantName"/> を渡して
    /// <see cref="CreateLogicalCopyUseCase"/> を呼び、 SelectedCandidate のアセットに紐づく新バリアントを
    /// 生成する。 空白 / 空文字なら「バリアント N」 自動採番。
    /// 新規 Copy 作成は Undo 対象外 (履歴に積めないので _history.Clear())。
    /// </summary>
    [RelayCommand]
    public async Task CommitCreateVariantAsync(CancellationToken ct = default)
    {
        var candidate = Context.SelectedCandidate;
        if (candidate is null || Context.IsBusy)
        {
            IsCreatingVariant = false;
            DraftVariantName = string.Empty;
            return;
        }

        try
        {
            Context.IsBusy = true;
            var assetId = candidate.AssetId;
            // 命名規則: 同じアセットに紐づく既存バリアント数 + 1
            var ordinal = Context.Candidates.Count(c => c.AssetId == assetId) + 1;
            var nameToUse = string.IsNullOrWhiteSpace(DraftVariantName)
                ? $"{_loc[Terminology.VariantPrefixKey]} {ordinal}"
                : DraftVariantName.Trim();

            var result = await _createCopyUseCase.ExecuteAsync(assetId, copyName: nameToUse, ct: ct);
            if (result.IsError)
            {
                Context.StatusMessage = string.Join(", ", result.Errors);
                return;
            }

            Context.StatusMessage = _loc.Format("Status_VariantCreatedFmt", nameToUse);
            // 新規 Copy 作成は Undo 対象外。 既存履歴の参照整合性が崩れる前にクリア。
            _history.Clear();
            _messenger.Send(new CopyLibraryChangedMessage());
            // 新バリアントを選択状態にするため、 Candidates 再ロード後に CopyId で再選択。
            // ReloadFromMessageAsync は fire-and-forget なので await できないが、
            // 親が受信側でもあるため Receive → ReloadFromMessageAsync の経路で更新される。
            await Context.LoadCandidatesAsync(ct);
            Context.SelectedCandidate = Context.Candidates.FirstOrDefault(c => c.CopyId == result.Value.Id) ?? Context.SelectedCandidate;
            LogVariantCreated(_logger, assetId, result.Value.Id);
        }
        finally
        {
            Context.IsBusy = false;
            IsCreatingVariant = false;
            DraftVariantName = string.Empty;
        }
    }

    /// <summary>
    /// 選択中バリアントを削除する。 Cascade で関連 Placement も消えるため、 履歴は全消去。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelectedCandidate))]
    public async Task DeleteSelectedCandidateAsync(CancellationToken ct = default)
    {
        var target = Context.SelectedCandidate;
        if (target is null || Context.IsBusy) return;

        try
        {
            Context.IsBusy = true;
            var label = target.CopyDisplayName;

            // このアセットの最後のバリアントか判定する。 そうなら親アセットごと
            // cascade 削除する (孤児アセット = アセット件数だけ残って候補リストから
            // 完全に見えなくなる状態を防止)。
            var group = Context.CandidateGroups.FirstOrDefault(g => g.AssetId == target.AssetId);
            var isLastVariant = group is not null && group.Variants.Count == 1;

            if (isLastVariant)
            {
                // DeleteImageAssetUseCase は DB cascade で ImageCopy / GridPlacement / ProtectedRegion を
                // まとめて消し、 画像本体ファイルとサムネも削除する。
                var assetResult = await _deleteAssetUseCase.ExecuteAsync(target.AssetId, ct);
                if (assetResult.IsError)
                {
                    Context.StatusMessage = string.Join(", ", assetResult.Errors);
                    return;
                }
            }
            else
            {
                var copyResult = await _copyRepository.DeleteAsync(target.CopyId, ct);
                if (copyResult.IsError)
                {
                    Context.StatusMessage = string.Join(", ", copyResult.Errors);
                    return;
                }
            }

            Context.Candidates.Remove(target);
            if (group is not null)
            {
                group.Variants.Remove(target);
                if (group.Variants.Count == 0)
                    Context.CandidateGroups.Remove(group);
            }
            Context.SelectedCandidate = Context.Candidates.FirstOrDefault();

            Context.StatusMessage = _loc.Format("Status_VariantDeletedFmt", label);
            // Copy / Asset 削除は cascade で Placement も消えるため履歴を全消去
            _history.Clear();
            _messenger.Send(new CopyLibraryChangedMessage());
            if (isLastVariant)
            {
                // AssetLibraryViewModel.Assets を再ロードさせる (アセット件数表示を更新)
                _messenger.Send(new AssetLibraryChangedMessage());
            }
            LogVariantDeleted(_logger, target.CopyId);
        }
        finally
        {
            Context.IsBusy = false;
        }
    }

    private bool CanDeleteSelectedCandidate() => !Context.IsBusy && Context.SelectedCandidate is not null;

    /// <summary>
    /// インラインリネーム編集を開始する。 <paramref name="candidate"/> の <c>IsEditing</c>=true、
    /// <c>EditingName</c> に現在の <c>CopyName</c> をコピー。
    /// 同時に他項目が編集中なら強制的にキャンセルする (同時編集を防ぐ)。
    /// </summary>
    public void BeginEditCandidate(CopyCandidateViewModel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        foreach (var c in Context.Candidates)
        {
            if (!ReferenceEquals(c, candidate) && c.IsEditing)
            {
                c.IsEditing = false;
                c.EditingName = null;
            }
        }
        candidate.EditingName = candidate.CopyName;
        candidate.IsEditing = true;
    }

    /// <summary>インラインリネーム編集をキャンセル (保存しない)。</summary>
    public void CancelEditCandidate(CopyCandidateViewModel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate.IsEditing = false;
        candidate.EditingName = null;
        LogVariantRenameCanceled(_logger, candidate.CopyId);
    }

    /// <summary>
    /// インラインリネームを確定して DB に保存する。 <c>EditingName</c> を trim (空白だけなら null) した上で
    /// <c>CopyName</c> と比較し、 同じなら no-op、 違えば <see cref="UpdateImageCopyCommand"/> を組み立てて
    /// 履歴に積む。 Undo/Redo round-trip 対応。
    /// </summary>
    public async Task CommitEditCandidateAsync(CopyCandidateViewModel candidate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsEditing) return;

        var trimmed = string.IsNullOrWhiteSpace(candidate.EditingName) ? null : candidate.EditingName!.Trim();
        var beforeName = candidate.CopyName;
        // 編集状態は先に閉じる (保存中の View 再描画で TextBox にフォーカスが残らないように)
        candidate.IsEditing = false;
        candidate.EditingName = null;

        if (string.Equals(trimmed, beforeName, StringComparison.Ordinal))
            return; // 変更なしなら履歴に積まない

        var before = new UpdateImageCopyChanges
        {
            CopyName = beforeName,
            ClearCopyName = beforeName is null,
        };
        var after = new UpdateImageCopyChanges
        {
            CopyName = trimmed,
            ClearCopyName = trimmed is null,
        };
        var beforeLabel = string.IsNullOrWhiteSpace(beforeName) ? _loc[Terminology.VariantUnnamedKey] : beforeName;
        var afterLabel = string.IsNullOrWhiteSpace(trimmed) ? _loc[Terminology.VariantUnnamedKey] : trimmed;
        var description = _loc.Format("History_VariantRenameFmt", _loc[Terminology.VariantKey], beforeLabel, afterLabel);
        var command = new UpdateImageCopyCommand(_updateCopyUseCase, candidate.CopyId, before, after, description);

        var result = await _history.ExecuteAsync(command, ct);
        if (result.IsError)
        {
            Context.StatusMessage = string.Join(", ", result.Errors);
            return;
        }

        // 永続化が成功したので VM の表示も即時更新 (CopyDisplayName が再計算される)
        candidate.CopyName = trimmed;
        _messenger.Send(new CopyLibraryChangedMessage());
        LogVariantRenamed(_logger, candidate.CopyId);
    }

    /// <summary>
    /// 親 (<see cref="GridWorkspaceViewModel"/>) で SelectedCandidate / IsBusy が変動したときに呼び、
    /// 本 VM のコマンド CanExecute を再評価する。 観測 chain を双方向に貼ると循環するので、
    /// 親から明示的に通知してもらう pull モデル。
    /// </summary>
    public void NotifyContextChanged()
    {
        BeginCreateVariantCommand.NotifyCanExecuteChanged();
        DeleteSelectedCandidateCommand.NotifyCanExecuteChanged();
    }

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information, Message = "配置タブから新規バリアント作成: asset={AssetId} copy={CopyId}")]
    private static partial void LogVariantCreated(ILogger logger, Guid assetId, Guid copyId);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Information, Message = "配置タブからバリアント削除: copy={CopyId}")]
    private static partial void LogVariantDeleted(ILogger logger, Guid copyId);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Information, Message = "配置タブからバリアントをリネーム: copy={CopyId}")]
    private static partial void LogVariantRenamed(ILogger logger, Guid copyId);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Debug, Message = "配置タブからバリアントのリネームをキャンセル: copy={CopyId}")]
    private static partial void LogVariantRenameCanceled(ILogger logger, Guid copyId);
}

/// <summary>
/// <see cref="VariantManagerViewModel"/> が親 (<see cref="GridWorkspaceViewModel"/>) から借りるコンテキスト。
/// 候補リスト (Candidates / CandidateGroups / SelectedCandidate) と全体ステータス (IsBusy / StatusMessage)、
/// および「候補リストを再ロード」 する非同期操作を露出する。
/// </summary>
public interface IVariantManagerContext
{
    /// <summary>現在選択中の候補バリアント。 削除 / 作成後に変更される。</summary>
    CopyCandidateViewModel? SelectedCandidate { get; set; }

    /// <summary>候補バリアントのフラットリスト (TreeView と候補管理ロジックで共有)。</summary>
    ObservableCollection<CopyCandidateViewModel> Candidates { get; }

    /// <summary>Asset 単位の候補グループ (TreeView root)。 削除時の整合性維持で参照する。</summary>
    ObservableCollection<CandidateGroupViewModel> CandidateGroups { get; }

    /// <summary>グローバル busy フラグ。 バリアント作成 / 削除中に true。</summary>
    bool IsBusy { get; set; }

    /// <summary>グローバルステータスバー表示用メッセージ。 ローカライズ済み文字列または <c>null</c>。</summary>
    string? StatusMessage { get; set; }

    /// <summary>候補リストを DB から再ロードする (CommitCreateVariantAsync 後に呼ぶ)。</summary>
    Task LoadCandidatesAsync(CancellationToken ct = default);
}

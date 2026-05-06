using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// ワークスペース切替ダイアログの ViewModel。 一覧取得 + 選択保持 + 切替実行に加えて、
/// Phase 1 の最小管理機能 (作成 / リネーム / 削除) も同じ画面で提供する。
/// 「再起動して切替」 押下時に <see cref="ApplyAsync"/> を呼び、 ダイアログ側で
/// <c>active.json</c> 書き換え後にプロセス再起動する。
/// </summary>
public sealed partial class WorkspaceSwitchDialogViewModel : ViewModelBase
{
    private readonly IWorkspaceManager _manager;
    private readonly IWorkspaceContext _context;

    public ObservableCollection<WorkspaceItem> Workspaces { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelected))]
    [NotifyPropertyChangedFor(nameof(IsActiveSelected))]
    [NotifyPropertyChangedFor(nameof(EditingDisplayName))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedEditor))]
    public partial WorkspaceItem? SelectedWorkspace { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelected))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string? StatusMessage { get; set; }

    /// <summary>新規作成フォームを開いている間 true。 ボタン押下で開閉する。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedEditor))]
    public partial bool IsCreating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial string DraftName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    public partial string DraftDisplayName { get; set; } = string.Empty;

    /// <summary>選択中ワークスペースの DisplayName 編集バッファ (リネーム用)。</summary>
    [ObservableProperty]
    public partial string EditingDisplayName { get; set; } = string.Empty;

    /// <summary>現在のアクティブワークスペース名 (起動時に解決済み)。</summary>
    public string ActiveWorkspaceName => _context.ActiveWorkspaceName;

    /// <summary>切替可能 (= 現在 active と異なる選択 + 非ビジー)。</summary>
    public bool CanApply
        => !IsBusy
        && SelectedWorkspace is { } sel
        && !string.Equals(sel.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase);

    /// <summary>選択中が active のときは削除を禁止する (使用中の DB を消すため)。</summary>
    public bool IsActiveSelected => SelectedWorkspace is { } sel
        && string.Equals(sel.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase);

    public bool CanDeleteSelected => !IsBusy && SelectedWorkspace is not null && !IsActiveSelected;

    public bool CanCreate => !IsBusy
        && IsCreating
        && !string.IsNullOrWhiteSpace(DraftName)
        && !string.IsNullOrWhiteSpace(DraftDisplayName);

    /// <summary>
    /// 選択中ワークスペースのリネーム / 削除カードを表示するか。 新規作成中は文脈を切り分けるため
    /// 隠して、 一覧 → 新規作成だけが見える状態にする。
    /// </summary>
    public bool ShowSelectedEditor => SelectedWorkspace is not null && !IsCreating;

    public WorkspaceSwitchDialogViewModel(IWorkspaceManager manager, IWorkspaceContext context)
    {
        _manager = manager;
        _context = context;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            IsBusy = true;
            await ReloadAsync(ct);
            SelectedWorkspace = Workspaces
                .FirstOrDefault(w => string.Equals(w.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 選択中のワークスペースを <c>active.json</c> に書き込む。 戻り値は成功なら選んだワークスペース名、
    /// 失敗なら <c>null</c>。 呼び出し元はこの結果を見てプロセス再起動を行う。
    /// </summary>
    public async Task<string?> ApplyAsync(CancellationToken ct = default)
    {
        if (SelectedWorkspace is not { } sel) return null;
        try
        {
            IsBusy = true;
            StatusMessage = null;
            var result = await _manager.SetActiveAsync(sel.Name, ct);
            if (result.IsError)
            {
                StatusMessage = result.FirstError.Description;
                return null;
            }
            return sel.Name;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginCreate()
    {
        DraftName = string.Empty;
        DraftDisplayName = string.Empty;
        StatusMessage = null;
        IsCreating = true;
    }

    public void CancelCreate()
    {
        IsCreating = false;
        DraftName = string.Empty;
        DraftDisplayName = string.Empty;
    }

    public async Task ConfirmCreateAsync(CancellationToken ct = default)
    {
        if (!CanCreate) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;
            var result = await _manager.CreateAsync(DraftName.Trim(), DraftDisplayName.Trim(), ct);
            if (result.IsError)
            {
                StatusMessage = result.FirstError.Description;
                return;
            }
            await ReloadAsync(ct);
            SelectedWorkspace = Workspaces.FirstOrDefault(w => w.Name == result.Value.Name);
            IsCreating = false;
            DraftName = string.Empty;
            DraftDisplayName = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RenameSelectedAsync(CancellationToken ct = default)
    {
        if (SelectedWorkspace is not { } sel) return;
        var trimmed = (EditingDisplayName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed == sel.DisplayName) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;
            var result = await _manager.RenameAsync(sel.Name, trimmed, ct);
            if (result.IsError)
            {
                StatusMessage = result.FirstError.Description;
                return;
            }
            await ReloadAsync(ct);
            SelectedWorkspace = Workspaces.FirstOrDefault(w => w.Name == sel.Name);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedAsync(CancellationToken ct = default)
    {
        if (SelectedWorkspace is not { } sel) return;
        if (string.Equals(sel.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            IsBusy = true;
            StatusMessage = null;
            var result = await _manager.DeleteAsync(sel.Name, ct);
            if (result.IsError)
            {
                StatusMessage = result.FirstError.Description;
                return;
            }
            await ReloadAsync(ct);
            SelectedWorkspace = Workspaces
                .FirstOrDefault(w => string.Equals(w.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        var entries = await _manager.ListAsync(ct);
        Workspaces.Clear();
        foreach (var m in entries)
            Workspaces.Add(new WorkspaceItem(m, _context.ActiveWorkspaceName));
    }

    partial void OnSelectedWorkspaceChanged(WorkspaceItem? value)
    {
        EditingDisplayName = value?.DisplayName ?? string.Empty;
    }

    /// <summary>ListBox の各行で表示する 1 ワークスペース。</summary>
    public sealed class WorkspaceItem
    {
        public WorkspaceItem(WorkspaceManifest manifest, string activeWorkspaceName)
        {
            Name = manifest.Name;
            DisplayName = manifest.DisplayName;
            IsActive = string.Equals(Name, activeWorkspaceName, StringComparison.OrdinalIgnoreCase);
        }

        public string Name { get; }
        public string DisplayName { get; }
        public bool IsActive { get; }

        /// <summary>ListBox の表示文字列。 内部名と表示名が同じならそのまま、 違えば「表示名 (Name)」を返す。</summary>
        public string Label => string.Equals(DisplayName, Name, StringComparison.Ordinal)
            ? DisplayName
            : $"{DisplayName} ({Name})";

        public override string ToString() => Label;
    }
}

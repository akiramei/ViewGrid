using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Application.ViewModels;

/// <summary>
/// ワークスペース切替ダイアログの ViewModel。 一覧取得 + 選択保持 + 切替実行を担う。
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
    public partial WorkspaceItem? SelectedWorkspace { get; set; }

    [ObservableProperty] public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string? StatusMessage { get; set; }

    /// <summary>現在のアクティブワークスペース名 (起動時に解決済み)。</summary>
    public string ActiveWorkspaceName => _context.ActiveWorkspaceName;

    /// <summary>切替可能 (= 現在 active と異なる選択 + 非ビジー)。</summary>
    public bool CanApply
        => !IsBusy
        && SelectedWorkspace is { } sel
        && !string.Equals(sel.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase);

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
            var entries = await _manager.ListAsync(ct);
            Workspaces.Clear();
            foreach (var m in entries)
                Workspaces.Add(new WorkspaceItem(m));
            SelectedWorkspace = Workspaces
                .FirstOrDefault(w => string.Equals(w.Name, _context.ActiveWorkspaceName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanApply));
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

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanApply));

    /// <summary>ComboBox の各行で表示する 1 ワークスペース。</summary>
    public sealed class WorkspaceItem
    {
        public WorkspaceItem(WorkspaceManifest manifest)
        {
            Name = manifest.Name;
            DisplayName = manifest.DisplayName;
        }

        public string Name { get; }
        public string DisplayName { get; }

        /// <summary>ComboBox の表示文字列。 内部名と表示名が同じならそのまま、 違えば「表示名 (Name)」を返す。</summary>
        public string Label => string.Equals(DisplayName, Name, StringComparison.Ordinal)
            ? DisplayName
            : $"{DisplayName} ({Name})";

        public override string ToString() => Label;
    }
}

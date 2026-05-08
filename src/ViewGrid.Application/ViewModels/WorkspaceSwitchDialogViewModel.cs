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
    [NotifyPropertyChangedFor(nameof(CanExportSelected))]
    [NotifyPropertyChangedFor(nameof(IsActiveSelected))]
    [NotifyPropertyChangedFor(nameof(EditingDisplayName))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedEditor))]
    public partial WorkspaceItem? SelectedWorkspace { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelected))]
    [NotifyPropertyChangedFor(nameof(CanExportSelected))]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty] public partial string? StatusMessage { get; set; }

    /// <summary>新規作成 / 複製フォームを開いている間 true。 ボタン押下で開閉する。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate))]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedEditor))]
    public partial bool IsCreating { get; set; }

    /// <summary>
    /// 複製モードのとき複製元ワークスペース名。 通常の新規空作成では <c>null</c>。
    /// <see cref="ConfirmCreateAsync"/> はこの値で <c>CreateAsync</c> と <c>DuplicateAsync</c> を
    /// 切り替える。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDuplicateMode))]
    [NotifyPropertyChangedFor(nameof(CreateCardTitle))]
    [NotifyPropertyChangedFor(nameof(CreateConfirmLabel))]
    public partial string? DuplicateSourceName { get; set; }

    public bool IsDuplicateMode => !string.IsNullOrEmpty(DuplicateSourceName);

    /// <summary>
    /// インポートモードのとき、 取り込み元 zip の絶対パス。 通常の新規空作成 / 複製では <c>null</c>。
    /// <see cref="ConfirmCreateAsync"/> はこの値で <c>ImportAsync</c> と <c>CreateAsync</c> /
    /// <c>DuplicateAsync</c> を切り替える。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportMode))]
    [NotifyPropertyChangedFor(nameof(CreateCardTitle))]
    [NotifyPropertyChangedFor(nameof(CreateConfirmLabel))]
    public partial string? ImportSourceZipPath { get; set; }

    public bool IsImportMode => !string.IsNullOrEmpty(ImportSourceZipPath);

    /// <summary>新規作成カードのヘッダ文言。 複製 / インポートモードでは別文言になる。</summary>
    public string CreateCardTitle => IsDuplicateMode
        ? $"「{DuplicateSourceName}」 を複製"
        : IsImportMode
            ? "zip からインポート"
            : "新しいワークスペースを作成";

    /// <summary>確定ボタンのラベル。 複製 / インポートモードでは別文言になる。</summary>
    public string CreateConfirmLabel => IsDuplicateMode
        ? "複製"
        : IsImportMode
            ? "インポート"
            : "作成";

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

    /// <summary>選択中ワークスペースをエクスポート可能か (active も可、 ビジー中は不可)。</summary>
    public bool CanExportSelected => !IsBusy && SelectedWorkspace is not null;

    /// <summary>新しい zip をインポート可能か (作成 / 複製 / 別インポート進行中・ビジー中は不可)。</summary>
    public bool CanImport => !IsBusy && !IsCreating;

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
        DuplicateSourceName = null;
        ImportSourceZipPath = null;
        DraftName = string.Empty;
        DraftDisplayName = string.Empty;
        StatusMessage = null;
        IsCreating = true;
    }

    /// <summary>
    /// 選択中のワークスペースを複製するモードで作成カードを開く。
    /// Draft の初期値は「&lt;source&gt;-copy」 / 「&lt;displayName&gt; (コピー)」 を入れて編集しやすくする。
    /// </summary>
    public void BeginDuplicate()
    {
        if (SelectedWorkspace is not { } source) return;
        DuplicateSourceName = source.Name;
        ImportSourceZipPath = null;
        DraftName = ProposeCopyName(source.Name);
        DraftDisplayName = $"{source.DisplayName} (コピー)";
        StatusMessage = null;
        IsCreating = true;
    }

    /// <summary>
    /// 選択した zip からインポートするモードで作成カードを開く。 zip メタデータがあれば
    /// 内部名 / 表示名のデフォルト値に使い、 既存と衝突する場合は「&lt;name&gt;-imported」
    /// 連番に置き換える。 メタデータが取れないときは zip ファイル名から推測する。
    /// </summary>
    public async Task BeginImportAsync(string zipPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(zipPath)) return;

        var info = await _manager.PeekExportInfoAsync(zipPath, ct);
        var fallbackName = SanitizeName(Path.GetFileNameWithoutExtension(zipPath));
        var nameFromZip = info?.Name ?? fallbackName;
        var displayFromZip = info?.DisplayName ?? fallbackName;

        DuplicateSourceName = null;
        ImportSourceZipPath = zipPath;
        DraftName = ProposeImportName(nameFromZip);
        DraftDisplayName = displayFromZip;
        StatusMessage = null;
        IsCreating = true;
    }

    public void CancelCreate()
    {
        IsCreating = false;
        DuplicateSourceName = null;
        ImportSourceZipPath = null;
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
            var newName = DraftName.Trim();
            var newDisplay = DraftDisplayName.Trim();

            ErrorOr.ErrorOr<WorkspaceManifest> result;
            if (DuplicateSourceName is { } src)
            {
                StatusMessage = "複製中... ワークスペースのサイズによっては時間がかかります。";
                result = await _manager.DuplicateAsync(src, newName, newDisplay, ct);
            }
            else if (ImportSourceZipPath is { } zip)
            {
                StatusMessage = "インポート中... ワークスペースのサイズによっては時間がかかります。";
                result = await _manager.ImportAsync(zip, newName, newDisplay, ct);
            }
            else
            {
                result = await _manager.CreateAsync(newName, newDisplay, ct);
            }

            if (result.IsError)
            {
                StatusMessage = result.FirstError.Description;
                return;
            }
            StatusMessage = null;
            await ReloadAsync(ct);
            SelectedWorkspace = Workspaces.FirstOrDefault(w => w.Name == result.Value.Name);
            IsCreating = false;
            DuplicateSourceName = null;
            ImportSourceZipPath = null;
            DraftName = string.Empty;
            DraftDisplayName = string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 選択中ワークスペースを zip にエクスポートする。 サイズが大きいと時間がかかる旨を
    /// StatusMessage に出しつつ、 完了時に成功 / 失敗メッセージを残す。
    /// </summary>
    public async Task ExportSelectedAsync(string destinationZipPath, CancellationToken ct = default)
    {
        if (SelectedWorkspace is not { } sel) return;
        if (string.IsNullOrWhiteSpace(destinationZipPath)) return;

        try
        {
            IsBusy = true;
            StatusMessage = "エクスポート中... ワークスペースのサイズによっては時間がかかります。";
            var result = await _manager.ExportAsync(sel.Name, destinationZipPath, ct);
            StatusMessage = result.IsError
                ? result.FirstError.Description
                : $"エクスポートしました: {destinationZipPath}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 「source」 → 「source-copy」 「source-copy-2」 等の名前候補を生成する。
    /// 既存ワークスペースと衝突しない最初の候補を返す。
    /// </summary>
    private string ProposeCopyName(string sourceName)
    {
        var existing = Workspaces.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseCandidate = $"{sourceName}-copy";
        if (!existing.Contains(baseCandidate)) return baseCandidate;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{sourceName}-copy-{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
        return baseCandidate;
    }

    /// <summary>
    /// インポート時の名前候補。 メタデータの内部名が既存と衝突しなければそのまま、
    /// 衝突する場合は「&lt;name&gt;-imported」 / 「&lt;name&gt;-imported-2」 ...と連番。
    /// </summary>
    private string ProposeImportName(string sourceName)
    {
        var existing = Workspaces.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(sourceName)) return sourceName;
        var baseCandidate = $"{sourceName}-imported";
        if (!existing.Contains(baseCandidate)) return baseCandidate;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{sourceName}-imported-{i}";
            if (!existing.Contains(candidate)) return candidate;
        }
        return baseCandidate;
    }

    /// <summary>
    /// zip ファイル名から内部名を作るときの正規化。 FS 互換のため英数 / ハイフン /
    /// アンダースコア以外の文字をハイフンに置換し、 連続ハイフンを 1 つにまとめる。
    /// 空になる場合は <c>"imported"</c> をデフォルトに使う。
    /// </summary>
    private static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "imported";
        var normalized = new string(raw.Select(c => IsAllowedNameChar(c) ? c : '-').ToArray());
        var segments = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).DefaultIfEmpty("imported");
        var collapsed = string.Join('-', segments);
        return collapsed.Length > 64 ? collapsed[..64] : collapsed;
    }

    private static bool IsAllowedNameChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';

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

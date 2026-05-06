using ViewGrid.Core.Services;

namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// <see cref="IWorkspaceContext"/> の標準実装。 起動時に
/// <c>Program.Main</c> で解決された不変パスを保持する。
/// </summary>
internal sealed class WorkspaceContext : IWorkspaceContext
{
    public WorkspaceContext(string rootDirectory, string workspaceDirectory, string activeWorkspaceName)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootDirectory);
        ArgumentException.ThrowIfNullOrEmpty(workspaceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(activeWorkspaceName);
        RootDirectory = rootDirectory;
        WorkspaceDirectory = workspaceDirectory;
        ActiveWorkspaceName = activeWorkspaceName;
    }

    public string RootDirectory { get; }
    public string WorkspaceDirectory { get; }
    public string ActiveWorkspaceName { get; }
}

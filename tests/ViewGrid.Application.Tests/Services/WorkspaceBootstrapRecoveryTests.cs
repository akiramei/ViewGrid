using System.Text.Json;
using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Infrastructure.Services;

namespace ViewGrid.Application.Tests.Services;

/// <summary>
/// 起動時のワークスペース解決 (<see cref="WorkspaceBootstrap.ResolveWithRecovery"/>)。
/// 選択中のワークスペースのデータが移動・削除されていたとき、 同名の空フォルダを黙って作り直さず、
/// 実在する別のワークスペースへ移り、 欠落を呼び出し側へ伝える (文書 7.x の「存在しない → 先頭をアクティブに」)。
/// </summary>
public sealed class WorkspaceBootstrapRecoveryTests : IDisposable
{
    private readonly DirectoryInfo _root = TestImageFactory.CreateTempDirectory();

    public void Dispose()
    {
        if (_root.Exists) _root.Delete(recursive: true);
    }

    private string WorkspaceDir(string name) => Path.Combine(_root.FullName, "workspaces", name);

    private void WriteActive(string name) =>
        File.WriteAllText(Path.Combine(_root.FullName, "active.json"), JsonSerializer.Serialize(new { Name = name }));

    private void WriteManifests(params string[] names) =>
        File.WriteAllText(
            Path.Combine(_root.FullName, "workspaces.json"),
            JsonSerializer.Serialize(names.Select(n => new { Name = n, DisplayName = n }).ToList()));

    private string ActiveOnDisk() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(_root.FullName, "active.json")))
            .RootElement.GetProperty("Name").GetString()!;

    [Fact]
    public void First_Run_Creates_Default_Without_Reporting_A_Missing_Workspace()
    {
        var r = WorkspaceBootstrap.ResolveWithRecovery(_root.FullName, null);

        r.ActiveName.Should().Be("Default");
        r.MissingWorkspaceName.Should().BeNull("初回起動は欠落ではない");
        Directory.Exists(WorkspaceDir("Default")).Should().BeTrue();
    }

    [Fact]
    public void Existing_Active_Workspace_Is_Opened_As_Is()
    {
        Directory.CreateDirectory(WorkspaceDir("work"));
        WriteActive("work");
        WriteManifests("Default", "work");

        var r = WorkspaceBootstrap.ResolveWithRecovery(_root.FullName, null);

        r.ActiveName.Should().Be("work");
        r.MissingWorkspaceName.Should().BeNull();
    }

    [Fact]
    public void Missing_Active_Workspace_Falls_Back_To_The_First_Existing_One_And_Reports_It()
    {
        Directory.CreateDirectory(WorkspaceDir("Default"));
        Directory.CreateDirectory(WorkspaceDir("hobby"));
        WriteActive("work"); // work のフォルダは移動されて無い
        WriteManifests("work", "hobby", "Default");

        var r = WorkspaceBootstrap.ResolveWithRecovery(_root.FullName, null);

        r.ActiveName.Should().Be("hobby", "登録順で、 ディレクトリが実在する最初のもの");
        r.MissingWorkspaceName.Should().Be("work");
        Directory.Exists(WorkspaceDir("work")).Should().BeFalse("元の名前の空フォルダを黙って作り直さない");
        ActiveOnDisk().Should().Be("hobby", "次回起動も同じ解決になるよう active.json を更新する");
    }

    [Fact]
    public void Missing_Active_Workspace_With_Nothing_Else_Is_Recreated_Empty_But_Reported()
    {
        WriteActive("work");
        WriteManifests("work");

        var r = WorkspaceBootstrap.ResolveWithRecovery(_root.FullName, null);

        r.ActiveName.Should().Be("work");
        r.MissingWorkspaceName.Should().Be("work", "空で作り直すが、 欠落を呼び出し側が利用者へ伝えられる");
        Directory.Exists(WorkspaceDir("work")).Should().BeTrue();
    }

    [Fact]
    public void Unregistered_Name_On_The_Command_Line_Is_Not_Reported_As_Missing_When_Nothing_Is_Registered()
    {
        // キャプチャモードなど、 一時ルートに新しい名前を渡す経路。
        var r = WorkspaceBootstrap.ResolveWithRecovery(_root.FullName, "capture-temp");

        r.ActiveName.Should().Be("capture-temp");
        r.MissingWorkspaceName.Should().BeNull();
        Directory.Exists(WorkspaceDir("capture-temp")).Should().BeTrue();
    }

    [Fact]
    public void Resolve_Tuple_Overload_Still_Returns_The_Resolved_Name_And_Directory()
    {
        Directory.CreateDirectory(WorkspaceDir("Default"));
        WriteActive("gone");
        WriteManifests("gone", "Default");

        var (name, dir) = WorkspaceBootstrap.Resolve(_root.FullName, null);

        name.Should().Be("Default");
        dir.Should().Be(WorkspaceDir("Default"));
    }
}

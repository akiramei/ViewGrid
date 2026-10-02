using FluentAssertions;
using ViewGrid.Application.Tests.TestSupport;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Services;

namespace ViewGrid.Application.Tests.ViewModels;

/// <summary>
/// 修正後ソースレビュー (2026-10-02、対象 ba5290b) の残件 R01〜R07 に対する回帰テスト。
/// 確認ダイアログの回答を後から返す (<see cref="DeferredConfirmationService"/>) など、 実際の操作順序で状態遷移を試す。
/// 自動保存・確認が即答する従来のテストでは、 確認の前後で起きる状態の食い違いを再現できなかった。
/// </summary>
public sealed class SourceReviewRegressionTests : IAsyncLifetime
{
    private readonly DeferredConfirmationService _confirm = new();
    private AppViewModelHarness _h = null!;

    private GridWorkspaceViewModel Vm => _h.Workspace;

    public async Task InitializeAsync() => _h = await AppViewModelHarness.CreateAsync(_confirm);

    public async Task DisposeAsync() => await _h.DisposeAsync();

    private async Task SelectPlacementAsync(PlacementItemViewModel? placement)
    {
        Vm.SelectedPlacement = placement;
        await Vm.WaitPendingInspectorAttachAsync();
        await Vm.WaitPendingVariantAttachAsync();
    }

    /// <summary>配置 A を選び、 A の共有特性に未保存の編集 (左右反転) を入れる。</summary>
    private async Task<(ImageCopy A, ImageCopy B)> SeedAndEditAsync(bool autoSave)
    {
        await _h.SetAutoSaveAsync(autoSave);
        var (_, a, b) = await _h.SeedTwoPlacementsAsync();
        await SelectPlacementAsync(Vm.Placements.Single(p => p.CopyId == a.Id));
        Vm.Inspector.CopyProperties.FlipX = true;
        return (a, b);
    }

    private async Task<bool> FlipXStoredAsync(Guid copyId) =>
        (await _h.Fx.CopyRepository.FindByIdAsync(copyId))!.Transform.FlipX;

    // ─── R01: 選択を戻しても編集対象が別のバリアントに残る ──────────────────────────

    [Fact]
    public async Task R01_Back_Then_Deselect_Keeps_The_Variant_Panel_On_The_Selected_Variant()
    {
        await _h.SetAutoSaveAsync(false);
        var (_, a, b) = await _h.SeedTwoPlacementsAsync();
        var placementA = Vm.Placements.Single(p => p.CopyId == a.Id);
        var placementB = Vm.Placements.Single(p => p.CopyId == b.Id);
        await SelectPlacementAsync(placementA);
        Vm.Inspector.CopyProperties.FlipX = true; // A の未保存の編集

        // B をクリック → 確認 (利用者が考えている間) → 「戻る」
        Vm.SelectedPlacement = placementB;
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Cancel);
        await Vm.WaitPendingInspectorAttachAsync();
        await Vm.WaitPendingVariantAttachAsync();
        Vm.SelectedPlacement.Should().BeSameAs(placementA);

        // Escape で選択解除 → 「破棄」
        Vm.SelectedPlacement = null;
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Discard);
        await Vm.WaitPendingInspectorAttachAsync();
        await Vm.WaitPendingVariantAttachAsync();

        // 表示している (選択中の) バリアントと、 編集パネルの接続先が一致する。
        Vm.SelectedCandidate.Should().NotBeNull();
        Vm.SelectedCandidate!.CopyId.Should().Be(a.Id);
        Vm.VariantProperties.AttachedCopyId.Should().Be(a.Id, "選択を戻したのに編集パネルが B に接続されたままになる");

        // この状態で編集・保存しても、 変わるのは選択中の A だけ。
        Vm.VariantProperties.FlipX = true;
        (await Vm.VariantProperties.TrySaveAsync()).Should().BeTrue();
        (await _h.Fx.CopyRepository.FindByIdAsync(a.Id))!.Transform.FlipX.Should().BeTrue();
        (await _h.Fx.CopyRepository.FindByIdAsync(b.Id))!.Transform.FlipX.Should().BeFalse("選択していない B を変更してはならない");
    }

    [Fact]
    public async Task R01_Candidate_Click_Then_Back_Keeps_The_Variant_Panel_On_The_Original_Variant()
    {
        await _h.SetAutoSaveAsync(false);
        var (_, a, b) = await _h.SeedTwoPlacementsAsync();
        await Vm.LoadCandidatesAsync();
        var placementA = Vm.Placements.Single(p => p.CopyId == a.Id);
        await SelectPlacementAsync(placementA);
        Vm.Inspector.CopyProperties.FlipX = true;

        // 候補リストで B を選ぶ → 配置の選択解除の確認 → 「戻る」
        Vm.SelectedCandidate = Vm.Candidates.Single(c => c.CopyId == b.Id);
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Cancel);
        await Vm.WaitPendingInspectorAttachAsync();
        await Vm.WaitPendingVariantAttachAsync();

        Vm.SelectedPlacement.Should().BeSameAs(placementA);
        Vm.SelectedCandidate!.CopyId.Should().Be(a.Id);
        Vm.VariantProperties.AttachedCopyId.Should().Be(a.Id);
        Vm.Inspector.CopyProperties.FlipX.Should().BeTrue("「戻る」 は A の未保存の編集をそのまま残す");
    }

    // ─── R02: 手動編集を残して閉じると確認前に画面が消える ─────────────────────────
    // 確認はウィンドウが開いているうち (Closing) に出す。 ここでは、 その判断 (PrepareToCloseAsync) を、
    // 回答が後から来る確認ダイアログで試す。

    [Fact]
    public async Task R02_Close_With_A_Manual_Edit_Asks_And_Save_Persists_Before_Closing()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: false);
        _h.Main.HasUnsavedManualEdits.Should().BeTrue();

        var closing = _h.Main.PrepareToCloseAsync();
        await _confirm.WaitForUnsavedRequestAsync();
        closing.IsCompleted.Should().BeFalse("確認の回答が来るまで閉じてはならない");

        _confirm.Answer(UnsavedChoice.Save);

        (await closing).Should().BeTrue();
        (await FlipXStoredAsync(a.Id)).Should().BeTrue("保存を選んだ編集は、 閉じる前に永続化されている");
    }

    [Fact]
    public async Task R02_Close_With_A_Manual_Edit_Discard_Closes_Without_Saving()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: false);

        var closing = _h.Main.PrepareToCloseAsync();
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Discard);

        (await closing).Should().BeTrue();
        (await FlipXStoredAsync(a.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task R02_Close_With_A_Manual_Edit_Back_Keeps_The_Window_And_The_Edit()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: false);

        var closing = _h.Main.PrepareToCloseAsync();
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Cancel);

        (await closing).Should().BeFalse("「戻る」 なら閉じない");
        _h.Main.HasUnsavedManualEdits.Should().BeTrue("編集できる画面と未保存の編集をそのまま残す");
        Vm.Inspector.CopyProperties.FlipX.Should().BeTrue();
        (await FlipXStoredAsync(a.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task R02_Repeated_Close_Requests_Share_One_Confirmation()
    {
        await SeedAndEditAsync(autoSave: false);

        var first = _h.Main.PrepareToCloseAsync();
        await _confirm.WaitForUnsavedRequestAsync();
        var second = _h.Main.PrepareToCloseAsync(); // 閉じるボタンの連打・メニュー終了との重なり
        _confirm.Answer(UnsavedChoice.Discard);

        (await first).Should().BeTrue();
        (await second).Should().BeTrue();
        _confirm.UnsavedRequests.Should().Be(1, "二重の終了要求も確認は 1 回");
    }

    [Fact]
    public async Task R02_Close_Without_Unsaved_Edits_Does_Not_Ask()
    {
        await _h.SetAutoSaveAsync(false);
        await _h.SeedTwoPlacementsAsync();

        (await _h.Main.PrepareToCloseAsync()).Should().BeTrue();
        _confirm.UnsavedRequests.Should().Be(0);
    }

    [Fact]
    public async Task R02_Close_With_An_Unsaved_Grid_Name_Asks_And_Save_Persists_It()
    {
        await _h.SetAutoSaveAsync(false);
        var grid = await _h.SeedGridAsync(2, 2, name: "before");
        await _h.GridList.LoadAsync();
        _h.GridList.SelectedGrid!.EditingName = "after";
        _h.Main.HasUnsavedManualEdits.Should().BeTrue();

        var closing = _h.Main.PrepareToCloseAsync();
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Save);

        (await closing).Should().BeTrue();
        (await _h.Fx.GridRepository.FindByIdAsync(grid.Id))!.Name.Should().Be("after");
    }

    // ─── R03: ワークスペース切替が自動保存完了を待たない ───────────────────────────

    [Fact]
    public async Task R03_Switch_Waits_For_A_Pending_AutoSave_And_Persists_It()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: true);
        Vm.Inspector.CopyProperties.IsDirty.Should().BeTrue("自動保存のデバウンス (1 秒) 内で、 まだ保存されていない");

        var preparation = await _h.Main.PrepareForWorkspaceSwitchAsync();

        preparation.Should().Be(ExitPreparation.Proceed);
        (await FlipXStoredAsync(a.Id)).Should().BeTrue("切替 (強制終了) の前に、 保留中の自動保存が完了している");
    }

    [Fact]
    public async Task R03_Switch_Waits_For_A_Save_That_Is_In_Flight()
    {
        await _h.SetAutoSaveAsync(true);
        var (_, a, _) = await _h.SeedTwoPlacementsAsync();
        await SelectPlacementAsync(Vm.Placements.Single(p => p.CopyId == a.Id));
        _h.History.Block();
        Vm.Inspector.CopyProperties.FlipX = true;
        await _h.History.WaitUntilBlockedAsync(); // デバウンス後の自動保存が始まり、 履歴コマンドの実行で止まっている

        var preparation = _h.Main.PrepareForWorkspaceSwitchAsync();
        await Task.Delay(200);
        preparation.IsCompleted.Should().BeFalse("保存中の間は切替へ進まない");

        _h.History.Release();

        (await preparation).Should().Be(ExitPreparation.Proceed);
        (await FlipXStoredAsync(a.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task R03_Switch_Is_Aborted_When_A_Pending_Edit_Cannot_Be_Saved()
    {
        await _h.SetAutoSaveAsync(true);
        await _h.SeedTwoPlacementsAsync();
        await SelectPlacementAsync(Vm.Placements.First());
        // 2x2 グリッドに 5x5 の占有は保存できない (検証エラー)
        Vm.Inspector.OccupyWidth = 5;
        Vm.Inspector.OccupyHeight = 5;

        var preparation = await _h.Main.PrepareForWorkspaceSwitchAsync();

        preparation.Should().Be(ExitPreparation.SaveFailed, "編集を失うので切替も再起動もしない");
    }

    [Fact]
    public async Task R03_Switch_With_A_Manual_Edit_Back_Does_Not_Switch()
    {
        await SeedAndEditAsync(autoSave: false);

        var preparation = _h.Main.PrepareForWorkspaceSwitchAsync();
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Cancel);

        (await preparation).Should().Be(ExitPreparation.Cancelled);
    }
}

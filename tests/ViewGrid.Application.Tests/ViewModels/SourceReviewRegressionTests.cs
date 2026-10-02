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

    // ─── 検証用の DB 読取り ──────────────────────────────────────────────────
    // アプリの DbContext は 1 本を全 VM が共有していて、 並行して使うと EF が「同時に 2 つの操作」 例外を投げる。
    // 保存のたびに非同期の後処理 (候補ライブラリ変更の再読込・選択切替の attach) が走るので、 保存・編集の直後に
    // テストが直接読むと、 その後処理と重なって失敗する。 読む前に、 後処理がすべて終わる同期点を挟む
    // (固定時間の待機や再試行ではなく、 完了を待つ)。 製品側の並行アクセスは隠さない:
    // 独立した DbContext は使わず、 EF の同時利用の検出もそのまま有効にしている。

    private async Task<ImageCopy?> ReadCopyAsync(Guid id)
    {
        await Vm.WaitForBackgroundWorkAsync();
        return await _h.Fx.CopyRepository.FindByIdAsync(id);
    }

    private async Task<GridCanvas?> ReadGridAsync(Guid id)
    {
        await Vm.WaitForBackgroundWorkAsync();
        return await _h.Fx.GridRepository.FindByIdAsync(id);
    }

    private async Task<IReadOnlyList<ImageCopy>> ReadAllCopiesAsync()
    {
        await Vm.WaitForBackgroundWorkAsync();
        return await _h.Fx.CopyRepository.FindAllAsync();
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
        (await ReadCopyAsync(copyId))!.Transform.FlipX;

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
        (await ReadCopyAsync(a.Id))!.Transform.FlipX.Should().BeTrue();
        (await ReadCopyAsync(b.Id))!.Transform.FlipX.Should().BeFalse("選択していない B を変更してはならない");
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
        (await ReadGridAsync(grid.Id))!.Name.Should().Be("after");
    }

    // ─── R05: 編集直後の複製が古い保存値を引き継ぐ ──────────────────────────────────

    /// <summary>候補リストから A を選び (配置の A はそのまま)、 「複製」 を実行して、 複製されたバリアントを返す。</summary>
    private async Task<ImageCopy?> DuplicateSelectedAsync(Guid sourceCopyId, bool waitForPrompt = false,
        UnsavedChoice? answer = null)
    {
        await Vm.LoadCandidatesAsync();
        Vm.SelectedCandidate = Vm.Candidates.Single(c => c.CopyId == sourceCopyId);
        var before = (await ReadAllCopiesAsync()).Select(c => c.Id).ToHashSet();

        var duplicating = Vm.Variants.DuplicateSelectedCandidateAsync();
        if (waitForPrompt)
        {
            await _confirm.WaitForUnsavedRequestAsync();
            _confirm.Answer(answer!.Value);
        }
        await duplicating;

        var after = await ReadAllCopiesAsync();
        return after.SingleOrDefault(c => !before.Contains(c.Id));
    }

    [Fact]
    public async Task R05_Duplicate_Right_After_An_Edit_Carries_The_Edit_With_AutoSave()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: true);
        (await FlipXStoredAsync(a.Id)).Should().BeFalse("デバウンス内で、 まだ保存されていない");

        var duplicate = await DuplicateSelectedAsync(a.Id);

        duplicate.Should().NotBeNull();
        duplicate!.Transform.FlipX.Should().BeTrue("画面で変えた設定を引き継ぐ (保存済みの古い値ではない)");
        (await FlipXStoredAsync(a.Id)).Should().BeTrue("元の案にも保存される (後から元だけが保存されて食い違わない)");
    }

    [Fact]
    public async Task R05_Duplicate_With_A_Manual_Edit_Save_Carries_The_Edit()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: false);

        var duplicate = await DuplicateSelectedAsync(a.Id, waitForPrompt: true, UnsavedChoice.Save);

        duplicate.Should().NotBeNull();
        duplicate!.Transform.FlipX.Should().BeTrue();
        (await FlipXStoredAsync(a.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task R05_Duplicate_With_A_Manual_Edit_Discard_Copies_The_Saved_Values()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: false);

        var duplicate = await DuplicateSelectedAsync(a.Id, waitForPrompt: true, UnsavedChoice.Discard);

        duplicate.Should().NotBeNull();
        duplicate!.Transform.FlipX.Should().BeFalse("破棄した編集は引き継がない");
        (await FlipXStoredAsync(a.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task R05_Duplicate_With_A_Manual_Edit_Back_Creates_Nothing_And_Keeps_The_Edit()
    {
        var (a, _) = await SeedAndEditAsync(autoSave: false);

        var duplicate = await DuplicateSelectedAsync(a.Id, waitForPrompt: true, UnsavedChoice.Cancel);

        duplicate.Should().BeNull("「戻る」 なら複製しない");
        Vm.Inspector.CopyProperties.FlipX.Should().BeTrue();
        Vm.Inspector.CopyProperties.IsDirty.Should().BeTrue();
    }

    [Fact]
    public async Task R05_Duplicate_Of_A_Standalone_Variant_Edit_Carries_The_Edit()
    {
        await _h.SetAutoSaveAsync(true);
        var asset = await _h.Fx.SeedAssetAsync();
        var copy = await _h.Fx.SeedCopyAsync(asset.Id, "solo");
        await Vm.LoadCandidatesAsync();
        Vm.SelectedCandidate = Vm.Candidates.Single(c => c.CopyId == copy.Id);
        await Vm.WaitPendingVariantAttachAsync();
        Vm.VariantProperties.FlipX = true; // 候補単体編集 (配置なし)

        var duplicate = await DuplicateSelectedAsync(copy.Id);

        duplicate.Should().NotBeNull();
        duplicate!.Transform.FlipX.Should().BeTrue();
    }

    // ─── R07: 保存待ち中の追加編集まで保存済みにしてしまう ─────────────────────────

    [Fact]
    public async Task R07_An_Edit_Made_While_A_Save_Is_In_Flight_Stays_Unsaved_And_Is_Saved_Next()
    {
        await SeedAndEditAsync(autoSave: false); // FlipX=true の未保存編集 (手動保存)
        var cp = Vm.Inspector.CopyProperties;
        var copyId = cp.AttachedCopyId!.Value;
        _h.History.Block();

        var saving = cp.TrySaveAsync();        // FlipX=true の保存を開始 → 履歴コマンドの実行で止まる
        await _h.History.WaitUntilBlockedAsync();
        cp.FlipY = true;                       // 保存の最中に追加の編集
        _h.History.Release();

        (await saving).Should().BeTrue();
        (await FlipXStoredAsync(copyId)).Should().BeTrue("保存開始時点の内容は保存された");
        cp.IsDirty.Should().BeTrue("保存の最中に入力した FlipY はまだ保存されていない。 保存済みと見なしてはならない");
        cp.FlipY.Should().BeTrue("画面の入力はそのまま残る");

        (await cp.TrySaveAsync()).Should().BeTrue();   // 次の保存 (明示保存または自動保存) で届く
        (await ReadCopyAsync(copyId))!.Transform.FlipY.Should().BeTrue();
        cp.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task R07_A_Late_Save_Completion_Does_Not_Clear_The_State_Of_The_Newly_Attached_Variant()
    {
        // 候補単体編集 (配置なし): バリアント A の保存の最中に候補 B へ切り替え、 B に編集を入れる。
        await _h.SetAutoSaveAsync(false);
        var asset = await _h.Fx.SeedAssetAsync();
        var a = await _h.Fx.SeedCopyAsync(asset.Id, "A");
        var b = await _h.Fx.SeedCopyAsync(asset.Id, "B");
        await Vm.LoadCandidatesAsync();
        Vm.SelectedCandidate = Vm.Candidates.Single(c => c.CopyId == a.Id);
        await Vm.WaitPendingVariantAttachAsync();
        var vp = Vm.VariantProperties;
        vp.FlipX = true;
        _h.History.Block();

        var saving = vp.TrySaveAsync();
        await _h.History.WaitUntilBlockedAsync();
        Vm.SelectedCandidate = Vm.Candidates.Single(c => c.CopyId == b.Id);
        await _confirm.WaitForUnsavedRequestAsync();
        _confirm.Answer(UnsavedChoice.Discard);
        await Vm.WaitPendingVariantAttachAsync();
        vp.AttachedCopyId.Should().Be(b.Id);
        vp.FlipY = true;
        _h.History.Release();
        await saving;

        vp.AttachedCopyId.Should().Be(b.Id);
        vp.IsDirty.Should().BeTrue("古い保存の完了が、 新しい対象の未保存状態を消してはならない");
        vp.FlipY.Should().BeTrue();
        (await FlipXStoredAsync(a.Id)).Should().BeTrue("A の保存自体は完了している");
    }

    [Fact]
    public async Task R07_With_AutoSave_The_Edit_Made_During_A_Save_Reaches_The_Database()
    {
        await _h.SetAutoSaveAsync(true);
        var (_, a, _) = await _h.SeedTwoPlacementsAsync();
        await SelectPlacementAsync(Vm.Placements.Single(p => p.CopyId == a.Id));
        var cp = Vm.Inspector.CopyProperties;
        _h.History.Block();

        cp.FlipX = true;                         // デバウンス後に自動保存が始まり、 履歴コマンドで止まる
        await _h.History.WaitUntilBlockedAsync();
        cp.FlipY = true;                         // 保存の最中の追加入力 (自動保存を再予約する)
        _h.History.Release();

        // 保存が終わる (= 未保存フラグが消える) のを、 DB を触らずに待つ。 VM の保存と同じ DbContext を
        // テスト側が並行して読むと、 EF の「同時に 2 つの操作」 例外で VM の保存の方が失敗してしまう。
        var until = Environment.TickCount64 + 8000;
        while (Environment.TickCount64 < until && cp.IsDirty)
            await Task.Delay(50);

        // 判定は強制保存 (FlushAllPendingEditsAsync) より前に行う。 強制保存は未保存の編集を自分で保存してしまうので、
        // 先に呼ぶと、 保存の最中の追加入力に対して自動保存が再予約されなかった場合でも、 テストが成功してしまう
        // (検出力が落ちる)。 ここでは、 自動保存だけで未保存フラグが消えたことを確認する。
        cp.IsDirty.Should().BeFalse("最後の入力まで自動保存された (強制保存の助けなしに)");

        // 未保存フラグが消えた直後は、 保存の後処理 (再読込の通知) がまだ積まれる途中かもしれない。 保存の完了を待ってから、
        // 後処理の完了を待って読む (ここでの強制保存は、 同期のためだけ。 すでに未保存の編集は無い)。
        (await Vm.FlushAllPendingEditsAsync()).Should().BeTrue();
        var stored = (await ReadCopyAsync(a.Id))!;
        stored.Transform.FlipX.Should().BeTrue();
        stored.Transform.FlipY.Should().BeTrue("最後の入力が自動保存で DB へ届く");
    }

    // ─── R06: グリッド保存失敗後も古い値で出力を進める ──────────────────────────────

    private static int PngWidth(byte[] png) => (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];

    private async Task<GridCanvas> OpenGridThroughTheListAsync(string name)
    {
        await _h.SetAutoSaveAsync(true);
        var asset = await _h.Fx.SeedAssetAsync();
        var copy = await _h.Fx.SeedCopyAsync(asset.Id, "c");
        var grid = await _h.SeedGridAsync(2, 2, name: name, canvas: 400);
        await new Application.UseCases.PlaceImageCopyUseCase(_h.Fx.GridRepository, _h.Fx.CopyRepository, _h.Fx.PlacementRepository)
            .ExecuteAsync(grid.Id, copy.Id, new CellPosition(0, 0));
        await _h.GridList.LoadAsync();
        var until = Environment.TickCount64 + 5000;
        while (Vm.CurrentGrid is null && Environment.TickCount64 < until) await Task.Delay(20);
        Vm.CurrentGrid.Should().NotBeNull();
        await Vm.WaitPendingInspectorAttachAsync();
        return grid;
    }

    [Fact]
    public async Task R06_Preview_Is_Aborted_When_The_Grid_Edit_Cannot_Be_Saved_And_Resumes_After_Correction()
    {
        await OpenGridThroughTheListAsync("grid");
        var item = _h.GridList.SelectedGrid!;
        item.EditingName = "";          // 名前エラー (保存できない)
        item.EditingCanvasWidth = 800;  // 未保存のまま残る入力

        var aborted = await Vm.Output.RequestPreviewAsync();

        aborted.Should().BeNull("グリッドの保存に失敗しているのに、 保存済みの古い値 (400px) で出力を進めてはならない");
        Vm.StatusMessage.Should().Be("Status_OutputAbortedSaveFailed");
        Vm.IsBusy.Should().BeFalse();

        item.EditingName = "fixed"; // 訂正して再保存
        var resumed = await Vm.Output.RequestPreviewAsync();

        resumed.Should().NotBeNull();
        PngWidth(resumed!).Should().Be(800, "訂正・再保存後は 800px で出力する");
    }

    [Fact]
    public async Task R06_Pre_Output_Check_Reports_A_Grid_Save_Failure_To_Every_Caller()
    {
        await OpenGridThroughTheListAsync("grid");
        var item = _h.GridList.SelectedGrid!;
        item.EditingName = "";
        item.EditingCanvasWidth = 800;

        // PNG 出力・Undo / Redo の前処理も同じ「保留中の編集をすべて確定」 を使う。 グリッドの保存失敗が false で伝わる。
        (await Vm.FlushAllPendingEditsAsync()).Should().BeFalse();

        item.EditingName = "fixed";
        (await Vm.FlushAllPendingEditsAsync()).Should().BeTrue("訂正して保存できれば成功に戻る");
        (await ReadGridAsync(item.GridId))!.CanvasSize.Width.Should().Be(800);
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

using ErrorOr;
using FluentAssertions;
using ViewGrid.Application.History;
using Xunit;

namespace ViewGrid.Application.Tests.History;

public sealed class UndoRedoServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Pushes_To_Undo_Stack_And_Clears_Redo()
    {
        var svc = new UndoRedoService();
        var cmd = new RecordingCommand("op1");

        var result = await svc.ExecuteAsync(cmd);

        result.IsError.Should().BeFalse();
        cmd.ExecuteCount.Should().Be(1);
        svc.CanUndo.Should().BeTrue();
        svc.CanRedo.Should().BeFalse();
        svc.NextUndoDescription.Should().Be("op1");
    }

    [Fact]
    public async Task UndoAsync_Pops_From_Undo_To_Redo()
    {
        var svc = new UndoRedoService();
        var cmd = new RecordingCommand("op1");
        await svc.ExecuteAsync(cmd);

        var result = await svc.UndoAsync();

        result.IsError.Should().BeFalse();
        cmd.UndoCount.Should().Be(1);
        svc.CanUndo.Should().BeFalse();
        svc.CanRedo.Should().BeTrue();
        svc.NextRedoDescription.Should().Be("op1");
    }

    [Fact]
    public async Task RedoAsync_Reapplies_And_Moves_Back_To_Undo()
    {
        var svc = new UndoRedoService();
        var cmd = new RecordingCommand("op1");
        await svc.ExecuteAsync(cmd);
        await svc.UndoAsync();

        var result = await svc.RedoAsync();

        result.IsError.Should().BeFalse();
        cmd.ExecuteCount.Should().Be(2);
        svc.CanUndo.Should().BeTrue();
        svc.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_New_Operation_Clears_Redo_Stack()
    {
        var svc = new UndoRedoService();
        var a = new RecordingCommand("a");
        var b = new RecordingCommand("b");
        var c = new RecordingCommand("c");

        await svc.ExecuteAsync(a);
        await svc.ExecuteAsync(b);
        await svc.UndoAsync(); // b in redo
        svc.CanRedo.Should().BeTrue();

        await svc.ExecuteAsync(c);

        svc.CanRedo.Should().BeFalse();
        svc.NextUndoDescription.Should().Be("c");
    }

    [Fact]
    public async Task UndoAsync_With_Empty_Stack_Is_NoOp()
    {
        var svc = new UndoRedoService();

        var result = await svc.UndoAsync();

        result.IsError.Should().BeFalse();
        svc.CanUndo.Should().BeFalse();
        svc.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task RedoAsync_With_Empty_Stack_Is_NoOp()
    {
        var svc = new UndoRedoService();

        var result = await svc.RedoAsync();

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Caps_Stack_At_MaxStackSize()
    {
        var svc = new UndoRedoService();
        var commands = new RecordingCommand[UndoRedoService.MaxStackSize + 5];
        for (var i = 0; i < commands.Length; i++)
        {
            commands[i] = new RecordingCommand($"op{i}");
            await svc.ExecuteAsync(commands[i]);
        }

        // 古い 5 件が破棄され、最新 50 件のみ残る。
        var undoCount = 0;
        while (svc.CanUndo)
        {
            await svc.UndoAsync();
            undoCount++;
        }
        undoCount.Should().Be(UndoRedoService.MaxStackSize);

        // 破棄された古い 5 件は Undo されない。
        for (var i = 0; i < 5; i++)
            commands[i].UndoCount.Should().Be(0);
        // 残りは全て 1 回ずつ Undo されている。
        for (var i = 5; i < commands.Length; i++)
            commands[i].UndoCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Error_Without_Pushing()
    {
        var svc = new UndoRedoService();
        var cmd = new RecordingCommand("fail", failOnExecute: true);

        var result = await svc.ExecuteAsync(cmd);

        result.IsError.Should().BeTrue();
        svc.CanUndo.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_Failure_Clears_All_Stacks()
    {
        var svc = new UndoRedoService();
        var ok = new RecordingCommand("ok");
        var bad = new RecordingCommand("bad", failOnUndo: true);

        await svc.ExecuteAsync(ok);
        await svc.ExecuteAsync(bad);

        var result = await svc.UndoAsync();

        result.IsError.Should().BeTrue();
        svc.CanUndo.Should().BeFalse();
        svc.CanRedo.Should().BeFalse();
    }

    [Fact]
    public async Task Clear_Empties_Both_Stacks_And_Notifies()
    {
        var svc = new UndoRedoService();
        var notifyCount = 0;
        svc.StateChanged += () => notifyCount++;

        await svc.ExecuteAsync(new RecordingCommand("a"));
        await svc.ExecuteAsync(new RecordingCommand("b"));
        await svc.UndoAsync();
        var beforeClear = notifyCount;

        svc.Clear();

        svc.CanUndo.Should().BeFalse();
        svc.CanRedo.Should().BeFalse();
        notifyCount.Should().BeGreaterThan(beforeClear);
    }

    [Fact]
    public void Clear_On_Empty_Does_Not_Notify()
    {
        var svc = new UndoRedoService();
        var notifyCount = 0;
        svc.StateChanged += () => notifyCount++;

        svc.Clear();

        notifyCount.Should().Be(0);
    }

    [Fact]
    public async Task StateChanged_Fires_On_Execute_Undo_Redo()
    {
        var svc = new UndoRedoService();
        var notifyCount = 0;
        svc.StateChanged += () => notifyCount++;

        await svc.ExecuteAsync(new RecordingCommand("a"));
        await svc.UndoAsync();
        await svc.RedoAsync();

        notifyCount.Should().Be(3);
    }

    [Fact]
    public void History_Returns_Empty_When_Stacks_Are_Empty()
    {
        var svc = new UndoRedoService();
        svc.History.Should().BeEmpty();
        svc.CurrentIndex.Should().Be(-1);
    }

    [Fact]
    public async Task History_Returns_Applied_And_Unapplied_In_Chronological_Order()
    {
        var svc = new UndoRedoService();
        await svc.ExecuteAsync(new RecordingCommand("a"));
        await svc.ExecuteAsync(new RecordingCommand("b"));
        await svc.ExecuteAsync(new RecordingCommand("c"));
        await svc.UndoAsync(); // c を取消
        await svc.UndoAsync(); // b を取消

        var history = svc.History;
        history.Should().HaveCount(3);
        history[0].Description.Should().Be("a");
        history[0].IsApplied.Should().BeTrue();
        history[1].Description.Should().Be("b");
        history[1].IsApplied.Should().BeFalse();
        history[2].Description.Should().Be("c");
        history[2].IsApplied.Should().BeFalse();
        // Index は 0 始まり連番
        history[0].Index.Should().Be(0);
        history[1].Index.Should().Be(1);
        history[2].Index.Should().Be(2);
    }

    [Fact]
    public async Task History_Marks_Current_With_IsCurrent_Flag()
    {
        var svc = new UndoRedoService();
        await svc.ExecuteAsync(new RecordingCommand("a"));
        await svc.ExecuteAsync(new RecordingCommand("b"));
        await svc.ExecuteAsync(new RecordingCommand("c"));

        var history = svc.History;
        history.Where(e => e.IsCurrent).Should().ContainSingle()
            .Which.Description.Should().Be("c");
    }

    [Fact]
    public async Task CurrentIndex_Reflects_Stack_State()
    {
        var svc = new UndoRedoService();
        svc.CurrentIndex.Should().Be(-1);

        await svc.ExecuteAsync(new RecordingCommand("a"));
        svc.CurrentIndex.Should().Be(0);

        await svc.ExecuteAsync(new RecordingCommand("b"));
        svc.CurrentIndex.Should().Be(1);

        await svc.UndoAsync();
        svc.CurrentIndex.Should().Be(0);

        await svc.UndoAsync();
        svc.CurrentIndex.Should().Be(-1);
    }

    [Fact]
    public async Task JumpToAsync_Forward_Redoes_Multiple()
    {
        var svc = new UndoRedoService();
        var a = new RecordingCommand("a");
        var b = new RecordingCommand("b");
        var c = new RecordingCommand("c");
        await svc.ExecuteAsync(a);
        await svc.ExecuteAsync(b);
        await svc.ExecuteAsync(c);
        await svc.UndoAsync(); // CurrentIndex=1
        await svc.UndoAsync(); // CurrentIndex=0
        await svc.UndoAsync(); // CurrentIndex=-1

        // Index=2 へジャンプ → a, b, c が順に再適用される
        var result = await svc.JumpToAsync(2);

        result.IsError.Should().BeFalse();
        svc.CurrentIndex.Should().Be(2);
        a.ExecuteCount.Should().Be(2); // 初回 + Redo 1 回
        b.ExecuteCount.Should().Be(2);
        c.ExecuteCount.Should().Be(2);
    }

    [Fact]
    public async Task JumpToAsync_Backward_Undoes_Multiple()
    {
        var svc = new UndoRedoService();
        var a = new RecordingCommand("a");
        var b = new RecordingCommand("b");
        var c = new RecordingCommand("c");
        await svc.ExecuteAsync(a);
        await svc.ExecuteAsync(b);
        await svc.ExecuteAsync(c);
        // CurrentIndex=2

        // Index=0 へジャンプ → c, b が順に取り消される（a は残る）
        var result = await svc.JumpToAsync(0);

        result.IsError.Should().BeFalse();
        svc.CurrentIndex.Should().Be(0);
        a.UndoCount.Should().Be(0);
        b.UndoCount.Should().Be(1);
        c.UndoCount.Should().Be(1);
    }

    [Fact]
    public async Task JumpToAsync_Same_Position_Is_NoOp()
    {
        var svc = new UndoRedoService();
        var a = new RecordingCommand("a");
        await svc.ExecuteAsync(a);
        var initialExec = a.ExecuteCount;
        var initialUndo = a.UndoCount;

        var result = await svc.JumpToAsync(0); // 既に 0

        result.IsError.Should().BeFalse();
        a.ExecuteCount.Should().Be(initialExec);
        a.UndoCount.Should().Be(initialUndo);
    }

    [Fact]
    public async Task JumpToAsync_To_Negative_One_Undoes_All()
    {
        var svc = new UndoRedoService();
        var a = new RecordingCommand("a");
        var b = new RecordingCommand("b");
        await svc.ExecuteAsync(a);
        await svc.ExecuteAsync(b);

        var result = await svc.JumpToAsync(-1);

        result.IsError.Should().BeFalse();
        svc.CurrentIndex.Should().Be(-1);
        a.UndoCount.Should().Be(1);
        b.UndoCount.Should().Be(1);
    }

    [Fact]
    public async Task JumpToAsync_OutOfRange_Returns_Validation_Error()
    {
        var svc = new UndoRedoService();
        await svc.ExecuteAsync(new RecordingCommand("a"));
        // 履歴数 = 1（Index=0 のみ有効、JumpTo の有効範囲は -1 〜 0）

        var tooHigh = await svc.JumpToAsync(5);
        tooHigh.IsError.Should().BeTrue();
        tooHigh.FirstError.Type.Should().Be(ErrorType.Validation);

        var tooLow = await svc.JumpToAsync(-2);
        tooLow.IsError.Should().BeTrue();
        tooLow.FirstError.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task UndoAsync_Raises_Undone_With_Command()
    {
        var svc = new UndoRedoService();
        var cmd = new RecordingCommand("op1");
        await svc.ExecuteAsync(cmd);
        IUndoableCommand? captured = null;
        svc.Undone += c => captured = c;

        await svc.UndoAsync();

        captured.Should().BeSameAs(cmd);
    }

    [Fact]
    public async Task RedoAsync_Raises_Redone_With_Command()
    {
        var svc = new UndoRedoService();
        var cmd = new RecordingCommand("op1");
        await svc.ExecuteAsync(cmd);
        await svc.UndoAsync();
        IUndoableCommand? captured = null;
        svc.Redone += c => captured = c;

        await svc.RedoAsync();

        captured.Should().BeSameAs(cmd);
    }

    [Fact]
    public async Task UndoAsync_NoOp_Does_Not_Raise_Undone()
    {
        var svc = new UndoRedoService();
        var raised = false;
        svc.Undone += _ => raised = true;

        await svc.UndoAsync();

        raised.Should().BeFalse();
    }

    [Fact]
    public async Task UndoAsync_Failure_Does_Not_Raise_Undone()
    {
        var svc = new UndoRedoService();
        var bad = new RecordingCommand("bad", failOnUndo: true);
        await svc.ExecuteAsync(bad);
        var raised = false;
        svc.Undone += _ => raised = true;

        var result = await svc.UndoAsync();

        result.IsError.Should().BeTrue();
        raised.Should().BeFalse();
    }

    [Fact]
    public async Task JumpToAsync_Failure_Stops_And_Clears_Stack()
    {
        var svc = new UndoRedoService();
        var ok = new RecordingCommand("ok");
        var bad = new RecordingCommand("bad", failOnUndo: true);
        var ok2 = new RecordingCommand("ok2");
        await svc.ExecuteAsync(ok);
        await svc.ExecuteAsync(bad);
        await svc.ExecuteAsync(ok2);
        // CurrentIndex=2

        // 0 へジャンプしようとする → ok2 Undo OK → bad Undo 失敗 → 履歴 Clear
        var result = await svc.JumpToAsync(0);

        result.IsError.Should().BeTrue();
        svc.CanUndo.Should().BeFalse();
        svc.CanRedo.Should().BeFalse();
    }

    private sealed class RecordingCommand : IUndoableCommand
    {
        private readonly bool _failOnExecute;
        private readonly bool _failOnUndo;

        public RecordingCommand(string description, bool failOnExecute = false, bool failOnUndo = false)
        {
            Description = description;
            _failOnExecute = failOnExecute;
            _failOnUndo = failOnUndo;
        }

        public string Description { get; }
        public Guid? AffectedGridId => null;
        public int ExecuteCount { get; private set; }
        public int UndoCount { get; private set; }

        public Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default)
        {
            ExecuteCount++;
            if (_failOnExecute)
                return Task.FromResult<ErrorOr<Success>>(Error.Failure("Test.Fail", "execute failed"));
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }

        public Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default)
        {
            UndoCount++;
            if (_failOnUndo)
                return Task.FromResult<ErrorOr<Success>>(Error.NotFound("Test.NotFound", "undo failed"));
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }
}

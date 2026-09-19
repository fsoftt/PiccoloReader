using PiccoloReader.Core.Services;

namespace PiccoloReader.Core.Tests.Services;

public class CompositeUndoActionTests
{
    private class RecordingAction : IUndoableAction
    {
        private readonly List<string> _log;
        private readonly string _name;

        public RecordingAction(List<string> log, string name)
        {
            _log = log;
            _name = name;
        }

        public Task UndoAsync()
        {
            _log.Add($"undo-{_name}");
            return Task.CompletedTask;
        }

        public Task RedoAsync()
        {
            _log.Add($"redo-{_name}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task UndoAsync_UndoesSubActionsInReverseOrder()
    {
        var log = new List<string>();
        var sut = new CompositeUndoAction(new IUndoableAction[]
        {
            new RecordingAction(log, "A"),
            new RecordingAction(log, "B"),
        });

        await sut.UndoAsync();

        Assert.Equal(new[] { "undo-B", "undo-A" }, log);
    }

    [Fact]
    public async Task RedoAsync_RedoesSubActionsInOriginalOrder()
    {
        var log = new List<string>();
        var sut = new CompositeUndoAction(new IUndoableAction[]
        {
            new RecordingAction(log, "A"),
            new RecordingAction(log, "B"),
        });

        await sut.RedoAsync();

        Assert.Equal(new[] { "redo-A", "redo-B" }, log);
    }
}

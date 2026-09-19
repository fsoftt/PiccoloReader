namespace PiccoloReader.Core.Services;

public class CompositeUndoAction : IUndoableAction
{
    private readonly IReadOnlyList<IUndoableAction> _actions;

    public CompositeUndoAction(IReadOnlyList<IUndoableAction> actions)
    {
        _actions = actions;
    }

    public async Task UndoAsync()
    {
        for (var i = _actions.Count - 1; i >= 0; i--)
        {
            await _actions[i].UndoAsync();
        }
    }

    public async Task RedoAsync()
    {
        foreach (var action in _actions)
        {
            await action.RedoAsync();
        }
    }
}

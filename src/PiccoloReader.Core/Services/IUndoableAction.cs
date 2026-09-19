namespace PiccoloReader.Core.Services;

public interface IUndoableAction
{
    Task UndoAsync();
    Task RedoAsync();
}

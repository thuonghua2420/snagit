namespace SnapGit.Editor.Commands;

public sealed class CommandHistory
{
    private const int MaxDepth = 200;

    private readonly List<IUndoableCommand> _undo = new();
    private readonly Stack<IUndoableCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Raised after execute/undo/redo/clear.</summary>
    public event EventHandler? Changed;

    public void Execute(IUndoableCommand command)
    {
        command.Do();
        _undo.Add(command);
        if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redo.Pop();
        command.Do();
        _undo.Add(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

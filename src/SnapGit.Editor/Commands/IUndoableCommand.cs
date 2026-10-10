namespace SnapGit.Editor.Commands;

/// <summary>A reversible edit. <see cref="Do"/> must be safe to call again after <see cref="Undo"/> (redo).</summary>
public interface IUndoableCommand
{
    string Name { get; }
    void Do();
    void Undo();
}

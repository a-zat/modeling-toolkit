namespace ModelingToolkit.Core.Editing;

/// <summary>
/// A change of a document that can be undone. <see cref="Apply"/> and <see cref="Revert"/> alternate, starting with
/// <see cref="Apply"/>: redoing applies the command again.
/// </summary>
public interface IEditCommand
{
    /// <summary>What the command does, as the undo and redo commands show it: "Rename", "Delete"...</summary>
    string Name { get; }

    /// <exception cref="EditRefusedException">The change cannot be made; the document is left as it was.</exception>
    void Apply();

    void Revert();
}

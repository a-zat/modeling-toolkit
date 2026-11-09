using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>What the tools work on: the edits of the open document, where they happen, and the selection.</summary>
public interface IToolHost
{
    /// <summary>Runs the changes of the tools, as commands that can be undone.</summary>
    EditSession Session { get; }

    EditContext Context { get; }

    /// <summary>The faces, edges and vertices selected.</summary>
    ElementSelection Elements { get; }

    /// <summary>The path of the entity selected, from the model; null for none. Elements are selected without it.</summary>
    IReadOnlyList<Entity>? SelectedEntity { get; }

    /// <summary>Selects an entity by its path, from the model, or none.</summary>
    void SelectEntity(IReadOnlyList<Entity>? path);

    /// <summary>Shows what the tool is doing over the 3D view, in place of what it showed; empty for nothing.</summary>
    void ShowPreview(ToolPreview preview);

    /// <summary>Tells the user that an edit was refused, and why.</summary>
    void Report(string message);
}

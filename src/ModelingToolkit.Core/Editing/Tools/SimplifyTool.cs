using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Simplifies a component of the block being edited, or a mesh of it: a click selects it, a double click opens the
/// simplification, given the path to it from the model.
/// </summary>
public sealed class SimplifyTool(Action<IReadOnlyList<Entity>> open) : Tool
{
    private const string StartHint = "Double-click a component or a mesh to simplify it";

    private readonly Action<IReadOnlyList<Entity>> _open = open ?? throw new ArgumentNullException(nameof(open));

    public override string Name => "Simplify";

    protected override void OnActivated() => Hint = StartHint;

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Hint = Target(input.Pick) is { } path
            ? $"Double-click to simplify {path[^1].Name ?? (path[^1] as BlockReference)?.Block.Name ?? "it"}"
            : StartHint;
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Target(input.Pick) is not { } path)
        {
            return;
        }
        Host.SelectEntity(path);
        if (input.ClickCount == 2)
        {
            _open(path);
        }
    }

    // The path to the reference or the mesh of the block being edited under the pointer; null for none.
    private IReadOnlyList<Entity>? Target(ToolPick pick)
    {
        if (pick.Path is not { Count: > 0 } path || Host.Context.Within(path) is not { Count: > 0 } within ||
            within[0] is not (BlockReference or MeshEntity))
        {
            return null;
        }
        return [.. path.Take(path.Count - within.Count + 1)];
    }
}

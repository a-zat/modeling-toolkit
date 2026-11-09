using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Selects what is clicked in the edit context: a reference of the block being edited, which a double click enters; a
/// face, edge or vertex of a shape of that block, Shift adding it to the elements selected or taking it out; a mesh of
/// that block. A click on what is outside the context leaves it, to the block that has what was clicked, and a click on
/// nothing to the model. Escape clears the selection, or else leaves the block.
/// </summary>
public sealed class SelectTool : Tool
{
    public override string Name => "Select";

    protected override void OnActivated() => Hint = HintFor(ToolPick.Nothing);

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Hint = HintFor(input.Pick);
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        IToolHost host = Host;
        if (input.Pick.Path is not { Count: > 0 } path)
        {
            host.SelectEntity(null);
            host.Elements.Clear();
            host.Context.LeaveAll();
            return;
        }

        IReadOnlyList<Entity> within = LeaveTo(path);
        if (within[0] is BlockReference reference)
        {
            if (input.ClickCount == 2)
            {
                host.SelectEntity(null);
                host.Context.Enter(reference);
            }
            else
            {
                host.Elements.Clear();
                host.SelectEntity(path.Take(path.Count - within.Count + 1).ToArray());
            }
        }
        else if (input.ClickCount > 1)
        {
            // The first click selected it.
        }
        else if (input.Pick.Element is { } element)
        {
            host.SelectEntity(null);
            if (input.Modifiers.HasFlag(ToolModifiers.Shift))
            {
                host.Elements.Toggle(element);
            }
            else
            {
                host.Elements.Select(element);
            }
        }
        else
        {
            host.Elements.Clear();
            host.SelectEntity(path);
        }
        Hint = HintFor(input.Pick);
    }

    public override bool Key(ToolKey key, ToolModifiers modifiers)
    {
        IToolHost host = Host;
        if (key != ToolKey.Escape)
        {
            return false;
        }
        if (host.Elements.Count > 0 || host.SelectedEntity is not null)
        {
            host.Elements.Clear();
            host.SelectEntity(null);
            return true;
        }
        if (!host.Context.IsModel)
        {
            host.Context.Leave();
            return true;
        }
        return false;
    }

    // Leaves the context until it holds the path of entities, and gives the path from the block being edited.
    private IReadOnlyList<Entity> LeaveTo(IReadOnlyList<Entity> path)
    {
        EditContext context = Host.Context;
        IReadOnlyList<Entity>? within;
        while ((within = context.Within(path)) is null)
        {
            context.Leave();
        }
        return within;
    }

    private string HintFor(ToolPick pick)
    {
        EditContext context = Host.Context;
        if (pick.Path is not { Count: > 0 } path)
        {
            return context.IsModel ? "Click an object to select it" : "Click outside the block or press Esc to leave it";
        }

        IReadOnlyList<Entity>? within = context.Within(path);
        if (within is null)
        {
            return "Click to leave the block and select there";
        }
        if (within[0] is BlockReference reference)
        {
            return $"Click to select {reference.Name ?? reference.Block.Name}, double-click to edit its block";
        }
        if (pick.Element is { } element)
        {
            string kind = element.Kind.ToString().ToLowerInvariant();
            return $"Click to select the {kind}, Shift-click to add or remove it";
        }
        return "Click to select the mesh";
    }
}

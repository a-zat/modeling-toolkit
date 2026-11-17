using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Selects what is clicked in the edit context: a reference of the block being edited, which a double click enters; a
/// face, edge or vertex of a shape of that block, Shift adding it to the elements selected or taking it out; a mesh of
/// that block. A click on what is outside the context leaves it, to the block that has what was clicked, and a click on
/// nothing to the model. Escape clears the selection, or else leaves the block. The handles of the box of the entity
/// selected move it across the ground, lift it, or turn it, as they are dragged.
/// </summary>
public sealed class SelectTool : Tool
{
    private (SelectionHandles Handles, SelectionHandle Handle, HandleDrag? Drag)? _grab;

    public override string Name => "Select";

    /// <summary>The handles of the entity selected, when it is in the block being edited; null for none.</summary>
    public SelectionHandles? Handles
    {
        get
        {
            if (!IsActive || Host.SelectedEntity is not { } path || Host.Context.Within(path) is not [{ } entity])
            {
                return null;
            }
            if (_handles is not { } handles || !ReferenceEquals(handles.Entity, entity) || !_handlesPlacement!.Equals(Placed(entity)))
            {
                _handles = new SelectionHandles(entity, Host.Context.Placement);
                _handlesPlacement = Placed(entity);
            }
            return _handles.IsEmpty ? null : _handles;
        }
    }

    private SelectionHandles? _handles;
    private (Transform, Transform)? _handlesPlacement; // of the block and of the entity, when the handles were made

    private (Transform, Transform) Placed(Entity entity) =>
        (Host.Context.Placement, entity is BlockReference reference ? reference.Transform : Transform.Identity);

    /// <summary>Whether a handle is being dragged.</summary>
    public bool IsDraggingHandle => _grab is not null;

    public override void Press(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _grab = input.Handle is { } index && Handles is { } handles && index >= 0 && index < handles.Handles.Count
            ? (handles, handles.Handles[index], null)
            : null;
        if (_grab is not null)
        {
            Hint = "Drag to place it · Esc stops";
        }
    }

    public override void Drag(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_grab is not { } grab || input.Pick.Ray is not { } ray || grab.Handles.Drag(grab.Handle, ray) is not { } drag)
        {
            return;
        }
        _grab = grab with { Drag = drag };
        Measurement = drag.IsAngle ? Measurements.FormatAngle(drag.Measure) : Measurements.Format(drag.Measure);
        MeasurementName = drag.IsAngle ? "Angle" : "Distance";
        if (grab.Handles.Entity is BlockReference reference)
        {
            Transform placement = grab.Handles.Placement;
            Host.PreviewPlacement(reference, placement.Inverse() * drag.Transform * placement * reference.Transform);
        }
        Host.ShowPreview(new ToolPreview([.. drag.Lines, .. grab.Handles.Box(drag.Transform)], null));
    }

    public override void Release(ToolInput input)
    {
        if (_grab is not { } grab)
        {
            return;
        }
        StopDrag();
        if (grab.Drag is not { } drag || drag.Measure == 0 && !drag.IsAngle || drag.IsAngle && Math.Abs(drag.Measure) < 1e-9)
        {
            return;
        }
        Transform placement = grab.Handles.Placement;
        Entity entity = grab.Handles.Entity;
        IReadOnlyList<Entity>? selected = Host.SelectedEntity;
        Block? owner = entity.Owner;
        int index = owner?.Entities.IndexOf(entity) ?? -1;
        try
        {
            Host.Session.Execute(TransformCommands.Of(
                drag.IsAngle ? "Rotate" : "Move", entity, placement.Inverse() * drag.Transform * placement));
            // A mesh or a shape is replaced by the one transformed, which stays selected.
            if (selected is { Count: > 0 } && owner is not null && entity.Owner is null &&
                index >= 0 && index < owner.Entities.Count)
            {
                Host.SelectEntity([.. selected.Take(selected.Count - 1), owner.Entities[index]]);
            }
        }
        catch (Exception exception) when (exception is Kernel.GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The object could not be changed: {exception.Message}");
        }
    }

    private void StopDrag()
    {
        if (_grab is { Handles.Entity: BlockReference reference })
        {
            Host.PreviewPlacement(reference, null);
        }
        _grab = null;
        Measurement = null;
        Host.ShowPreview(ToolPreview.Empty);
        Hint = HintFor(ToolPick.Nothing);
    }

    protected override void OnDeactivated()
    {
        if (_grab is not null)
        {
            StopDrag();
        }
    }

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
        if (_grab is not null)
        {
            StopDrag();
            return true;
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

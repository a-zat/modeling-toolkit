using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// Selects what is clicked in the edit context: a reference of the block being edited, which a double click enters; a
/// face, edge or vertex of a shape of that block, Shift adding it to the elements selected or taking it out; a mesh of
/// that block. A click on what is outside the context leaves it, to the block that has what was clicked, and a click on
/// nothing to the model. Escape clears the selection, or else leaves the block. The handles of the box of the entity
/// selected move it, by its corners anywhere and by the middles of its edges along the axes, snapping as the move tool
/// does, or turn it, by the middles of its faces around their normal.
/// </summary>
public sealed class SelectTool(IGeometryKernel? kernel = null) : Tool
{
    // Moves the selection by the corners and the edges of its box; without a kernel, as for viewing, only faces turn it.
    private readonly MoveTool? _mover = kernel is null ? null : new MoveTool(kernel);
    private Grab? _grab;

    // A handle taken: by a corner or an edge, the mover moves the selection; by a face, it turns around its normal.
    private sealed record Grab(SelectionHandles Handles, SelectionHandle Handle)
    {
        public Vector3d? From; // where the turn starts
        public HandleDrag? Turn;
        public bool Moved;
    }

    public override string Name => "Select";

    /// <summary>
    /// The handle of a press on the selection itself, away from its handles: a drag moves it from the point pressed,
    /// snapped as the move tool snaps, to where it is let go, snapped to what is there.
    /// </summary>
    public const int SelectionBody = -1;

    /// <summary>The handles of the entity selected, when it is in the block being edited; null for none.</summary>
    public SelectionHandles? Handles
    {
        get
        {
            if (!IsActive || Host.SelectedEntity is not { } path || Host.Context.Reach(path) is not var (entity, placement))
            {
                return null;
            }
            (Transform, int) madeFor = (placement, Host.Session.Revision);
            if (_handles is not { } handles || !ReferenceEquals(handles.Entity, entity) || _handlesFor != madeFor)
            {
                _handles = new SelectionHandles(entity, placement);
                _handlesFor = madeFor;
            }
            return _handles.IsEmpty ? null : _handles;
        }
    }

    private SelectionHandles? _handles;
    private (Transform, int)? _handlesFor; // the placement of the block and the revision of the document they were made at

    /// <summary>Whether a handle is being dragged, or the selection placed.</summary>
    public bool IsDraggingHandle => _grab is not null || _placing is not null;

    // The selection being placed, as a model imported, and what to do if it is not.
    private Action? _placing;

    /// <summary>Whether the selection follows the pointer, until a click places it.</summary>
    public bool IsPlacing => _placing is not null;

    /// <summary>
    /// Makes the selection follow the pointer from a point of it, snapping as the move tool does, until a click places
    /// it there, as one command; Escape leaves it where it is and calls back. Whether it follows.
    /// </summary>
    public bool Place(Point3d from, Action? cancelled = null)
    {
        if (_mover is null || _grab is not null)
        {
            return false;
        }
        StopPlacing();
        _mover.Activate(Host);
        if (!_mover.Grab(new Inference(InferenceKind.Endpoint, from)))
        {
            _mover.Deactivate();
            return false;
        }
        _placing = cancelled ?? (() => { });
        Hint = "Click where it goes · Esc stops";
        return true;
    }

    private void StopPlacing()
    {
        if (_placing is null)
        {
            return;
        }
        _placing = null;
        if (_mover is { IsActive: true })
        {
            _mover.Deactivate();
        }
        Measurement = null;
        Host.ShowPreview(ToolPreview.Empty);
        Hint = HintFor(ToolPick.Nothing);
    }

    public override void Press(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _grab = null;
        if (_placing is not null)
        {
            return;
        }
        if (input.Handle == SelectionBody && Handles is { } held && _mover is not null)
        {
            // Taken by the point pressed, as the move tool takes it by a click.
            _mover.Activate(Host);
            _mover.Click(input);
            if (_mover.TakenAt is not { } taken)
            {
                _mover.Deactivate();
                return;
            }
            _grab = new Grab(held, new SelectionHandle(HandleKind.Move, taken, new Inference(InferenceKind.Endpoint, taken)));
            Hint = "Drag where it goes · Esc stops";
            return;
        }
        if (input.Handle is not { } index || Handles is not { } handles || index < 0 || index >= handles.Handles.Count)
        {
            return;
        }
        SelectionHandle handle = handles.Handles[index];
        if (handle.Kind != HandleKind.Rotate)
        {
            if (_mover is null)
            {
                return;
            }
            _mover.Activate(Host);
            if (!_mover.Grab(handle.Grip, constrained: true))
            {
                _mover.Deactivate();
                return;
            }
        }
        _grab = new Grab(handles, handle);
        Hint = handle.Kind == HandleKind.Rotate ? "Drag around to turn it · Esc stops" : "Drag where it goes · Esc stops";
    }

    public override void Drag(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_grab is not { } grab)
        {
            return;
        }
        if (grab.Handle.Kind != HandleKind.Rotate)
        {
            grab.Moved = true;
            _mover!.Hover(input);
            MeasurementName = _mover.MeasurementName;
            Measurement = _mover.Measurement;
            return;
        }
        Vector3d? from = grab.From;
        if (input.Pick.Ray is not { } ray || SelectionHandles.Turn(grab.Handle, ray, ref from,
                grab.Handle.Grip.Direction is { } axis ? RotateTool.Heading(grab.Handles.Entity, grab.Handles.Placement, axis) : null)
            is not { } turn)
        {
            return;
        }
        grab.From = from;
        grab.Turn = turn;
        Measurement = Measurements.FormatAngle(turn.Measure);
        MeasurementName = "Angle";
        if (grab.Handles.Entity is BlockReference reference)
        {
            Transform placement = grab.Handles.Placement;
            Host.PreviewPlacement(reference, placement.Inverse() * turn.Transform * placement * reference.Transform);
        }
        Host.ShowPreview(new ToolPreview([.. turn.Lines, .. grab.Handles.Box(turn.Transform)], null));
    }

    public override void Release(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_grab is not { } grab)
        {
            return;
        }
        if (grab.Handle.Kind != HandleKind.Rotate)
        {
            IReadOnlyList<Entity>? selected = Host.SelectedEntity;
            Entity entity = grab.Handles.Entity;
            Block? owner = entity.Owner;
            int index = owner?.Entities.IndexOf(entity) ?? -1;
            if (grab.Moved)
            {
                _mover!.Click(input); // where it is let go
            }
            StopDrag();
            Reselect(selected, entity, owner, index);
            return;
        }
        StopDrag();
        if (grab.Turn is not { } turn || Math.Abs(turn.Measure) < 1e-9)
        {
            return;
        }
        Transform placement = grab.Handles.Placement;
        IReadOnlyList<Entity>? path = Host.SelectedEntity;
        Entity turned = grab.Handles.Entity;
        Block? block = turned.Owner;
        int at = block?.Entities.IndexOf(turned) ?? -1;
        try
        {
            Host.Session.Execute(TransformCommands.Of("Rotate", turned, placement.Inverse() * turn.Transform * placement));
            Reselect(path, turned, block, at);
        }
        catch (Exception exception) when (exception is Kernel.GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The object could not be changed: {exception.Message}");
        }
    }

    // A mesh or a shape is replaced by the one transformed, which stays selected.
    private void Reselect(IReadOnlyList<Entity>? selected, Entity entity, Block? owner, int index)
    {
        if (selected is { Count: > 0 } && owner is not null && entity.Owner is null &&
            index >= 0 && index < owner.Entities.Count)
        {
            Host.SelectEntity([.. selected.Take(selected.Count - 1), owner.Entities[index]]);
        }
    }

    private void StopDrag()
    {
        if (_mover is { IsActive: true })
        {
            _mover.Deactivate();
        }
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
        Face = null;
        if (_grab is not null)
        {
            StopDrag();
        }
        StopPlacing();
    }

    protected override void OnActivated() => Hint = HintFor(ToolPick.Nothing);

    public override void Hover(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_placing is not null)
        {
            _mover!.Hover(input);
            MeasurementName = _mover.MeasurementName;
            Measurement = _mover.Measurement;
            return;
        }
        Hint = HintFor(input.Pick);
        PointFace(input);
    }

    /// <summary>
    /// The face of the box of the selection under the pointer, whose handles show; it stays while the pointer is on one
    /// of them, as on a corner, which is half off the face, or within <see cref="FaceMargin"/> of its sides. Null when
    /// the pointer is off the box.
    /// </summary>
    public int? Face { get; private set; }

    /// <summary>How far beyond the sides of the face pointed at, in units of the screen, its handles stay shown.</summary>
    public const double FaceMargin = 24;

    private void PointFace(ToolInput input)
    {
        int? face = null;
        if (_grab is null && Handles is { IsFlat: false } handles && input.Pick.Ray is { } ray)
        {
            face = handles.FaceUnder(ray, Face, FaceMargin);
            if (face is null && Face is { } current && input.Handle is { } handle && handles.OnFace(current).Contains(handle))
            {
                face = current;
            }
        }
        Face = face;
    }

    public override void Click(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_placing is not null)
        {
            IReadOnlyList<Entity>? selected = Host.SelectedEntity;
            _mover!.Click(input);
            StopPlacing();
            if (selected is not null)
            {
                Host.SelectEntity(selected); // still selected, to take it by its handles
            }
            return;
        }
        IToolHost host = Host;
        if (input.Pick.Path is not { Count: > 0 } path)
        {
            host.SelectEntity(null);
            host.Elements.Clear();
            if (input.ClickCount == 2 && !host.Context.IsModel)
            {
                host.Context.Leave(); // a double click outside the block leaves it, as one on it entered
            }
            return;
        }

        int clicks = input.ClickCount;
        if (host.Context.Within(path) is null)
        {
            // Outside the block being edited: only a double click leaves it, to select there, not to enter.
            if (clicks != 2)
            {
                return;
            }
            clicks = 1;
        }
        IReadOnlyList<Entity> within = LeaveTo(path);
        if (within[0] is BlockReference)
        {
            int chosen = path.Count - within.Count;
            if (clicks == 2)
            {
                host.SelectEntity(null);
                host.Context.Enter((BlockReference)path[chosen]);
            }
            else if (input.Modifiers.HasFlag(ToolModifiers.Shift))
            {
                // The innermost component under the pointer, its blocks entered as by double clicks.
                int deepest = path.Count - 1;
                while (path[deepest] is not BlockReference)
                {
                    deepest--;
                }
                for (int i = path.Count - within.Count; i < deepest; i++)
                {
                    if (path[i] is BlockReference around)
                    {
                        host.Context.Enter(around);
                    }
                }
                host.Elements.Clear();
                host.SelectEntity(path.Take(deepest + 1).ToArray());
            }
            else
            {
                host.Elements.Clear();
                host.SelectEntity(path.Take(chosen + 1).ToArray());
            }
        }
        else if (clicks > 1)
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
        if (_placing is { } cancelled)
        {
            StopPlacing();
            cancelled();
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
            return context.IsModel ? "Click an object to select it" : "Double click outside the block or press Esc to leave it";
        }

        IReadOnlyList<Entity>? within = context.Within(path);
        if (within is null)
        {
            return "Double click to leave the block and select there";
        }
        if (within[0] is BlockReference)
        {
            var reference = (BlockReference)within[0];
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

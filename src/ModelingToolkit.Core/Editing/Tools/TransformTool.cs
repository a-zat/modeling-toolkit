using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// A tool that transforms an entity of the block being edited, the one selected or else the one clicked, or a copy of
/// it with Option (Alt) held, showing its box where it goes.
/// </summary>
public abstract class TransformTool : DrawingTool
{
    /// <summary>How far, in degrees, the pointer may be off an axis and still snap to it.</summary>
    public const double AxisAngle = 6;

    protected TransformTool(IGeometryKernel kernel)
        : base(kernel)
    {
        Inference.AxisAngle = AxisAngle;
        Inference.AnyDirection = true;
    }

    // The corners, the middles of the edges and of the faces of the box of the target, or else of the selection, and
    // what they were made for: the entity, the placement of its block, and the revision of the document.
    private IReadOnlyList<Inference> _grips = [];
    private (Entity, Transform, int)? _grippedFor;

    /// <summary>
    /// The points of the box of the selection to take it by, while nothing is taken: its corners, the middles of its
    /// edges and of its faces, these on the plane of the face, along its normal.
    /// </summary>
    public IReadOnlyList<Inference> GripPoints => IsActive && Target is null ? [.. Grips()] : [];

    // Once taken, what is moved has no points to snap to: they would be where it was.
    protected override IEnumerable<Inference> Grips() => Target is null ? GripsFor(Selected) : [];

    // The entity selected, in the block being edited or in groups of it, with the placement of its block.
    private (Entity Entity, Transform Placement)? Selected =>
        Host.SelectedEntity is { } path ? Host.Context.Reach(path) : null;

    private IReadOnlyList<Inference> GripsFor((Entity Entity, Transform Placement)? of)
    {
        if (of is not var (entity, placement))
        {
            return [];
        }
        (Entity, Transform, int) key = (entity, placement, Host.Session.Revision);
        if (_grippedFor is not { } made || !ReferenceEquals(made.Item1, entity) || made.Item2 != key.Item2 ||
            made.Item3 != key.Item3)
        {
            _grips = GripsOf(entity, key.Item2);
            _grippedFor = key;
        }
        return _grips;
    }

    /// <summary>Whether a point is one of the middles of the edges of the box, which move along the axes only.</summary>
    protected bool IsEdgeGrip(Inference point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.Kind == InferenceKind.Midpoint && GripsFor(Target is { } target ? (target, TargetPlacement) : Selected).Any(grip =>
            grip.Kind == InferenceKind.Midpoint && grip.Point.DistanceTo(point.Point) < 1e-9);
    }

    /// <summary>The point the selection was taken by: to move from, or to turn around; null before.</summary>
    public virtual Point3d? TakenAt => null;

    /// <summary>
    /// Takes the selection by a point, as a click there would; whether there is a selection to take.
    /// </summary>
    public virtual bool Grab(Inference grip) => false;

    /// <summary>
    /// What is under the pointer, but nothing where the target is, which moves with the pointer: the pointer would
    /// otherwise take points on it as it goes.
    /// </summary>
    protected ToolPick Clear(ToolPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        return Target is { } target && pick.Path is { } path && path.Contains(target)
            ? pick with { Path = null, Element = null, Point = default, Normal = default }
            : pick;
    }

    /// <summary>Shows the target transformed, by a transform of the model space, as it would be; null as it is.</summary>
    protected void ShowTransformed(Transform? inModel)
    {
        if (Target is BlockReference reference)
        {
            Host.PreviewPlacement(reference, inModel is null
                ? null
                : TargetPlacement.Inverse() * inModel * TargetPlacement * reference.Transform);
        }
    }

    /// <summary>The entity taken; null before.</summary>
    protected Entity? Target { get; private set; }

    /// <summary>The placement in the model of the block holding the target: the block being edited, or a group of it.</summary>
    protected Transform TargetPlacement { get; private set; } = Transform.Identity;

    /// <summary>The edges of the box of the target, in the model space.</summary>
    protected IReadOnlyList<PreviewLine> Box { get; private set; } = [];

    /// <summary>Whether a copy is made, as Option was held at the last input.</summary>
    protected bool Copy { get; private set; }

    /// <summary>Notes whether Option is held.</summary>
    protected void Follow(ToolInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Copy = input.Modifiers.HasFlag(ToolModifiers.Alt);
    }

    /// <summary>Takes the entity selected, or else the one under the pointer; reports when there is none.</summary>
    protected bool TakeTarget(ToolPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        // What is clicked is taken as a click selects it: the entity of the block being edited that holds it.
        IReadOnlyList<Entity>? path = Host.SelectedEntity ??
            (pick.Path is { } picked && Host.Context.Within(picked) is { Count: > 0 } within
                ? [.. picked.Take(picked.Count - within.Count + 1)]
                : null);
        (Target, TargetPlacement) = path is not null && Host.Context.Reach(path) is var (entity, placement)
            ? (entity, placement)
            : ((Entity?)null, Transform.Identity);
        if (Target is null)
        {
            Host.Report($"Select an object, or click on one, to {Name.ToLowerInvariant()} it.");
            return false;
        }
        Box = BoxOf(Target);
        // A component turned is moved along its own axes too, as along those of the block being edited.
        ObjectAxes = Target is BlockReference reference && AxesOf(TargetPlacement * reference.Transform) is var own &&
            own.Any(axis => !IsContextAxis(axis))
            ? own
            : null;
        Inference.ObjectAxes = ObjectAxes;
        return true;
    }

    /// <summary>The axes of the component taken, X, Y and Z in the model space, of unit length; null for none.</summary>
    protected IReadOnlyList<Vector3d>? ObjectAxes { get; private set; }

    /// <summary>Whether a direction is along one of the axes of the edit context.</summary>
    protected bool IsContextAxis(Vector3d direction)
    {
        Vector3d unit = direction.Normalized();
        return AxesOf(Host.Context.Placement).Any(axis => Math.Abs(Math.Abs(axis.Dot(unit)) - 1) < 1e-9);
    }

    private static Vector3d[] AxesOf(Transform frame) =>
    [
        frame.ApplyVector(Vector3d.UnitX).Normalized(), frame.ApplyVector(Vector3d.UnitY).Normalized(),
        frame.ApplyVector(Vector3d.UnitZ).Normalized(),
    ];

    protected void ReleaseTarget()
    {
        if (IsActive)
        {
            ShowTransformed(null);
        }
        Target = null;
        TargetPlacement = Transform.Identity;
        Box = [];
        ObjectAxes = null;
        Inference.ObjectAxes = null;
    }

    protected override void OnDeactivated()
    {
        ShowTransformed(null);
        base.OnDeactivated();
    }

    /// <summary>
    /// The corners of the box of an entity of the block being edited, turned with it, the middles of its edges and of
    /// its faces, in the model space.
    /// </summary>
    protected static List<Inference> GripsOf(Entity entity, Transform placement) =>
        [.. new SelectionHandles(entity, placement).Grips];

    /// <summary>
    /// Transforms the target, or a copy of it, by a transform of the model space, as one command; reports why when it
    /// cannot. Whether it was done.
    /// </summary>
    protected bool Apply(Transform inModel)
    {
        ArgumentNullException.ThrowIfNull(inModel);
        if (Target is not { } target)
        {
            return false;
        }
        Transform placement = TargetPlacement;
        try
        {
            Host.Session.Execute(TransformCommands.Of(
                Copy ? "Copy" : Name, target, placement.Inverse() * inModel * placement, Copy));
            return true;
        }
        catch (Exception exception) when (exception is GeometryKernelException or ArgumentException or EditRefusedException)
        {
            Host.Report($"The object could not be changed: {exception.Message}");
            return false;
        }
    }

    /// <summary>Lines transformed in the model space.</summary>
    protected static IEnumerable<PreviewLine> Transformed(IEnumerable<PreviewLine> lines, Transform transform) =>
        lines.Select(line => line with { Start = transform.Apply(line.Start), End = transform.Apply(line.End) });

    private List<PreviewLine> BoxOf(Entity entity)
    {
        BoundingBox box = entity.GetBoundingBox();
        if (box.IsEmpty)
        {
            return [];
        }
        Transform placement = TargetPlacement;
        Point3d Corner(int i) => placement.Apply(new Point3d(
            (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z));
        var lines = new List<PreviewLine>();
        for (int i = 0; i < 8; i++)
        {
            foreach (int bit in new[] { 1, 2, 4 })
            {
                if ((i & bit) == 0)
                {
                    lines.Add(new PreviewLine(Corner(i), Corner(i | bit)));
                }
            }
        }
        return lines;
    }
}

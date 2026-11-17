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

    /// <summary>The corners of the box of the target and the middle of its bottom, to take it by.</summary>
    protected IReadOnlyList<Point3d> Grips { get; private set; } = [];

    protected override IEnumerable<Point3d> MoreSnapPoints => Grips;

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
                : Host.Context.Placement.Inverse() * inModel * Host.Context.Placement * reference.Transform);
        }
    }

    /// <summary>The entity taken; null before.</summary>
    protected Entity? Target { get; private set; }

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
        IReadOnlyList<Entity>? path = Host.SelectedEntity ?? pick.Path;
        Target = path is not null && Host.Context.Within(path) is { Count: > 0 } within ? within[0] : null;
        if (Target is null)
        {
            Host.Report($"Select an object, or click on one, to {Name.ToLowerInvariant()} it.");
            return false;
        }
        Box = BoxOf(Target);
        Grips = GripsOf(Target);
        return true;
    }

    protected void ReleaseTarget()
    {
        if (IsActive)
        {
            ShowTransformed(null);
        }
        Target = null;
        Box = [];
        Grips = [];
    }

    protected override void OnDeactivated()
    {
        ShowTransformed(null);
        base.OnDeactivated();
    }

    /// <summary>The corners of the box of an entity of the block being edited and the middle of its bottom.</summary>
    protected List<Point3d> GripsOf(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        BoundingBox box = entity.GetBoundingBox();
        if (box.IsEmpty)
        {
            return [];
        }
        Transform placement = Host.Context.Placement;
        var grips = new List<Point3d>();
        for (int i = 0; i < 8; i++)
        {
            grips.Add(placement.Apply(new Point3d(
                (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z)));
        }
        grips.Add(placement.Apply(new Point3d((box.Min.X + box.Max.X) / 2, (box.Min.Y + box.Max.Y) / 2, box.Min.Z)));
        return grips;
    }

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
        Transform placement = Host.Context.Placement;
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
        Transform placement = Host.Context.Placement;
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

using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>
/// A tool that transforms an entity of the block being edited, the one selected or else the one clicked, or a copy of
/// it with Option (Alt) held, showing its box where it goes.
/// </summary>
public abstract class TransformTool(IGeometryKernel kernel) : DrawingTool(kernel)
{
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
        return true;
    }

    protected void ReleaseTarget()
    {
        Target = null;
        Box = [];
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

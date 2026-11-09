using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>A line that a tool shows while it works, such as the segment being drawn.</summary>
/// <param name="Along">What the line follows, which gives it its colour: an axis, or none for a plain line.</param>
public sealed record PreviewLine(Point3d Start, Point3d End, InferenceKind Along = InferenceKind.None);

/// <summary>What a tool shows over the 3D view while it works: lines, and the point inferred under the pointer.</summary>
/// <param name="Inference">The point the tool would take, shown with a marker of its kind; null for none.</param>
public sealed record ToolPreview(IReadOnlyList<PreviewLine> Lines, Inference? Inference)
{
    public static ToolPreview Empty { get; } = new([], null);
}

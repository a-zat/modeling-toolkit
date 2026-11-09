using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>The command that erases faces and edges of shapes.</summary>
public static class EraseCommand
{
    /// <summary>
    /// One command that erases the faces and edges, of one shape or more, with <see cref="IGeometryKernel.RemoveElements"/>:
    /// a face leaves its edges and an edge takes the faces it bounds; the materials stay on the faces that remain. A
    /// shape left with nothing is deleted. Vertices are not erased on their own.
    /// </summary>
    /// <returns>Null when there is nothing to erase, as for vertices alone.</returns>
    public static IEditCommand? Of(IReadOnlyList<ElementReference> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var commands = new List<IEditCommand>();
        foreach (IGrouping<BrepEntity, ElementReference> group in elements
            .Where(element => element.Kind != ElementKind.Vertex).GroupBy(element => element.Entity))
        {
            BrepEntity entity = group.Key;
            int[] faces = [.. group.Where(e => e.Kind == ElementKind.Face).Select(e => e.Index).Distinct()];
            int[] edges = [.. group.Where(e => e.Kind == ElementKind.Edge).Select(e => e.Index).Distinct()];
            ShapeEdit edit = entity.Kernel.RemoveElements(entity.Shape, faces, edges);
            ShapeTopology left = entity.Kernel.CountElements(edit.Shape);
            if (left.Faces == 0 && left.Edges == 0)
            {
                edit.Shape.Dispose();
                commands.Add(new DeleteCommand(entity));
            }
            else
            {
                commands.Add(new ReplaceShapeCommand("Erase", entity, edit));
            }
        }
        return commands.Count == 0 ? null : new CompositeCommand("Erase", commands);
    }
}

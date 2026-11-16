using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>Counts and extent of a document, for reports.</summary>
/// <param name="Blocks">Blocks reachable from the root, the root excluded.</param>
/// <param name="Meshes">Meshes of the root and of those blocks, each counted once.</param>
/// <param name="PlacedMeshes">Meshes as they appear in the model: a block placed twice counts its meshes twice.</param>
/// <param name="BrepShapes">B-rep shapes of the root and of those blocks, each counted once.</param>
/// <param name="PlacedBrepShapes">B-rep shapes as they appear in the model, as <paramref name="PlacedMeshes"/>.</param>
/// <param name="BoundingBox">
/// Box of the vertices as placed in the model: exact, unlike <see cref="Document.GetBoundingBox"/>, at the cost of
/// visiting every placed vertex. A B-rep shape contributes the box of the shape, transformed as a whole.
/// </param>
public sealed record DocumentStatistics(
    int Blocks,
    int BlockReferences,
    int Meshes,
    long Vertices,
    long Triangles,
    long PlacedMeshes,
    long PlacedTriangles,
    int BrepShapes,
    long PlacedBrepShapes,
    int Materials,
    int Textures,
    int Animations,
    BoundingBox BoundingBox)
{
    public static DocumentStatistics Of(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<Block> blocks = ReachableBlocks(document.Root);
        List<Entity> entities = blocks.SelectMany(block => block.Entities).ToList();
        List<MeshEntity> meshes = entities.OfType<MeshEntity>().ToList();
        (long placedMeshes, long placedTriangles, long placedBrepShapes) =
            Placed(document.Root, new Dictionary<Block, (long, long, long)>());

        return new DocumentStatistics(
            Blocks: blocks.Count - 1,
            BlockReferences: entities.OfType<BlockReference>().Count(),
            Meshes: meshes.Count,
            Vertices: meshes.Sum(mesh => (long)mesh.Vertices.Length),
            Triangles: meshes.Sum(mesh => (long)mesh.Triangles.Length),
            PlacedMeshes: placedMeshes,
            PlacedTriangles: placedTriangles,
            BrepShapes: entities.OfType<BrepEntity>().Count(),
            PlacedBrepShapes: placedBrepShapes,
            Materials: document.Materials.Count,
            Textures: document.Textures.Count,
            Animations: document.Animations.Count,
            BoundingBox: PlacedBoundingBox(document.Root, Transform.Identity, BoundingBox.Empty));
    }

    private static BoundingBox PlacedBoundingBox(Block block, Transform transform, BoundingBox box)
    {
        foreach (Entity entity in block.Entities)
        {
            if (entity is MeshEntity mesh)
            {
                foreach (Point3d vertex in mesh.Vertices)
                {
                    box = box.Include(transform.Apply(vertex));
                }
            }
            else if (entity is BrepEntity brep)
            {
                box = box.Union(brep.GetBoundingBox().Transform(transform));
            }
            else if (entity is BlockReference reference)
            {
                box = PlacedBoundingBox(reference.Block, transform * reference.Transform, box);
            }
        }
        return box;
    }

    // The root first, then every block it references directly or indirectly, once each.
    private static List<Block> ReachableBlocks(Block root)
    {
        var found = new List<Block>();
        var visited = new HashSet<Block>();
        var pending = new Stack<Block>();
        pending.Push(root);
        while (pending.TryPop(out Block? block))
        {
            if (!visited.Add(block))
            {
                continue;
            }
            found.Add(block);
            foreach (BlockReference reference in block.Entities.OfType<BlockReference>())
            {
                pending.Push(reference.Block);
            }
        }
        return found;
    }

    // Totals of a block as placed in the model, computed once per block.
    private static (long Meshes, long Triangles, long BrepShapes) Placed(
        Block block, Dictionary<Block, (long, long, long)> totals)
    {
        if (totals.TryGetValue(block, out (long, long, long) total))
        {
            return total;
        }

        long meshes = 0;
        long triangles = 0;
        long brepShapes = 0;
        foreach (Entity entity in block.Entities)
        {
            if (entity is MeshEntity mesh)
            {
                meshes++;
                triangles += mesh.Triangles.Length;
            }
            else if (entity is BrepEntity)
            {
                brepShapes++;
            }
            else if (entity is BlockReference reference)
            {
                (long referencedMeshes, long referencedTriangles, long referencedBrepShapes) =
                    Placed(reference.Block, totals);
                meshes += referencedMeshes;
                triangles += referencedTriangles;
                brepShapes += referencedBrepShapes;
            }
        }
        totals[block] = (meshes, triangles, brepShapes);
        return (meshes, triangles, brepShapes);
    }
}

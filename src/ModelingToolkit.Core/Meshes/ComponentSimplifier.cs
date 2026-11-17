using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Meshes;

/// <summary>
/// The meshes of a component simplified: of the block of a reference and of the blocks it places, each mesh once, or a
/// mesh by itself. The meshes of a block placed elsewhere too change there as well.
/// </summary>
public sealed class SimplifiedComponent
{
    private readonly Dictionary<Block, List<MeshEntity>> _merged = [];

    private SimplifiedComponent(IReadOnlyList<SimplifiedMesh> meshes, SimplifyOptions options)
    {
        Meshes = meshes;
        Options = options;
        Before = meshes.Aggregate(default(MeshMeasures), (sum, mesh) => sum + mesh.Before);
        MaxDeviation = meshes.Count == 0 ? 0 : meshes.Max(mesh => mesh.MaxDeviation);
        double weight = meshes.Sum(mesh => (double)mesh.Before.Triangles);
        MeanDeviation = weight == 0 ? 0 : meshes.Sum(mesh => mesh.MeanDeviation * mesh.Before.Triangles) / weight;

        if (options.MergeByMaterial)
        {
            // The results of each block with the same material, as one.
            foreach (IGrouping<(Block Block, Material? Material), SimplifiedMesh> group in meshes
                         .Where(mesh => mesh.Result is not null && mesh.Original.Owner is not null)
                         .GroupBy(mesh => (mesh.Original.Owner!, mesh.Result!.Material)))
            {
                if (!_merged.TryGetValue(group.Key.Block, out List<MeshEntity>? list))
                {
                    _merged[group.Key.Block] = list = [];
                }
                list.Add(Merge([.. group.Select(mesh => mesh.Result!)]));
            }
            After = _merged.Values.SelectMany(list => list).Aggregate(default(MeshMeasures), (sum, mesh) => sum + MeshMeasures.Of(mesh));
        }
        else
        {
            After = meshes.Aggregate(default(MeshMeasures), (sum, mesh) => sum + mesh.After);
        }
    }

    public IReadOnlyList<SimplifiedMesh> Meshes { get; }

    public SimplifyOptions Options { get; }

    public MeshMeasures Before { get; }

    public MeshMeasures After { get; }

    /// <summary>The furthest the surface moved, in millimetres.</summary>
    public double MaxDeviation { get; }

    /// <summary>How far the surface moved on average, in millimetres.</summary>
    public double MeanDeviation { get; }

    /// <summary>The meshes of an entity: those of the blocks a reference places, each once, or a mesh itself.</summary>
    public static IReadOnlyList<MeshEntity> MeshesOf(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var meshes = new List<MeshEntity>();
        var seen = new HashSet<Block>();
        void Visit(Entity visited)
        {
            switch (visited)
            {
                case MeshEntity mesh:
                    meshes.Add(mesh);
                    break;
                case BlockReference reference when seen.Add(reference.Block):
                    foreach (Entity inner in reference.Block.Entities)
                    {
                        Visit(inner);
                    }
                    break;
            }
        }
        Visit(entity);
        return meshes;
    }

    /// <summary>
    /// The meshes of an entity as they are, each with the placement of a path to it, in the coordinates of the
    /// component: those of the block of a reference, wherever it places them, or a mesh itself.
    /// </summary>
    public static IEnumerable<(MeshEntity Mesh, Transform Placement)> MeshesPlaced(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var results = new List<(MeshEntity, Transform)>();
        void Visit(Entity visited, Transform placement, int depth)
        {
            switch (visited)
            {
                case MeshEntity mesh:
                    results.Add((mesh, placement));
                    break;
                case BlockReference reference when depth < 64:
                    foreach (Entity child in reference.Block.Entities)
                    {
                        Visit(child, placement * reference.Transform, depth + 1);
                    }
                    break;
            }
        }
        Visit(entity, entity is BlockReference top ? top.Transform.Inverse() : Transform.Identity, 0);
        return results;
    }

    /// <summary>Simplifies the meshes of an entity, several at once.</summary>
    public static SimplifiedComponent Of(Entity entity, SimplifyOptions options, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);
        IReadOnlyList<MeshEntity> meshes = MeshesOf(entity);
        var results = new SimplifiedMesh[meshes.Count];
        Parallel.For(0, meshes.Count, new ParallelOptions { CancellationToken = cancellation },
            index => results[index] = MeshSimplifier.Simplify(meshes[index], options, cancellation));
        return new SimplifiedComponent(results, options);
    }

    /// <summary>
    /// The meshes that the component would have, each with the placement of a path to it from the component, for
    /// showing them: the results, or the meshes as they are.
    /// </summary>
    public IEnumerable<(MeshEntity Mesh, Transform Placement)> Placed(Entity entity, bool simplified)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var byOriginal = new Dictionary<MeshEntity, SimplifiedMesh>(ReferenceEqualityComparer.Instance);
        foreach (SimplifiedMesh mesh in Meshes)
        {
            byOriginal[mesh.Original] = mesh;
        }
        var results = new List<(MeshEntity, Transform)>();
        void Visit(Entity visited, Transform placement, int depth)
        {
            switch (visited)
            {
                case MeshEntity mesh when !simplified:
                    results.Add((mesh, placement));
                    break;
                case MeshEntity mesh when !Options.MergeByMaterial:
                    if (byOriginal.TryGetValue(mesh, out SimplifiedMesh? done) && done.Result is { } result)
                    {
                        results.Add((result, placement));
                    }
                    break;
                case BlockReference reference when depth < 64:
                    Transform inner = placement * reference.Transform;
                    foreach (Entity child in reference.Block.Entities)
                    {
                        Visit(child, inner, depth + 1);
                    }
                    if (simplified && Options.MergeByMaterial && _merged.TryGetValue(reference.Block, out List<MeshEntity>? merged))
                    {
                        results.AddRange(merged.Select(mesh => (mesh, inner)));
                    }
                    break;
            }
        }
        // The component in its own coordinates.
        Visit(entity, entity is BlockReference top ? top.Transform.Inverse() : Transform.Identity, 0);
        if (simplified && Options.MergeByMaterial && entity is MeshEntity single &&
            single.Owner is { } owner && _merged.TryGetValue(owner, out List<MeshEntity>? alone))
        {
            results.AddRange(alone.Select(mesh => (mesh, Transform.Identity)));
        }
        return results;
    }

    /// <summary>The command that puts the results in the place of the meshes, as one change that can be undone.</summary>
    public IEditCommand Command()
    {
        var commands = new List<IEditCommand>();
        if (Options.MergeByMaterial)
        {
            // Deleting from the last of each block keeps the places of the others.
            foreach (MeshEntity mesh in Meshes.Select(mesh => mesh.Original).Where(mesh => mesh.Owner is not null)
                         .OrderByDescending(mesh => mesh.Owner!.Entities.IndexOf(mesh)))
            {
                commands.Add(new DeleteCommand(mesh));
            }
            foreach ((Block block, List<MeshEntity> merged) in _merged)
            {
                commands.AddRange(merged.Select(mesh => new AddEntityCommand("Simplify", block, mesh)));
            }
        }
        else
        {
            foreach (SimplifiedMesh mesh in Meshes.Where(mesh => mesh.Original.Owner is not null))
            {
                commands.Add(mesh.Result is { } result
                    ? new ReplaceEntityCommand("Simplify", mesh.Original, result)
                    : new DeleteCommand(mesh.Original));
            }
        }
        return new CompositeCommand("Simplify", commands);
    }

    // One mesh of several, of the material of the first, named after it.
    private static MeshEntity Merge(IReadOnlyList<MeshEntity> meshes)
    {
        if (meshes.Count == 1)
        {
            return meshes[0];
        }
        bool normals = meshes.All(mesh => mesh.Normals is not null);
        bool uvs = meshes.All(mesh => mesh.TextureCoordinates is not null);
        var vertices = new List<Point3d>();
        var triangles = new List<Triangle>();
        var allNormals = new List<System.Numerics.Vector3>();
        var allUvs = new List<System.Numerics.Vector2>();
        foreach (MeshEntity mesh in meshes)
        {
            int offset = vertices.Count;
            vertices.AddRange(mesh.Vertices);
            triangles.AddRange(mesh.Triangles.Select(t => new Triangle(t.A + offset, t.B + offset, t.C + offset)));
            if (normals)
            {
                allNormals.AddRange(mesh.Normals!);
            }
            if (uvs)
            {
                allUvs.AddRange(mesh.TextureCoordinates!);
            }
        }
        return new MeshEntity([.. vertices], [.. triangles], normals ? [.. allNormals] : null, uvs ? [.. allUvs] : null)
        {
            Name = meshes[0].Name,
            Material = meshes[0].Material,
        };
    }
}

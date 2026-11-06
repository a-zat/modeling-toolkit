using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>A mesh placed in the model space with a material, as indices into the lists of a <see cref="ViewportScene"/>.</summary>
internal readonly record struct ViewportInstance(int Mesh, int Material, Transform Placement);

/// <summary>
/// What the 3D view shows of a document: each mesh, material and texture once, and an instance for each placement of a
/// mesh, through the block references that reach it from the model space. A mesh without a material takes the one of
/// the closest reference that has one, "by block", or <see cref="DefaultMaterial"/>. The instances follow the order of
/// the model tree, so that those of a node are consecutive. A B-rep shape is shown as its tessellation, made once, for
/// a deflection that is a fraction of the size of the shape.
/// </summary>
internal sealed class ViewportScene
{
    private readonly Block _root;
    private readonly Dictionary<Block, int> _instanceCounts;
    private readonly Dictionary<BrepEntity, ShapeTessellation?> _tessellations;

    // The index of each instance in the viewer that shows the scene, or -1 for those it could not show.
    private int[] _shownInstances = [];

    private ViewportScene(Block root, Builder builder)
    {
        _root = root;
        _instanceCounts = builder.InstanceCounts;
        _tessellations = builder.Tessellations;
        Meshes = builder.Meshes;
        MeshEdges = builder.MeshEdges;
        Warnings = builder.Warnings;
        Materials = builder.Materials;
        MaterialTextures = builder.MaterialTextures;
        Textures = builder.Textures;
        Instances = builder.Instances;
    }

    /// <summary>A light grey, rather rough plastic.</summary>
    public static Material DefaultMaterial { get; } = new("Default")
    {
        BaseColor = new Color(0.42f, 0.45f, 0.5f),
        Roughness = 0.7f,
    };

    /// <summary>The meshes with triangles, those of the shapes included; meshes without any show nothing.</summary>
    public IReadOnlyList<MeshEntity> Meshes { get; }

    /// <summary>
    /// For each mesh, the edges to draw in its place of those of its triangles: those of the shape it is the
    /// tessellation of; null for the other meshes.
    /// </summary>
    public IReadOnlyList<Polylines?> MeshEdges { get; }

    public IReadOnlyList<Material> Materials { get; }

    /// <summary>For each material, its base colour texture as an index into <see cref="Textures"/>, or -1.</summary>
    public IReadOnlyList<int> MaterialTextures { get; }

    public IReadOnlyList<Texture> Textures { get; }

    public IReadOnlyList<ViewportInstance> Instances { get; }

    /// <summary>What the scene leaves out: the shapes that could not be turned into triangles, and why.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public static ViewportScene Of(Document document)
    {
        var builder = new Builder();
        builder.Tessellate(document.Root);
        builder.Visit(document.Root, Transform.Identity, inherited: null);
        return new ViewportScene(document.Root, builder);
    }

    /// <summary>
    /// The instances that a node of the model tree places, given its path: the entities from the model to the node.
    /// All of them for the empty path, the model; none for a path that is not in the document.
    /// </summary>
    public Range InstancesOf(IReadOnlyList<Entity> path)
    {
        int first = 0;
        int count = InstanceCount(_root);
        Block? block = _root;
        foreach (Entity entity in path)
        {
            int index = block?.Entities.IndexOf(entity) ?? -1;
            if (index < 0)
            {
                return default;
            }

            first += block!.Entities.Take(index).Sum(InstanceCount);
            count = InstanceCount(entity);
            block = (entity as BlockReference)?.Block;
        }

        return first..(first + count);
    }

    /// <summary>Shows the scene in a viewer, in place of what it showed, and turns the camera to it.</summary>
    /// <param name="images">The image of each texture, null for those that could not be decoded.</param>
    public void ShowIn(OcctViewer viewer, IReadOnlyList<TextureImage?> images)
    {
        viewer.Clear();
        int[] textures = images.Select(image => image is null ? -1 : viewer.AddTexture(image)).ToArray();
        int[] materials = new int[Materials.Count];
        for (int index = 0; index < Materials.Count; index++)
        {
            int texture = MaterialTextures[index];
            materials[index] = viewer.AddMaterial(Materials[index], texture < 0 ? -1 : textures[texture]);
        }

        // A mesh or a placement that the viewer refuses, with positions that are not finite or a placement that
        // flattens the mesh, is left out, rather than the whole document.
        int[] meshes = Meshes.Select((mesh, index) =>
        {
            try
            {
                return viewer.AddMesh(mesh, MeshEdges[index]);
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }).ToArray();
        _shownInstances = Instances.Select(instance =>
        {
            try
            {
                return meshes[instance.Mesh] < 0
                    ? -1
                    : viewer.AddInstance(meshes[instance.Mesh], materials[instance.Material], instance.Placement);
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }).ToArray();

        viewer.FitAll();
    }

    /// <summary>
    /// The path of the leaf of the model tree that places an instance: the entities from the model to its mesh. Null
    /// for an index out of range.
    /// </summary>
    public IReadOnlyList<Entity>? PathOf(int instance)
    {
        if (instance < 0 || instance >= Instances.Count)
        {
            return null;
        }

        // Down from the model, into the entity whose instances include this one, until its mesh.
        var path = new List<Entity>();
        Block block = _root;
        while (true)
        {
            Entity? next = null;
            foreach (Entity entity in block.Entities)
            {
                int count = InstanceCount(entity);
                if (instance < count)
                {
                    next = entity;
                    break;
                }

                instance -= count;
            }

            if (next is null)
            {
                return null;
            }

            path.Add(next);
            if (next is not BlockReference reference)
            {
                return path;
            }

            block = reference.Block;
        }
    }

    /// <summary>As <see cref="PathOf"/>, for an instance of the viewer that shows the scene; null for -1.</summary>
    public IReadOnlyList<Entity>? PathOfShown(int viewerInstance) =>
        viewerInstance < 0 ? null : PathOf(Array.IndexOf(_shownInstances, viewerInstance));

    /// <summary>
    /// Shows as selected the instances of a node of the model tree, given its path, in the viewer that shows the scene;
    /// none for null.
    /// </summary>
    public void ShowSelectionIn(OcctViewer viewer, IReadOnlyList<Entity>? path)
    {
        int[] selected = path is null
            ? []
            : _shownInstances.Take(InstancesOf(path)).Where(instance => instance >= 0).ToArray();
        viewer.SetSelection(selected);
    }

    private int InstanceCount(Block block) => _instanceCounts.GetValueOrDefault(block);

    private int InstanceCount(Entity entity) => entity switch
    {
        MeshEntity mesh => mesh.Triangles.Length > 0 ? 1 : 0,
        BrepEntity brep => _tessellations.GetValueOrDefault(brep) is null ? 0 : 1,
        BlockReference reference => InstanceCount(reference.Block),
        _ => 0,
    };

    private sealed class Builder
    {
        // The deflection of a shape is this part of its diagonal, but not less than the minimum, in millimetres.
        private const double RelativeDeflection = 0.001;
        private const double MinimumDeflection = 0.001;

        private readonly Dictionary<MeshEntity, int> _meshIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Material, int> _materialIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Texture, int> _textureIndices = new(ReferenceEqualityComparer.Instance);

        public List<MeshEntity> Meshes { get; } = [];

        public List<Polylines?> MeshEdges { get; } = [];

        public List<Material> Materials { get; } = [];

        public List<int> MaterialTextures { get; } = [];

        public List<Texture> Textures { get; } = [];

        public List<ViewportInstance> Instances { get; } = [];

        public List<string> Warnings { get; } = [];

        /// <summary>The mesh of each B-rep shape visited, null when it could not be tessellated.</summary>
        public Dictionary<BrepEntity, ShapeTessellation?> Tessellations { get; } =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>The number of instances that a reference to each block places.</summary>
        public Dictionary<Block, int> InstanceCounts { get; } = new(ReferenceEqualityComparer.Instance);

        /// <param name="placement">From the coordinates of the block to those of the model space.</param>
        /// <param name="inherited">The material "by block": the one of the closest reference that has one.</param>
        public void Visit(Block block, Transform placement, Material? inherited)
        {
            int first = Instances.Count;
            foreach (Entity entity in block.Entities)
            {
                switch (entity)
                {
                    case MeshEntity mesh when mesh.Triangles.Length > 0:
                        AddInstance(mesh, entity.Material ?? inherited, placement);
                        break;
                    case BrepEntity brep when TessellationOf(brep) is { } tessellation:
                        AddInstance(tessellation.Mesh, entity.Material ?? inherited, placement, tessellation.Edges);
                        break;
                    case BlockReference reference:
                        Visit(reference.Block, placement * reference.Transform, reference.Material ?? inherited);
                        break;
                }
            }

            InstanceCounts[block] = Instances.Count - first;
        }

        private void AddInstance(MeshEntity mesh, Material? material, Transform placement, Polylines? edges = null) =>
            Instances.Add(new ViewportInstance(IndexOf(mesh, edges), IndexOf(material ?? DefaultMaterial), placement));

        /// <summary>
        /// Tessellates, in parallel, every shape that the model reaches, once each however often it is placed. The
        /// warnings follow the order of the model, whatever the order in which the shapes are done.
        /// </summary>
        public void Tessellate(Block root)
        {
            var shapes = new List<BrepEntity>();
            var seenShapes = new HashSet<BrepEntity>(ReferenceEqualityComparer.Instance);
            var seenBlocks = new HashSet<Block>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<Block>([root]);
            while (pending.TryPop(out Block? block))
            {
                if (!seenBlocks.Add(block))
                {
                    continue;
                }
                foreach (Entity entity in block.Entities)
                {
                    if (entity is BrepEntity brep && seenShapes.Add(brep))
                    {
                        shapes.Add(brep);
                    }
                    else if (entity is BlockReference reference)
                    {
                        pending.Push(reference.Block);
                    }
                }
            }

            var results = new (ShapeTessellation? Mesh, string? Warning)[shapes.Count];
            Parallel.For(0, shapes.Count, index => results[index] = Tessellated(shapes[index]));
            for (int index = 0; index < shapes.Count; index++)
            {
                Tessellations[shapes[index]] = results[index].Mesh;
                if (results[index].Warning is { } warning)
                {
                    Warnings.Add(warning);
                }
            }
        }

        // The shapes were tessellated beforehand: those of the model, by Tessellate.
        private ShapeTessellation? TessellationOf(BrepEntity brep) => Tessellations.GetValueOrDefault(brep);

        // A shape that cannot be turned into triangles is left out, as a mesh that the viewer refuses, with a warning.
        private static (ShapeTessellation? Mesh, string? Warning) Tessellated(BrepEntity brep)
        {
            try
            {
                BoundingBox box = brep.GetBoundingBox();
                double diagonal = Math.Sqrt(
                    Math.Pow(box.Max.X - box.Min.X, 2) + Math.Pow(box.Max.Y - box.Min.Y, 2) +
                    Math.Pow(box.Max.Z - box.Min.Z, 2));
                double deflection = Math.Max(diagonal * RelativeDeflection, MinimumDeflection);
                return (brep.Kernel.Tessellate(brep.Shape, deflection), null);
            }
            catch (Exception exception) when (exception is ArgumentException or GeometryKernelException)
            {
                string name = brep.Name ?? brep.Material?.Name ?? "A shape";
                return (null, $"{name} cannot be shown: {exception.Message}");
            }
        }

        private int IndexOf(MeshEntity mesh, Polylines? edges)
        {
            if (!_meshIndices.TryGetValue(mesh, out int index))
            {
                index = _meshIndices[mesh] = Meshes.Count;
                Meshes.Add(mesh);
                MeshEdges.Add(edges);
            }

            return index;
        }

        private int IndexOf(Material material)
        {
            if (!_materialIndices.TryGetValue(material, out int index))
            {
                index = _materialIndices[material] = Materials.Count;
                Materials.Add(material);
                MaterialTextures.Add(material.BaseColorTexture is { } texture ? IndexOf(texture) : -1);
            }

            return index;
        }

        private int IndexOf(Texture texture)
        {
            if (!_textureIndices.TryGetValue(texture, out int index))
            {
                index = _textureIndices[texture] = Textures.Count;
                Textures.Add(texture);
            }

            return index;
        }
    }
}

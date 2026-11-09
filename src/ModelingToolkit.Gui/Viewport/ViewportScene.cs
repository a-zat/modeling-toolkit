using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport.Interop;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// A mesh placed in the model space with a material, as indices into the lists of a <see cref="ViewportScene"/>, and
/// the path of the leaf of the model tree that places it: the entities from the model to its mesh.
/// </summary>
internal readonly record struct ViewportInstance(int Mesh, int Material, Transform Placement, IReadOnlyList<Entity> Path);

/// <summary>A tessellation of a B-rep entity, for one of its shapes: null when it could not be made, with a warning.</summary>
internal sealed record ShapeMesh(BrepEntity Entity, IKernelShape Shape, ShapeTessellation? Tessellation, string? Warning);

/// <summary>
/// What the 3D view shows of a document: each mesh, material and texture once, and an instance for each placement of a
/// mesh, through the block references that reach it from the model space. A mesh without a material takes the one of
/// the closest reference that has one, "by block", or <see cref="DefaultMaterial"/>. The instances follow the order of
/// the model tree, so that those of a node are consecutive. A B-rep shape is shown as its tessellation, made once, for
/// a deflection that is a fraction of the size of the shape.
/// </summary>
/// <remarks>
/// The scene follows the changes of the document through <see cref="Update"/>, which walks the model again, cheaply as
/// the meshes are kept, and changes in the viewer only the instances that differ: those of the same path keep their
/// index in the viewer. A shape that changed keeps its previous tessellation until the new one, made in the background
/// (see <see cref="TakeShapesToTessellate"/>), is accepted; one made for a shape that has changed again is discarded.
/// The scene is used from one thread, the background tessellations aside.
/// </remarks>
internal sealed class ViewportScene
{
    // The deflection of a shape is this part of its diagonal, but not less than the minimum, in millimetres.
    private const double RelativeDeflection = 0.001;
    private const double MinimumDeflection = 0.001;

    private readonly Block _root;

    private readonly List<MeshEntity?> _meshes = [];
    private readonly List<Polylines?> _meshEdges = [];
    private readonly List<ShapeTessellation?> _meshShapes = []; // for the meshes of shapes, picked by their elements
    private readonly List<Material> _materials = [];
    private readonly List<int> _materialTextures = [];
    private readonly List<Texture> _textures = [];
    private readonly Dictionary<MeshEntity, int> _meshIndices = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Material, int> _materialIndices = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Texture, int> _textureIndices = new(ReferenceEqualityComparer.Instance);

    // The latest tessellation accepted for each B-rep entity, possibly of a shape it no longer has, and the shapes
    // being tessellated.
    private readonly Dictionary<BrepEntity, ShapeMesh> _tessellations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BrepEntity, IKernelShape> _tessellating = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _warnings = [];

    private List<ViewportInstance> _instances = [];
    private List<BrepEntity> _reachedShapes = [];

    // The viewer that shows the scene, and the index there of each mesh, texture, material and instance of the scene,
    // -1 for those it could not show.
    private IViewerScene? _viewer;
    private readonly List<int> _shownMeshes = [];
    private readonly List<int> _shownTextures = [];
    private readonly List<int> _shownMaterials = [];
    private List<int> _shownInstances = [];

    private ViewportScene(Block root)
    {
        _root = root;
    }

    /// <summary>A light grey, rather rough plastic.</summary>
    public static Material DefaultMaterial { get; } = new("Default")
    {
        BaseColor = new Color(0.42f, 0.45f, 0.5f),
        Roughness = 0.7f,
    };

    /// <summary>
    /// The meshes with triangles, those of the shapes included; meshes without any show nothing. A mesh that no
    /// instance places any more, after an update, is null.
    /// </summary>
    public IReadOnlyList<MeshEntity?> Meshes => _meshes;

    /// <summary>
    /// For each mesh, the edges to draw in place of those of its triangles: those of the shape it is the tessellation
    /// of, or its sharp edges (<see cref="SharpEdges"/>).
    /// </summary>
    public IReadOnlyList<Polylines?> MeshEdges => _meshEdges;

    public IReadOnlyList<Material> Materials => _materials;

    /// <summary>For each material, its base colour texture as an index into <see cref="Textures"/>, or -1.</summary>
    public IReadOnlyList<int> MaterialTextures => _materialTextures;

    public IReadOnlyList<Texture> Textures => _textures;

    public IReadOnlyList<ViewportInstance> Instances => _instances;

    /// <summary>What the scene left out when it was made: the shapes that could not be turned into triangles, and why.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Makes the scene of a document, tessellating its shapes in parallel.</summary>
    public static ViewportScene Of(Document document)
    {
        var scene = new ViewportScene(document.Root);
        List<BrepEntity> shapes = scene.ReachedShapes();
        var results = new ShapeMesh[shapes.Count];
        Parallel.For(0, shapes.Count, index => results[index] = Tessellate(shapes[index], shapes[index].Shape));
        // The warnings follow the order of the model, whatever the order in which the shapes were done.
        foreach (ShapeMesh result in results)
        {
            scene._tessellations[result.Entity] = result;
            if (result.Warning is { } warning)
            {
                scene._warnings.Add(warning);
            }
        }

        scene._instances = scene.Build();
        return scene;
    }

    /// <summary>
    /// Tessellates a shape of a B-rep entity; safe on any thread. A shape that cannot be turned into triangles gives a
    /// warning instead, as one that has been disposed in the meantime.
    /// </summary>
    public static ShapeMesh Tessellate(BrepEntity brep, IKernelShape shape)
    {
        try
        {
            BoundingBox box = brep.Kernel.GetBoundingBox(shape);
            if (box.IsEmpty)
            {
                return new(brep, shape, null, $"{NameOf(brep)} cannot be shown: it has no geometry.");
            }
            double diagonal = Math.Sqrt(
                Math.Pow(box.Max.X - box.Min.X, 2) + Math.Pow(box.Max.Y - box.Min.Y, 2) +
                Math.Pow(box.Max.Z - box.Min.Z, 2));
            double deflection = Math.Max(diagonal * RelativeDeflection, MinimumDeflection);
            return new(brep, shape, brep.Kernel.Tessellate(shape, deflection), null);
        }
        catch (Exception exception)
            when (exception is ArgumentException or GeometryKernelException or ObjectDisposedException)
        {
            return new(brep, shape, null, $"{NameOf(brep)} cannot be shown: {exception.Message}");
        }
    }

    /// <summary>
    /// The shapes of the model, as of the last <see cref="Update"/>, that the scene shows with an older tessellation, or
    /// not at all yet, and that are not being tessellated: they are now, until <see cref="Accept"/> receives their
    /// tessellation.
    /// </summary>
    public IReadOnlyList<(BrepEntity Entity, IKernelShape Shape)> TakeShapesToTessellate()
    {
        var shapes = new List<(BrepEntity, IKernelShape)>();
        foreach (BrepEntity brep in _reachedShapes)
        {
            IKernelShape shape = brep.Shape;
            bool current = _tessellations.TryGetValue(brep, out ShapeMesh? known) && ReferenceEquals(known.Shape, shape);
            bool pending = _tessellating.TryGetValue(brep, out IKernelShape? started) && ReferenceEquals(started, shape);
            if (!current && !pending)
            {
                _tessellating[brep] = shape;
                shapes.Add((brep, shape));
            }
        }
        return shapes;
    }

    /// <summary>
    /// Takes a tessellation made in the background, unless its entity has another shape by now. Returns whether it was
    /// taken: the scene shows it at the next <see cref="Update"/>.
    /// </summary>
    public bool Accept(ShapeMesh result)
    {
        if (_tessellating.TryGetValue(result.Entity, out IKernelShape? started) && ReferenceEquals(started, result.Shape))
        {
            _tessellating.Remove(result.Entity);
        }
        if (!ReferenceEquals(result.Entity.Shape, result.Shape))
        {
            return false;
        }

        _tessellations[result.Entity] = result;
        return true;
    }

    /// <summary>
    /// Walks the model again, after changes of the document, and changes in the viewer that shows the scene the
    /// instances that differ. The selection must be shown again afterwards: removed instances may have been part of it.
    /// </summary>
    public void Update()
    {
        List<ViewportInstance> next = Build();
        if (_viewer is { } viewer)
        {
            ShowNewResources(viewer);
            _shownInstances = Synchronize(viewer, next);
        }

        _instances = next;
        ReleaseUnusedMeshes();
    }

    /// <summary>
    /// The instances that a node of the model tree places, given its path: the entities from the model to the node.
    /// All of them for the empty path, the model; none for a path that is not in the document.
    /// </summary>
    public Range InstancesOf(IReadOnlyList<Entity> path)
    {
        // Those of a node are consecutive, in the order of the tree.
        int first = _instances.FindIndex(instance => StartsWith(instance.Path, path));
        if (first < 0)
        {
            return default;
        }

        int end = first + 1;
        while (end < _instances.Count && StartsWith(_instances[end].Path, path))
        {
            end++;
        }
        return first..end;
    }

    /// <summary>The path of the leaf of the model tree that places an instance; null for an index out of range.</summary>
    public IReadOnlyList<Entity>? PathOf(int instance) =>
        instance >= 0 && instance < _instances.Count ? _instances[instance].Path : null;

    /// <summary>As <see cref="PathOf"/>, for an instance of the viewer that shows the scene; null for -1.</summary>
    public IReadOnlyList<Entity>? PathOfShown(int viewerInstance) =>
        viewerInstance < 0 ? null : PathOf(_shownInstances.IndexOf(viewerInstance));

    /// <summary>Shows the scene in a viewer, in place of what it showed, and turns the camera to it.</summary>
    /// <param name="images">The image of each texture, null for those that could not be decoded.</param>
    public void ShowIn(IViewerScene viewer, IReadOnlyList<TextureImage?> images)
    {
        viewer.Clear();
        _viewer = viewer;
        _shownMeshes.Clear();
        _shownTextures.Clear();
        _shownMaterials.Clear();
        _shownTextures.AddRange(images.Select(image => image is null ? -1 : viewer.AddTexture(image)));
        ShowNewResources(viewer);
        _shownInstances = _instances.Select(instance => Show(viewer, instance)).ToList();
        viewer.FitAll();
    }

    /// <summary>
    /// The element of a shape that a pick of the viewer that shows the scene finds; null when it finds no element, as
    /// on a mesh, picked as a whole, or on nothing.
    /// </summary>
    public ElementReference? ElementOf(ViewerPick pick)
    {
        ElementKind? kind = pick.Kind switch
        {
            NativeElementKind.Face => ElementKind.Face,
            NativeElementKind.Edge => ElementKind.Edge,
            NativeElementKind.Vertex => ElementKind.Vertex,
            _ => null,
        };
        return kind is { } known && PathOfShown(pick.Instance) is { } path && path[^1] is BrepEntity
            ? new ElementReference(path, known, pick.Index)
            : null;
    }

    /// <summary>
    /// Highlights elements as selected in the viewer that shows the scene, in place of those highlighted before; null
    /// for none. The elements that the scene does not show, as its shapes are being tessellated or no longer have them,
    /// are left out.
    /// </summary>
    public void ShowElementsIn(IViewerScene viewer, IEnumerable<ElementReference>? elements)
    {
        var shown = new List<ViewerElement>();
        foreach (ElementReference element in elements ?? [])
        {
            int instance = _instances.FindIndex(candidate => PathComparer.Instance.Equals(candidate.Path, element.Path));
            if (instance < 0 || _shownInstances[instance] < 0 ||
                _meshShapes[_instances[instance].Mesh] is not { } shape || !Has(shape, element))
            {
                continue;
            }

            NativeElementKind kind = element.Kind switch
            {
                ElementKind.Face => NativeElementKind.Face,
                ElementKind.Edge => NativeElementKind.Edge,
                _ => NativeElementKind.Vertex,
            };
            shown.Add(new ViewerElement(_shownInstances[instance], kind, element.Index));
        }
        viewer.SetHighlightedElements(shown.ToArray());

        static bool Has(ShapeTessellation shape, ElementReference element) => element.Kind switch
        {
            ElementKind.Face => element.Index < shape.FaceCount,
            ElementKind.Edge => shape.EdgeIndices.Contains(element.Index),
            _ => element.Index < shape.Vertices.Length,
        };
    }

    /// <summary>
    /// Shows as selected the instances of a node of the model tree, given its path, in the viewer that shows the scene;
    /// none for null.
    /// </summary>
    public void ShowSelectionIn(IViewerScene viewer, IReadOnlyList<Entity>? path)
    {
        int[] selected = path is null
            ? []
            : _shownInstances.Take(InstancesOf(path)).Where(instance => instance >= 0).ToArray();
        viewer.SetSelection(selected);
    }

    // The instances of the model as it is now, with the meshes and the materials they need.
    private List<ViewportInstance> Build()
    {
        var instances = new List<ViewportInstance>();
        _reachedShapes = [];
        var reached = new HashSet<BrepEntity>(ReferenceEqualityComparer.Instance);
        Visit(_root, Transform.Identity, inherited: null, [], instances, reached);

        // The tessellations of the shapes no longer in the model are of no use.
        foreach (BrepEntity gone in _tessellations.Keys.Where(brep => !reached.Contains(brep)).ToList())
        {
            _tessellations.Remove(gone);
        }
        return instances;
    }

    /// <param name="placement">From the coordinates of the block to those of the model space.</param>
    /// <param name="inherited">The material "by block": the one of the closest reference that has one.</param>
    private void Visit(
        Block block, Transform placement, Material? inherited, List<Entity> path, List<ViewportInstance> instances,
        HashSet<BrepEntity> reached)
    {
        foreach (Entity entity in block.Entities)
        {
            path.Add(entity);
            switch (entity)
            {
                case MeshEntity mesh when mesh.Triangles.Length > 0:
                    instances.Add(new(IndexOf(mesh, null), IndexOf(entity.Material ?? inherited), placement, [.. path]));
                    break;
                case BrepEntity brep:
                    if (reached.Add(brep))
                    {
                        _reachedShapes.Add(brep);
                    }
                    if (_tessellations.GetValueOrDefault(brep)?.Tessellation is { } tessellation)
                    {
                        instances.Add(new(
                            IndexOf(tessellation.Mesh, tessellation), IndexOf(entity.Material ?? inherited),
                            placement, [.. path]));
                    }
                    break;
                case BlockReference reference:
                    Visit(reference.Block, placement * reference.Transform, reference.Material ?? inherited, path,
                        instances, reached);
                    break;
            }
            path.RemoveAt(path.Count - 1);
        }
    }

    // The shapes that the model reaches, once each however often they are placed, in the order of the model.
    private List<BrepEntity> ReachedShapes()
    {
        var shapes = new List<BrepEntity>();
        var seenShapes = new HashSet<BrepEntity>(ReferenceEqualityComparer.Instance);
        var seenBlocks = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Block>([_root]);
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
        return shapes;
    }

    // The instances of the same path keep their index in the viewer, changed if they differ; the others are added or
    // removed. Returns the index in the viewer of each instance of next.
    private List<int> Synchronize(IViewerScene viewer, List<ViewportInstance> next)
    {
        var previous = new Dictionary<IReadOnlyList<Entity>, int>(PathComparer.Instance);
        for (int index = 0; index < _instances.Count; index++)
        {
            previous[_instances[index].Path] = index;
        }

        var shown = new List<int>(next.Count);
        foreach (ViewportInstance instance in next)
        {
            if (!previous.Remove(instance.Path, out int index))
            {
                shown.Add(Show(viewer, instance));
                continue;
            }

            int viewerInstance = _shownInstances[index];
            ViewportInstance before = _instances[index];
            if (before.Mesh == instance.Mesh && before.Material == instance.Material &&
                before.Placement == instance.Placement)
            {
                shown.Add(viewerInstance);
            }
            else if (viewerInstance < 0)
            {
                shown.Add(Show(viewer, instance));
            }
            else
            {
                shown.Add(Change(viewer, viewerInstance, instance));
            }
        }

        foreach (int index in previous.Values)
        {
            if (_shownInstances[index] >= 0)
            {
                viewer.RemoveInstance(_shownInstances[index]);
            }
        }
        return shown;
    }

    // A mesh or a placement that the viewer refuses, with positions that are not finite or a placement that flattens
    // the mesh, is left out, rather than the whole document.
    private int Show(IViewerScene viewer, ViewportInstance instance)
    {
        int mesh = _shownMeshes[instance.Mesh];
        if (mesh < 0)
        {
            return -1;
        }

        try
        {
            return viewer.AddInstance(mesh, _shownMaterials[instance.Material], instance.Placement);
        }
        catch (ArgumentException)
        {
            return -1;
        }
    }

    private int Change(IViewerScene viewer, int viewerInstance, ViewportInstance instance)
    {
        int mesh = _shownMeshes[instance.Mesh];
        if (mesh >= 0)
        {
            try
            {
                viewer.SetInstance(viewerInstance, mesh, _shownMaterials[instance.Material], instance.Placement);
                return viewerInstance;
            }
            catch (ArgumentException)
            {
                // Removed below, as the viewer refuses its new mesh or placement.
            }
        }

        viewer.RemoveInstance(viewerInstance);
        return -1;
    }

    // The textures, materials and meshes added to the scene since the viewer last received them.
    private void ShowNewResources(IViewerScene viewer)
    {
        for (int index = _shownTextures.Count; index < _textures.Count; index++)
        {
            _shownTextures.Add(TextureImage.Decode(_textures[index]) is { } image ? viewer.AddTexture(image) : -1);
        }
        for (int index = _shownMaterials.Count; index < _materials.Count; index++)
        {
            int texture = _materialTextures[index];
            _shownMaterials.Add(viewer.AddMaterial(_materials[index], texture < 0 ? -1 : _shownTextures[texture]));
        }
        for (int index = _shownMeshes.Count; index < _meshes.Count; index++)
        {
            int shown = -1;
            if (_meshes[index] is { } mesh)
            {
                try
                {
                    shown = viewer.AddMesh(mesh, _meshEdges[index], _meshShapes[index]);
                }
                catch (ArgumentException)
                {
                    // Left out, with the instances that place it.
                }
            }
            _shownMeshes.Add(shown);
        }
    }

    // The meshes that no instance places, such as the previous tessellation of a shape, are released, in the viewer
    // too. Their indices are not given again.
    private void ReleaseUnusedMeshes()
    {
        var used = new HashSet<int>(_instances.Select(instance => instance.Mesh));
        for (int index = 0; index < _meshes.Count; index++)
        {
            if (_meshes[index] is not { } mesh || used.Contains(index))
            {
                continue;
            }

            if (_viewer is { } viewer && _shownMeshes[index] >= 0)
            {
                viewer.RemoveMesh(_shownMeshes[index]);
            }
            _meshIndices.Remove(mesh);
            _meshes[index] = null;
            _meshEdges[index] = null;
            _meshShapes[index] = null;
        }
    }

    private static bool StartsWith(IReadOnlyList<Entity> path, IReadOnlyList<Entity> prefix)
    {
        if (prefix.Count > path.Count)
        {
            return false;
        }
        for (int index = 0; index < prefix.Count; index++)
        {
            if (!ReferenceEquals(path[index], prefix[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static string NameOf(BrepEntity brep) => brep.Name ?? brep.Material?.Name ?? "A shape";

    private int IndexOf(MeshEntity mesh, ShapeTessellation? shape)
    {
        if (!_meshIndices.TryGetValue(mesh, out int index))
        {
            index = _meshIndices[mesh] = _meshes.Count;
            _meshes.Add(mesh);
            // A mesh read from a file has no edges but those of its triangles: the sharp ones outline it.
            _meshEdges.Add(shape?.Edges ?? SharpEdges.Of(mesh.Vertices, mesh.Triangles));
            _meshShapes.Add(shape);
        }

        return index;
    }

    private int IndexOf(Material? material)
    {
        material ??= DefaultMaterial;
        if (!_materialIndices.TryGetValue(material, out int index))
        {
            index = _materialIndices[material] = _materials.Count;
            _materials.Add(material);
            _materialTextures.Add(material.BaseColorTexture is { } texture ? IndexOf(texture) : -1);
        }

        return index;
    }

    private int IndexOf(Texture texture)
    {
        if (!_textureIndices.TryGetValue(texture, out int index))
        {
            index = _textureIndices[texture] = _textures.Count;
            _textures.Add(texture);
        }

        return index;
    }

    /// <summary>Paths are equal when they are made of the same entities.</summary>
    private sealed class PathComparer : IEqualityComparer<IReadOnlyList<Entity>>
    {
        public static PathComparer Instance { get; } = new();

        public bool Equals(IReadOnlyList<Entity>? x, IReadOnlyList<Entity>? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.Count == y.Count && StartsWith(x, y));

        public int GetHashCode(IReadOnlyList<Entity> path)
        {
            var hash = new HashCode();
            foreach (Entity entity in path)
            {
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entity));
            }
            return hash.ToHashCode();
        }
    }
}

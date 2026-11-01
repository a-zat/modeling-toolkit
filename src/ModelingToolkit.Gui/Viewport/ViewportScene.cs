using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>A mesh placed in the model space with a material, as indices into the lists of a <see cref="ViewportScene"/>.</summary>
internal readonly record struct ViewportInstance(int Mesh, int Material, Transform Placement);

/// <summary>
/// What the 3D view shows of a document: each mesh, material and texture once, and an instance for each placement of a
/// mesh, through the block references that reach it from the model space. A mesh without a material takes the one of
/// the closest reference that has one, "by block", or <see cref="DefaultMaterial"/>.
/// </summary>
internal sealed class ViewportScene
{
    private ViewportScene(
        List<MeshEntity> meshes, List<Material> materials, List<int> materialTextures, List<Texture> textures,
        List<ViewportInstance> instances)
    {
        Meshes = meshes;
        Materials = materials;
        MaterialTextures = materialTextures;
        Textures = textures;
        Instances = instances;
    }

    /// <summary>A light grey, rather rough plastic.</summary>
    public static Material DefaultMaterial { get; } = new("Default")
    {
        BaseColor = new Color(0.42f, 0.45f, 0.5f),
        Roughness = 0.7f,
    };

    /// <summary>The meshes with triangles; meshes without any show nothing.</summary>
    public IReadOnlyList<MeshEntity> Meshes { get; }

    public IReadOnlyList<Material> Materials { get; }

    /// <summary>For each material, its base colour texture as an index into <see cref="Textures"/>, or -1.</summary>
    public IReadOnlyList<int> MaterialTextures { get; }

    public IReadOnlyList<Texture> Textures { get; }

    public IReadOnlyList<ViewportInstance> Instances { get; }

    public static ViewportScene Of(Document document)
    {
        var builder = new Builder();
        builder.Visit(document.Root, Transform.Identity, inherited: null);
        return new ViewportScene(builder.Meshes, builder.Materials, builder.MaterialTextures, builder.Textures,
            builder.Instances);
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
        int[] meshes = Meshes.Select(mesh =>
        {
            try
            {
                return viewer.AddMesh(mesh);
            }
            catch (ArgumentException)
            {
                return -1;
            }
        }).ToArray();
        foreach (ViewportInstance instance in Instances.Where(instance => meshes[instance.Mesh] >= 0))
        {
            try
            {
                viewer.AddInstance(meshes[instance.Mesh], materials[instance.Material], instance.Placement);
            }
            catch (ArgumentException)
            {
            }
        }

        viewer.FitAll();
    }

    private sealed class Builder
    {
        private readonly Dictionary<MeshEntity, int> _meshIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Material, int> _materialIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Texture, int> _textureIndices = new(ReferenceEqualityComparer.Instance);

        public List<MeshEntity> Meshes { get; } = [];

        public List<Material> Materials { get; } = [];

        public List<int> MaterialTextures { get; } = [];

        public List<Texture> Textures { get; } = [];

        public List<ViewportInstance> Instances { get; } = [];

        /// <param name="placement">From the coordinates of the block to those of the model space.</param>
        /// <param name="inherited">The material "by block": the one of the closest reference that has one.</param>
        public void Visit(Block block, Transform placement, Material? inherited)
        {
            foreach (Entity entity in block.Entities)
            {
                switch (entity)
                {
                    case MeshEntity mesh when mesh.Triangles.Length > 0:
                        Material material = mesh.Material ?? inherited ?? DefaultMaterial;
                        Instances.Add(new ViewportInstance(IndexOf(mesh), IndexOf(material), placement));
                        break;
                    case BlockReference reference:
                        Visit(reference.Block, placement * reference.Transform, reference.Material ?? inherited);
                        break;
                }
            }
        }

        private int IndexOf(MeshEntity mesh)
        {
            if (!_meshIndices.TryGetValue(mesh, out int index))
            {
                index = _meshIndices[mesh] = Meshes.Count;
                Meshes.Add(mesh);
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

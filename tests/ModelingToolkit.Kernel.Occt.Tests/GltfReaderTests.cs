using Microsoft.Extensions.Logging.Abstractions;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Kernel.Occt.Tests;

// Expected values computed independently from the glTF files: vertices through the node transformations, converted
// from metres and Y up to millimetres and Z up.
public sealed class GltfReaderTests
{
    private readonly GltfReader _reader = new(NullLogger<GltfReader>.Instance);

    [Fact]
    public void Box_is_read_in_millimetres_with_its_material()
    {
        Document document = _reader.Read(Asset("Box.glb"));

        AssertBox(new Point3d(-500, -500, -500), new Point3d(500, 500, 500), WorldBoundingBox(document));
        Material material = Assert.Single(document.Materials);
        Assert.Same(material, Assert.Single(Meshes(document)).Material);
    }

    [Fact]
    public void Meshes_compressed_by_draco_are_decoded()
    {
        Document document = _reader.Read(Asset(Path.Combine("draco", "Box.gltf")));

        // Draco quantizes the positions.
        AssertBox(new Point3d(-500, -500, -500), new Point3d(500, 500, 500), WorldBoundingBox(document), 1);
        MeshEntity mesh = Assert.Single(Meshes(document));
        Assert.Equal(12, mesh.Triangles.Length);
        Assert.Same(Assert.Single(document.Materials), mesh.Material);
    }

    [Fact]
    public void Hierarchy_and_shared_meshes_are_preserved()
    {
        Document document = _reader.Read(Asset("CesiumMilkTruck.glb"));

        AssertBox(
            new Point3d(-1395.999944, -2437.999853, 1.451834),
            new Point3d(1395.999944, 2430.910063, 2584.369807),
            WorldBoundingBox(document));

        Block wheels = Assert.Single(document.Blocks, block => block.Name == "Wheels");
        Assert.Equal(2, References(document).Count(reference => reference.Block == wheels));
    }

    [Fact]
    public void Materials_share_the_textures_of_the_file()
    {
        Document document = _reader.Read(Asset("CesiumMilkTruck.glb"));

        Assert.Equal(["wheels", "truck", "glass", "window_trim"], document.Materials.Select(material => material.Name));
        Texture texture = Assert.Single(document.Textures);
        Assert.Equal("image/jpeg", texture.MediaType);
        Assert.Same(texture, document.Materials.Single(material => material.Name == "wheels").BaseColorTexture);
        Assert.Same(texture, document.Materials.Single(material => material.Name == "truck").BaseColorTexture);
    }

    [Fact]
    public void Normals_and_texture_coordinates_are_read()
    {
        MeshEntity mesh = Assert.Single(Meshes(_reader.Read(Asset("BoxTextured.glb"))));

        Assert.Equal(mesh.Vertices.Length, mesh.Normals?.Length);
        Assert.Equal(mesh.Vertices.Length, mesh.TextureCoordinates?.Length);
    }

    [Fact]
    public void Meshes_without_material_take_it_by_block()
    {
        Document document = _reader.Read(Asset("SimpleMeshes.gltf"));

        Assert.Empty(document.Materials);
        Assert.Null(Assert.Single(Meshes(document)).Material);
        AssertBox(new Point3d(0, 0, 0), new Point3d(2000, 0, 1000), WorldBoundingBox(document));
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        Assert.Throws<FileNotFoundException>(() => _reader.Read(Asset("Missing.glb")));
    }

    [Fact]
    public void Invalid_file_is_reported()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.glb");
        File.WriteAllText(path, "not a glTF file");
        try
        {
            Assert.Throws<InvalidDataException>(() => _reader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "gltf", name);

    private static IEnumerable<Entity> Entities(Document document) =>
        document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities);

    private static IEnumerable<MeshEntity> Meshes(Document document) => Entities(document).OfType<MeshEntity>();

    private static IEnumerable<BlockReference> References(Document document) =>
        Entities(document).OfType<BlockReference>();

    // Box of the transformed vertices; Document.GetBoundingBox can be larger when references are rotated.
    private static BoundingBox WorldBoundingBox(Document document)
    {
        BoundingBox box = BoundingBox.Empty;
        Visit(document.Root, Transform.Identity);
        return box;

        void Visit(Block block, Transform transform)
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
                else if (entity is BlockReference reference)
                {
                    Visit(reference.Block, transform * reference.Transform);
                }
            }
        }
    }

    private static void AssertBox(Point3d min, Point3d max, BoundingBox actual, double tolerance = 1e-3)
    {
        Assert.Equal(min.X, actual.Min.X, tolerance);
        Assert.Equal(min.Y, actual.Min.Y, tolerance);
        Assert.Equal(min.Z, actual.Min.Z, tolerance);
        Assert.Equal(max.X, actual.Max.X, tolerance);
        Assert.Equal(max.Y, actual.Max.Y, tolerance);
        Assert.Equal(max.Z, actual.Max.Z, tolerance);
    }
}

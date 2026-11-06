using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Kernel.Occt.Tests;

// Expected values computed independently from the glTF files: vertices through the node transformations, converted
// from metres and Y up to millimetres and Z up.
public sealed class GltfReaderTests : IDisposable
{
    private readonly GltfReader _reader = new();
    private readonly List<string> _warnings = [];
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-gltf-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Box_is_read_in_millimetres_with_its_material()
    {
        Document document = Read(Asset("Box.glb"));

        AssertBox(new Point3d(-500, -500, -500), new Point3d(500, 500, 500), WorldBoundingBox(document));
        Material material = Assert.Single(document.Materials);
        Assert.Same(material, Assert.Single(Meshes(document)).Material);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Meshes_compressed_by_draco_are_decoded()
    {
        Document document = Read(Asset(Path.Combine("draco", "Box.gltf")));

        // Draco quantizes the positions.
        AssertBox(new Point3d(-500, -500, -500), new Point3d(500, 500, 500), WorldBoundingBox(document), 1);
        MeshEntity mesh = Assert.Single(Meshes(document));
        Assert.Equal(12, mesh.Triangles.Length);
        Assert.Same(Assert.Single(document.Materials), mesh.Material);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Textures_in_webp_are_read_as_they_are()
    {
        Document document = Read(Asset(Path.Combine("webp", "BoxTextured.gltf")));

        Texture texture = Assert.Single(document.Textures);
        Assert.Equal("image/webp", texture.MediaType);
        Assert.Equal(File.ReadAllBytes(Asset(Path.Combine("webp", "Dot_C.webp"))), texture.Data);
        Assert.Same(texture, Assert.Single(document.Materials).BaseColorTexture);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData(".gltf")]
    [InlineData(".glb")]
    public void Files_that_require_unsupported_geometry_extensions_are_refused(string extension)
    {
        string path = WithExtensions(extension, required: ["EXT_meshopt_compression", "EXT_mesh_gpu_instancing"]);

        var exception = Assert.Throws<InvalidDataException>(() => Read(path));
        Assert.EndsWith("not supported: EXT_mesh_gpu_instancing, EXT_meshopt_compression.", exception.Message);
    }

    [Fact]
    public void Unsupported_texture_extensions_leave_out_only_textures()
    {
        string path = WithExtensions(".glb", required: ["KHR_texture_basisu"], used: ["KHR_texture_transform"]);

        Document document = Read(path);

        Assert.Single(Meshes(document));
        Assert.Equal(
        [
            "Unsupported glTF extension KHR_texture_basisu: the textures or materials that need it are left out.",
            "Unsupported glTF extension KHR_texture_transform: it is ignored.",
        ], _warnings);
    }

    [Fact]
    public void Hierarchy_and_shared_meshes_are_preserved()
    {
        Document document = Read(Asset("CesiumMilkTruck.glb"));

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
        Document document = Read(Asset("CesiumMilkTruck.glb"));

        Assert.Equal(["wheels", "truck", "glass", "window_trim"], document.Materials.Select(material => material.Name));
        Texture texture = Assert.Single(document.Textures);
        Assert.Equal("image/jpeg", texture.MediaType);
        Assert.Same(texture, document.Materials.Single(material => material.Name == "wheels").BaseColorTexture);
        Assert.Same(texture, document.Materials.Single(material => material.Name == "truck").BaseColorTexture);
    }

    [Fact]
    public void Normals_and_texture_coordinates_are_read()
    {
        MeshEntity mesh = Assert.Single(Meshes(Read(Asset("BoxTextured.glb"))));

        Assert.Equal(mesh.Vertices.Length, mesh.Normals?.Length);
        Assert.Equal(mesh.Vertices.Length, mesh.TextureCoordinates?.Length);
    }

    [Fact]
    public void Meshes_without_material_take_it_by_block()
    {
        Document document = Read(Asset("SimpleMeshes.gltf"));

        Assert.Empty(document.Materials);
        Assert.Null(Assert.Single(Meshes(document)).Material);
        AssertBox(new Point3d(0, 0, 0), new Point3d(2000, 0, 1000), WorldBoundingBox(document));
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        Assert.Throws<FileNotFoundException>(() => Read(Asset("Missing.glb")));
    }

    [Fact]
    public void Invalid_file_is_reported()
    {
        string path = Path.Combine(_folder, "text.glb");
        File.WriteAllText(path, "not a glTF file");

        Assert.Throws<InvalidDataException>(() => Read(path));
    }

    private Document Read(string path) => _reader.Read(path, _warnings);

    // SimpleMeshes, whose buffers are embedded, listing the given extensions; a .glb holds it as its only chunk.
    private string WithExtensions(string extension, string[] required, string[]? used = null)
    {
        JsonNode json = JsonNode.Parse(File.ReadAllText(Asset("SimpleMeshes.gltf")))!;
        json["extensionsRequired"] = new JsonArray([.. required.Select(name => JsonValue.Create(name))]);
        json["extensionsUsed"] = new JsonArray([.. required.Concat(used ?? []).Select(name => JsonValue.Create(name))]);
        string path = Path.Combine(_folder, "model" + extension);
        if (extension == ".gltf")
        {
            File.WriteAllText(path, json.ToJsonString());
            return path;
        }

        byte[] text = Encoding.UTF8.GetBytes(json.ToJsonString());
        int chunkLength = (text.Length + 3) / 4 * 4;
        byte[] glb = new byte[20 + chunkLength];
        BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)glb.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)chunkLength);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        text.CopyTo(glb, 20);
        glb.AsSpan(20 + text.Length).Fill((byte)' ');
        File.WriteAllBytes(path, glb);
        return path;
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

using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.IO.Native.Tests;

public sealed class NativeFormatTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-tests-").FullName;
    private readonly NativeDocumentWriter _writer = new();
    private readonly NativeDocumentReader _reader = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_document_survives_a_round_trip()
    {
        Document original = SampleDocument();
        string path = PathOf("sample.mtk");

        _writer.Write(original, path);
        Document copy = _reader.Read(path);

        Texture texture = Assert.Single(copy.Textures);
        Assert.Equal(("bricks", "image/png"), (texture.Name, texture.MediaType));
        Assert.Equal([1, 2, 3], texture.Data);

        Material material = Assert.Single(copy.Materials);
        Assert.Equal("brick", material.Name);
        Assert.Equal(new Color(0.5f, 0.25f, 0.125f, 0.75f), material.BaseColor);
        Assert.Equal((0.1f, 0.9f), (material.Metallic, material.Roughness));
        Assert.Same(texture, material.BaseColorTexture);

        Block leg = Assert.Single(copy.Blocks);
        Assert.Equal("Leg", leg.Name);
        MeshEntity legMesh = Assert.IsType<MeshEntity>(Assert.Single(leg.Entities));
        MeshEntity originalMesh = (MeshEntity)original.Blocks[0].Entities[0];
        Assert.Equal(originalMesh.Vertices, legMesh.Vertices);
        Assert.Equal(originalMesh.Triangles, legMesh.Triangles);
        Assert.Equal(originalMesh.Normals, legMesh.Normals);
        Assert.Equal(originalMesh.TextureCoordinates, legMesh.TextureCoordinates);
        Assert.Same(material, legMesh.Material);

        Assert.Equal("Table", copy.Root.Name);
        Assert.Collection(
            copy.Root.Entities,
            entity =>
            {
                var reference = Assert.IsType<BlockReference>(entity);
                Assert.Equal(("Front leg", Transform.Translation(0, 0, 0)), (reference.Name, reference.Transform));
                Assert.Same(leg, reference.Block);
            },
            entity =>
            {
                var reference = Assert.IsType<BlockReference>(entity);
                Assert.Equal(("Back leg", RotatedAndMoved), (reference.Name, reference.Transform));
                Assert.Same(leg, reference.Block);
            },
            entity =>
            {
                var mesh = Assert.IsType<MeshEntity>(entity);
                Assert.Null(mesh.Material);
                Assert.Null(mesh.Normals);
                Assert.Null(mesh.TextureCoordinates);
            });
    }

    [Fact]
    public void The_manifest_is_versioned_json()
    {
        string path = PathOf("sample.mtk");
        _writer.Write(SampleDocument(), path);

        using ZipArchive archive = ZipFile.OpenRead(path);
        using Stream stream = archive.GetEntry("manifest.json")!.Open();
        using JsonDocument manifest = JsonDocument.Parse(stream);
        Assert.Equal("modeling-toolkit", manifest.RootElement.GetProperty("format").GetString());
        Assert.Equal(1, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("mm", manifest.RootElement.GetProperty("units").GetString());
    }

    [Fact]
    public void Objects_outside_the_document_cannot_be_written()
    {
        Document document = SampleDocument();
        document.Materials.Clear();
        string path = PathOf("invalid.mtk");

        Assert.Throws<InvalidOperationException>(() => _writer.Write(document, path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
    }

    [Fact]
    public void Files_that_are_not_archives_are_rejected()
    {
        string path = PathOf("text.mtk");
        File.WriteAllText(path, "not an archive");

        Assert.Throws<InvalidDataException>(() => _reader.Read(path));
    }

    [Fact]
    public void Newer_versions_are_rejected()
    {
        string path = PathOf("future.mtk");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
        {
            writer.Write("""
                {"format": "modeling-toolkit", "version": 99, "units": "mm", "upAxis": "Z", "textures": [],
                 "materials": [], "root": {"name": "Model", "entities": []}, "blocks": []}
                """);
        }

        Assert.Throws<InvalidDataException>(() => _reader.Read(path));
    }

    private static readonly Transform RotatedAndMoved = new(
        0, -1, 0, 1000.5,
        1, 0, 0, -20.25,
        0, 0, 1, 0.125);

    private string PathOf(string name) => Path.Combine(_folder, name);

    // A table: one leg block placed twice, plus a top without material, normals or texture coordinates.
    private static Document SampleDocument()
    {
        var document = new Document();
        document.Root.Name = "Table";

        var texture = new Texture("bricks", "image/png", [1, 2, 3]);
        document.Textures.Add(texture);
        var material = new Material("brick")
        {
            BaseColor = new Color(0.5f, 0.25f, 0.125f, 0.75f),
            Metallic = 0.1f,
            Roughness = 0.9f,
            BaseColorTexture = texture,
        };
        document.Materials.Add(material);

        var leg = new Block("Leg");
        leg.Entities.Add(new MeshEntity(
            [new(0, 0, 0), new(50, 0, 0), new(0, 50, 700.123456789)],
            [new Triangle(0, 1, 2)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX],
            [new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1)])
        {
            Material = material,
        });
        document.Blocks.Add(leg);

        document.Root.Entities.Add(new BlockReference(leg, Transform.Translation(0, 0, 0)) { Name = "Front leg" });
        document.Root.Entities.Add(new BlockReference(leg, RotatedAndMoved) { Name = "Back leg" });
        document.Root.Entities.Add(new MeshEntity([new(0, 0, 700), new(1000, 0, 700), new(0, 500, 700)],
            [new Triangle(0, 1, 2)]));
        return document;
    }
}

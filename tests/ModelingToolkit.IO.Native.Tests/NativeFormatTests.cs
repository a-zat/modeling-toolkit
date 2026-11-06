using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt;

namespace ModelingToolkit.IO.Native.Tests;

public sealed class NativeFormatTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-tests-").FullName;
    private readonly NativeDocumentWriter _writer = new();
    private readonly OcctKernel _kernel = new();
    private readonly NativeDocumentReader _reader;

    public NativeFormatTests() => _reader = new NativeDocumentReader(_kernel);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_document_survives_a_round_trip()
    {
        Document original = SampleDocument();
        string path = PathOf("sample.mtk");

        _writer.Write(original, path);
        Document copy = _reader.Read(path, []);

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
    public void Reading_and_writing_report_their_progress_to_the_end()
    {
        string path = PathOf("sample.mtk");
        var writing = new RecordingProgress();
        var reading = new RecordingProgress();

        _writer.Write(SampleDocument(), path, writing);
        _reader.Read(path, [], reading);

        foreach (RecordingProgress progress in new[] { writing, reading })
        {
            Assert.NotEmpty(progress.Reports);
            Assert.Equal(progress.Reports.Order(), progress.Reports);
            Assert.Equal(1, progress.Reports[^1]);
        }
    }

    [Fact]
    public void A_cancelled_writing_leaves_the_file_as_it_was()
    {
        string path = PathOf("sample.mtk");
        File.WriteAllText(path, "before");
        using var cancelling = new CancellationTokenSource();

        Assert.Throws<OperationCanceledException>(() => _writer.Write(
            SampleDocument(), path, new RecordingProgress(_ => cancelling.Cancel()), cancelling.Token));

        Assert.Equal("before", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_cancelled_reading_throws()
    {
        string path = PathOf("sample.mtk");
        _writer.Write(SampleDocument(), path);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => _reader.Read(path, [], cancellationToken: cancelled.Token));
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
        Assert.Equal(2, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("mm", manifest.RootElement.GetProperty("units").GetString());
    }

    [Fact]
    public void B_rep_shapes_survive_a_round_trip_with_their_placement_names_and_materials()
    {
        var steel = new Material("steel");
        var document = new Document();
        document.Materials.Add(steel);
        using var cylinder = _kernel.MakeCylinder(10, 40);
        var shaftEntity = new BrepEntity(_kernel, _kernel.Place(cylinder, RotatedAndMoved))
        {
            Name = "Shaft",
            Material = steel,
        };
        var pin = new Block("Pin") { Entities = { shaftEntity } };
        document.Blocks.Add(pin);
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(10, 20, 30)));
        document.Root.Entities.Add(new BlockReference(pin, Transform.Translation(500, 0, 0)));
        string path = PathOf("shapes.mtk");

        _writer.Write(document, path);
        Document copy = _reader.Read(path, []);

        Material material = Assert.Single(copy.Materials);
        BrepEntity box = Assert.IsType<BrepEntity>(copy.Root.Entities[0]);
        Assert.Null(box.Name);
        Assert.Null(box.Material);
        Assert.Equal(6000, _kernel.ComputeVolume(box.Shape), precision: 6);
        Assert.Equal(new Point3d(10, 20, 30), box.GetBoundingBox().Max);

        BlockReference reference = Assert.IsType<BlockReference>(copy.Root.Entities[1]);
        BrepEntity shaft = Assert.IsType<BrepEntity>(Assert.Single(reference.Block.Entities));
        Assert.Equal("Shaft", shaft.Name);
        Assert.Same(material, shaft.Material);
        Assert.Equal(Math.PI * 10 * 10 * 40, _kernel.ComputeVolume(shaft.Shape), precision: 3);
        BoundingBox expected = shaftEntity.GetBoundingBox();
        BoundingBox actual = shaft.GetBoundingBox();
        Assert.Equal(expected.Min.X, actual.Min.X, tolerance: 1e-6);
        Assert.Equal(expected.Max.Y, actual.Max.Y, tolerance: 1e-6);
        Assert.Equal(expected.Max.Z, actual.Max.Z, tolerance: 1e-6);

        using ZipArchive archive = ZipFile.OpenRead(path);
        Assert.NotNull(archive.GetEntry("shapes/0.brep"));
        Assert.NotNull(archive.GetEntry("shapes/1.brep"));
        Assert.Null(archive.GetEntry("shapes/2.brep"));
    }

    [Fact]
    public void Files_of_version_one_are_still_read()
    {
        string path = Archive("old.mtk", """
            {"format": "modeling-toolkit", "version": 1, "units": "mm", "upAxis": "Z", "textures": [],
             "materials": [], "root": {"name": "Old", "entities": []}, "blocks": []}
            """);

        Assert.Equal("Old", _reader.Read(path, []).Root.Name);
    }

    [Fact]
    public void A_shape_file_that_is_not_a_shape_makes_the_file_invalid()
    {
        var document = new Document();
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(1, 1, 1)));
        string path = PathOf("broken.mtk");
        _writer.Write(document, path);
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            archive.GetEntry("shapes/0.brep")!.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("shapes/0.brep").Open());
            writer.Write("this is not a shape");
        }

        var exception = Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));

        Assert.Contains("not a shape", exception.Message);
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

        Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
    }

    [Theory]
    [InlineData(3)] // newer than this reader
    [InlineData(0)]
    [InlineData(-1)]
    public void Versions_that_this_reader_does_not_know_are_rejected(int version)
    {
        string path = Archive("unknown.mtk", Manifest($$"""{"version": {{version}}}"""));

        var exception = Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));

        Assert.Contains($"version {version}", exception.Message);
    }

    [Theory]
    [InlineData("""{"textures": null}""")]
    [InlineData("""{"materials": [null]}""")]
    [InlineData("""{"materials": [{"name": "m", "metallic": 0, "roughness": 1}]}""")]
    [InlineData("""{"root": null}""")]
    [InlineData("""{"root": {"name": "Model"}}""")]
    [InlineData("""{"root": {"name": "Model", "entities": [null]}}""")]
    [InlineData("""{"blocks": [{"name": "b", "entities": []}], "root": {"name": "Model", "entities": [{"type": "blockReference", "block": 0}]}}""")]
    public void Manifests_without_what_the_format_requires_make_the_file_invalid(string changes)
    {
        string path = Archive("incomplete.mtk", Manifest(changes));

        Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
    }

    [Theory]
    [InlineData(3, 1)] // the data of the sample mesh
    [InlineData(1_000_000_000, 1)]
    [InlineData(-1, 1)]
    [InlineData(3, int.MaxValue)]
    public void A_mesh_is_read_only_if_its_file_holds_what_the_manifest_says(int vertexCount, int triangleCount)
    {
        byte[] data = new byte[3 * 24 + 12];
        new[] { 0, 1, 2 }.CopyTo(MemoryMarshal.Cast<byte, int>(data.AsSpan(3 * 24)));
        string path = Archive("mesh.mtk", Manifest($$$"""
            {"root": {"name": "Model", "entities": [{"type": "mesh", "path": "meshes/0.bin",
             "vertexCount": {{{vertexCount}}}, "triangleCount": {{{triangleCount}}}, "hasNormals": false,
             "hasTextureCoordinates": false}]}}
            """), ("meshes/0.bin", data));

        if (vertexCount == 3 && triangleCount == 1)
        {
            Assert.IsType<MeshEntity>(Assert.Single(_reader.Read(path, []).Root.Entities));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
        }
    }

    [Fact]
    public void Writing_into_a_folder_that_does_not_exist_reports_it()
    {
        string path = Path.Combine(_folder, "missing", "model.mtk");

        Assert.Throws<DirectoryNotFoundException>(() => _writer.Write(SampleDocument(), path));
    }

    // A valid manifest of an empty document, with the properties of changes replacing its own.
    private static string Manifest(string changes)
    {
        var manifest = (JsonObject)JsonNode.Parse("""
            {"format": "modeling-toolkit", "version": 2, "units": "mm", "upAxis": "Z", "textures": [],
             "materials": [], "root": {"name": "Model", "entities": []}, "blocks": []}
            """)!;
        foreach ((string name, JsonNode? value) in
                 JsonNode.Parse(changes)!.AsObject())
        {
            manifest[name] = value?.DeepClone();
        }
        return manifest.ToJsonString();
    }

    // An archive with the manifest and the files given.
    private string Archive(string name, string manifest, params (string Path, byte[] Data)[] files)
    {
        string path = PathOf(name);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
        {
            writer.Write(manifest);
        }
        foreach ((string filePath, byte[] data) in files)
        {
            using Stream stream = archive.CreateEntry(filePath).Open();
            stream.Write(data);
        }
        return path;
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

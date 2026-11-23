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
        Assert.Equal(250, material.TextureSize);

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
        Assert.Equal(7, manifest.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("mm", manifest.RootElement.GetProperty("units").GetString());
    }

    [Fact]
    public void B_rep_shapes_survive_a_round_trip_with_their_placement_names_and_materials()
    {
        var steel = new Material("steel");
        var document = new Document();
        document.Materials.Add(steel);
        using var cylinder = _kernel.MakeCylinder(10, 40);
        var shaftEntity = new BrepEntity(
            _kernel, _kernel.Place(cylinder, RotatedAndMoved), new Dictionary<int, Material> { [0] = steel, [2] = steel })
        {
            Name = "Shaft",
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
        Assert.Empty(box.FaceMaterials);
        Assert.Equal(6000, _kernel.ComputeVolume(box.Shape), precision: 6);
        Assert.Equal(new Point3d(10, 20, 30), box.GetBoundingBox().Max);

        BlockReference reference = Assert.IsType<BlockReference>(copy.Root.Entities[1]);
        BrepEntity shaft = Assert.IsType<BrepEntity>(Assert.Single(reference.Block.Entities));
        Assert.Equal("Shaft", shaft.Name);
        Assert.Equal([0, 2], shaft.FaceMaterials.Keys.Order());
        Assert.All(shaft.FaceMaterials.Values, painted => Assert.Same(material, painted));
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)] // the materials were of entities
    public void Files_of_older_versions_are_no_longer_read(int version)
    {
        string path = Archive("old.mtk", Manifest($$"""{"version": {{version}}}"""));

        var exception = Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));

        Assert.Contains($"version {version} is no longer read", exception.Message);
    }

    [Fact]
    public void Guides_survive_a_round_trip_and_files_of_version_3_are_still_read()
    {
        var document = new Document();
        document.Root.Entities.Add(new GuideEntity(new Point3d(1, 2, 3), new Vector3d(0, 2, 0)) { Name = "Edge" });
        string path = PathOf("guides.mtk");
        _writer.Write(document, path);

        var guide = Assert.IsType<GuideEntity>(Assert.Single(_reader.Read(path, []).Root.Entities));
        Assert.Equal((new Point3d(1, 2, 3), new Vector3d(0, 1, 0), "Edge"), (guide.Point, guide.Direction, guide.Name));

        string old = Archive("version3.mtk", Manifest("{}"));
        Assert.Empty(_reader.Read(old, []).Root.Entities);
    }

    [Fact]
    public void Anchors_and_attachments_survive_a_round_trip()
    {
        var document = new Document();
        var leaf = new Block("Leaf");
        var anchor = new AnchorEntity(Transform.Translation(450, 0, 0)) { Name = "End" };
        leaf.Entities.Add(anchor);
        var seat = new Block("Chair");
        seat.Entities.Add(new AnchorEntity(Transform.Translation(0, 0, 1200), AnchorKind.PointOfView));
        document.Blocks.AddRange([leaf, seat]);
        var leafReference = new BlockReference(leaf, Transform.Translation(900, 0, 0));
        var chair = new BlockReference(seat, Transform.Translation(1500, 0, 0));
        document.Root.Entities.Add(leafReference);
        document.Root.Entities.Add(chair);
        document.Attach(new Attachment(chair, [leafReference], anchor, Transform.Translation(150, 0, 0)));
        string path = PathOf("anchors.mtk");

        _writer.Write(document, path);
        Document copy = _reader.Read(path, []);

        var copiedAnchor = Assert.IsType<AnchorEntity>(copy.Blocks[0].Entities[0]);
        Assert.Equal(("End", AnchorKind.Attach, new Point3d(450, 0, 0)), (copiedAnchor.Name, copiedAnchor.Kind, copiedAnchor.Point));
        Assert.Equal(AnchorKind.PointOfView, Assert.IsType<AnchorEntity>(copy.Blocks[1].Entities[0]).Kind);
        Attachment attachment = Assert.Single(copy.Attachments);
        Assert.Same(copy.Root.Entities[1], attachment.Follower);
        Assert.Same(copy.Root.Entities[0], Assert.Single(attachment.Chain));
        Assert.Same(copiedAnchor, attachment.Anchor);
        Assert.Equal(Transform.Translation(150, 0, 0), attachment.Offset);
    }

    [Fact]
    public void Layers_survive_a_round_trip_with_the_entities_on_them()
    {
        var document = new Document();
        var living = new Layer("Living room") { IsHidden = true };
        document.AddLayer(new Layer("Kitchen"));
        document.AddLayer(living);
        document.Root.Entities.Add(new MeshEntity([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new(0, 1, 2)]) { Layer = living });
        document.Root.Entities.Add(new MeshEntity([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [new(0, 1, 2)]));
        string path = PathOf("layers.mtk");

        _writer.Write(document, path);
        Document copy = _reader.Read(path, []);

        Assert.Equal(["Kitchen", "Living room"], copy.Layers.Select(layer => layer.Name));
        Assert.True(copy.Layers[1].IsHidden);
        Assert.Equal([copy.Layers[1], null], copy.Root.Entities.Select(entity => entity.Layer));
    }

    [Fact]
    public void Animations_survive_a_round_trip_without_the_motions_of_references_no_longer_in_the_document()
    {
        var document = new Document();
        var part = new Block("Part");
        document.Blocks.Add(part);
        var door = new BlockReference(part, Transform.Translation(1, 2, 3)) { Name = "Door" };
        var hinge = new Block("Hinge");
        document.Blocks.Add(hinge);
        hinge.Entities.Add(door);
        document.Root.Entities.Add(new BlockReference(hinge, Transform.Identity));
        var drawer = new BlockReference(part, Transform.Identity);
        document.Root.Entities.Add(drawer);
        var gone = new BlockReference(part, Transform.Identity);
        document.Animations.Add(new Animation("Open", [
            new AnimationChannel(door, AnimatedProperty.Rotation, AnimationInterpolation.Linear,
                [0, 1.5], [0, 0, 0, 1, 0, 0, 1, 0]),
            new AnimationChannel(drawer, AnimatedProperty.Translation, AnimationInterpolation.CubicSpline,
                [0], [0, 0, 0, 1, 2, 3, 0, 0, 0]),
            new AnimationChannel(gone, AnimatedProperty.Scale, AnimationInterpolation.Step, [0], [1, 1, 1]),
        ]));
        document.Animations.Add(new Animation("Lost", [
            new AnimationChannel(gone, AnimatedProperty.Scale, AnimationInterpolation.Step, [0], [1, 1, 1]),
        ]));
        string path = PathOf("animations.mtk");

        _writer.Write(document, path);
        Document copy = _reader.Read(path, []);

        Animation open = Assert.Single(copy.Animations);
        Assert.Equal("Open", open.Name);
        Assert.Equal(2, open.Channels.Count);
        AnimationChannel rotation = open.Channels[0];
        Assert.Same(copy.Blocks[1].Entities[0], rotation.Target);
        Assert.Equal(
            (AnimatedProperty.Rotation, AnimationInterpolation.Linear),
            (rotation.Property, rotation.Interpolation));
        Assert.Equal([0, 1.5], rotation.Times);
        Assert.Equal([0, 0, 0, 1, 0, 0, 1, 0], rotation.Values);
        AnimationChannel translation = open.Channels[1];
        Assert.Same(copy.Root.Entities[1], translation.Target);
        Assert.Equal(AnimationInterpolation.CubicSpline, translation.Interpolation);
        Assert.Equal([0, 0, 0, 1, 2, 3, 0, 0, 0], translation.Values);
    }

    [Fact]
    public void An_animation_of_an_entity_that_is_not_a_reference_makes_the_file_invalid()
    {
        string path = Archive("animated.mtk", Manifest("""
            {"version": 5, "root": {"name": "Model", "entities": [
                {"type": "guide", "point": [0, 0, 0], "direction": [1, 0, 0]}]},
             "animations": [{"name": "Wrong", "channels": [{"entity": 0, "property": "rotation",
                "interpolation": "linear", "times": [0], "values": [0, 0, 0, 1]}]}]}
            """));

        var exception = Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));

        Assert.Contains("not a block reference", exception.Message);
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
    public void A_face_painted_with_a_material_that_the_file_lacks_makes_it_invalid()
    {
        var document = new Document();
        var paint = new Material("Paint");
        document.Materials.Add(paint);
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(1, 1, 1), new Dictionary<int, Material> { [3] = paint }));
        string path = PathOf("unpainted.mtk");
        _writer.Write(document, path);
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.GetEntry("manifest.json")!;
            JsonNode manifest;
            using (Stream stream = entry.Open())
            {
                manifest = JsonNode.Parse(stream)!;
            }
            manifest["materials"] = new JsonArray();
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            writer.Write(manifest.ToJsonString());
        }

        Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
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
    [InlineData(8)] // newer than this reader
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

    [Fact]
    public void An_empty_document_survives_a_round_trip()
    {
        string path = PathOf("empty.mtk");

        _writer.Write(new Document(), path);
        Document copy = _reader.Read(path, []);

        Assert.Equal("Model", copy.Root.Name);
        Assert.Empty(copy.Root.Entities);
        Assert.Empty(copy.Blocks);
        Assert.Empty(copy.Materials);
        Assert.Empty(copy.Textures);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(0.999)]
    public void A_truncated_file_is_invalid(double kept)
    {
        string path = PathOf("truncated.mtk");
        _writer.Write(SampleDocument(), path);
        using (FileStream stream = File.Open(path, FileMode.Open))
        {
            stream.SetLength((long)(stream.Length * kept));
        }

        Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("meshes/0.bin")]
    [InlineData("shapes/0.brep")]
    public void A_file_with_a_corrupted_entry_is_invalid(string entry)
    {
        Document document = SampleDocument();
        document.Root.Entities.Add(new BrepEntity(_kernel, _kernel.MakeBox(1, 2, 3)));
        string path = PathOf("corrupted.mtk");
        _writer.Write(document, path);
        long offset;
        using (ZipArchive archive = ZipFile.OpenRead(path))
        {
            offset = DataOffsetOf(path, archive.GetEntry(entry)!);
        }
        using (FileStream stream = File.Open(path, FileMode.Open))
        {
            // Bytes in the middle of the compressed data of the entry turned into others.
            stream.Position = offset + 8;
            Span<byte> bytes = stackalloc byte[8];
            stream.ReadExactly(bytes);
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] ^= 0x5A;
            }
            stream.Position = offset + 8;
            stream.Write(bytes);
        }

        Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("""{"root": {"name": "Model", "entities": [{"type": "nurbs"}]}}""")]
    [InlineData("""{"root": {"name": "Model", "entities": [{"type": "brep", "path": "shapes/9.brep"}]}}""")]
    [InlineData("""{"root": {"name": "Model", "entities": [{"type": "blockReference", "block": 0, "transform": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]}]}}""")]
    [InlineData("""{"root": {"name": "Model", "entities": [{"type": "blockReference", "block": 0, "transform": [1, 0, 0]}]}, "blocks": [{"name": "b", "entities": []}]}""")]
    [InlineData("""{"materials": [{"name": "m", "baseColor": [1, 1, 1, 1], "metallic": 0, "roughness": 1, "baseColorTexture": 0}]}""")]
    [InlineData("""{"materials": [{"name": "m", "baseColor": [1, 1, 1], "metallic": 0, "roughness": 1}]}""")]
    [InlineData("""{"blocks": [{"name": "b", "entities": [{"type": "blockReference", "block": 0, "transform": [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]}]}]}""")]
    [InlineData("""{"format": "another-format"}""")]
    [InlineData("""{"units": "inch"}""")]
    public void A_manifest_that_does_not_hold_together_makes_the_file_invalid(string changes)
    {
        string path = Archive("invalid.mtk", changes.StartsWith('{') && changes.EndsWith('}') ? Manifest(changes) : changes);

        Assert.Throws<InvalidDataException>(() => _reader.Read(path, []));
    }

    // Where the data of an entry starts in the file: after its local header, of 30 bytes, its name and its extra field.
    private static long DataOffsetOf(string path, ZipArchiveEntry entry)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] file = new byte[stream.Length];
        stream.ReadExactly(file);
        byte[] name = System.Text.Encoding.UTF8.GetBytes(entry.FullName);
        for (int start = 0; start + 30 + name.Length <= file.Length; start++)
        {
            if (file[start] == 0x50 && file[start + 1] == 0x4B && file[start + 2] == 3 && file[start + 3] == 4 &&
                file.AsSpan(start + 30, name.Length).SequenceEqual(name))
            {
                int extra = BitConverter.ToUInt16(file, start + 28);
                return start + 30 + name.Length + extra;
            }
        }
        throw new InvalidOperationException($"No local header for {entry.FullName}.");
    }

    // A valid manifest of an empty document, with the properties of changes replacing its own.
    private static string Manifest(string changes)
    {
        var manifest = (JsonObject)JsonNode.Parse("""
            {"format": "modeling-toolkit", "version": 3, "units": "mm", "upAxis": "Z", "textures": [],
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
            TextureSize = 250,
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

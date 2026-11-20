using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class GltfWriterTests : IDisposable
{
    private readonly GltfReader _reader = new();
    private readonly GltfWriter _writer = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-gltf-writer-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("CesiumMilkTruck.glb")]
    [InlineData("BoxTextured.glb")]
    [InlineData("SimpleMeshes.gltf")]
    public void A_model_written_and_read_again_places_the_same_vertices_with_the_same_materials(string asset)
    {
        Document original = _reader.Read(Asset(asset), []);

        Document copy = RoundTrip(original);

        AssertSame(Placed(original, new Dictionary<BlockReference, Transform>()),
            Placed(copy, new Dictionary<BlockReference, Transform>()));
        Assert.Equal(original.Materials.Select(material => (material.Name, material.BaseColor, material.Metallic,
                material.Roughness, material.BaseColorTexture?.Data.Length)),
            copy.Materials.Select(material => (material.Name, material.BaseColor, material.Metallic,
                material.Roughness, material.BaseColorTexture?.Data.Length)));
        // Texture coordinates come back as they were, the origin of textures being at the top in glTF only.
        Assert.Equal(Coordinates(original), Coordinates(copy));
    }

    [Fact]
    public void Animations_written_and_read_again_place_the_parts_as_before()
    {
        Document original = _reader.Read(Asset("CesiumMilkTruck.glb"), []);

        Document copy = RoundTrip(original);

        Animation before = Assert.Single(original.Animations), after = Assert.Single(copy.Animations);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.Duration, after.Duration, 1e-6);
        foreach (double time in (double[])[0, 0.3, 0.77, 1.25])
        {
            AssertSame(Placed(original, before.PoseAt(time)), Placed(copy, after.PoseAt(time)));
        }
    }

    [Fact]
    public void The_model_space_gives_the_scene_its_name_with_no_node_around_it_and_hidden_entities_stay_hidden()
    {
        Document original = _reader.Read(Asset("CesiumMilkTruck.glb"), []);
        original.Root.Name = "Truck model";
        BlockReference wheel = Assert.Single(original.Blocks.SelectMany(block => block.Entities)
            .OfType<BlockReference>(), reference => reference.Name == "Wheels");
        wheel.IsHidden = true;
        Material textured = original.Materials.First(material => material.BaseColorTexture is not null);
        textured.TextureSize = 250;
        string rootName = original.Root.Name;

        Document copy = RoundTrip(original);

        Assert.Equal(rootName, copy.Root.Name);
        Assert.Equal(original.Root.Entities.Count, copy.Root.Entities.Count); // none wraps them
        Assert.True(copy.Blocks.SelectMany(block => block.Entities).OfType<BlockReference>()
            .Single(reference => reference.Name == "Wheels").IsHidden);
        Assert.Equal(250, copy.Materials.Single(material => material.Name == textured.Name).TextureSize);
    }

    [Fact]
    public void Anchors_are_empty_nodes_and_attachments_survive_a_round_trip()
    {
        var document = new Document();
        MeshEntity Triangle() => new([new(0, 0, 0), new(100, 0, 0), new(0, 100, 0)], [new(0, 1, 2)]);
        var leaf = new Block("Leaf") { Entities = { Triangle() } };
        var end = new AnchorEntity(Transform.Translation(450, 0, 760) * Transform.Rotation(Point3d.Origin, Vector3d.UnitZ, Math.PI / 2))
            { Name = "End" };
        leaf.Entities.Add(end);
        var chair = new Block("Chair") { Entities = { Triangle() } };
        chair.Entities.Add(new AnchorEntity(Transform.Translation(0, 0, 1200), AnchorKind.PointOfView) { Name = "Sitting" });
        var table = new Block("Table");
        var leafReference = new BlockReference(leaf, Transform.Translation(900, 0, 0)) { Name = "Leaf" };
        table.Entities.Add(leafReference);
        document.Blocks.AddRange([leaf, chair, table]);
        var tableReference = new BlockReference(table, Transform.Translation(0, 1000, 0)) { Name = "Table" };
        var chairReference = new BlockReference(chair, Transform.Translation(1500, 1000, 0)) { Name = "Chair" };
        document.Root.Entities.Add(tableReference);
        document.Root.Entities.Add(chairReference);
        document.Attach(new Attachment(chairReference, [tableReference, leafReference], end,
            Attachment.OffsetOf(chairReference, [tableReference, leafReference], end)));
        tableReference.IsGroup = true;
        tableReference.IsHidden = true; // both in the extras

        Document copy = RoundTrip(document);

        BlockReference Named(string name) =>
            copy.Blocks.Prepend(copy.Root).SelectMany(block => block.Entities).OfType<BlockReference>().Single(r => r.Name == name);
        AnchorEntity copiedEnd = Assert.Single(Named("Leaf").Block.Entities.OfType<AnchorEntity>());
        Assert.Equal(("End", AnchorKind.Attach), (copiedEnd.Name, copiedEnd.Kind));
        Assert.Equal(450, copiedEnd.Point.X, 1e-6);
        Assert.Equal(760, copiedEnd.Point.Z, 1e-6);
        Assert.Equal(1, copiedEnd.Placement.ApplyVector(Vector3d.UnitX).Y, 1e-6); // still turned a quarter
        Assert.Equal(AnchorKind.PointOfView, Assert.Single(Named("Chair").Block.Entities.OfType<AnchorEntity>()).Kind);
        Assert.True(Named("Table").IsGroup);
        Assert.True(Named("Table").IsHidden);
        Assert.False(Named("Chair").IsGroup);
        Attachment attachment = Assert.Single(copy.Attachments);
        Assert.Same(Named("Chair"), attachment.Follower);
        Assert.Equal([Named("Table"), Named("Leaf")], attachment.Chain);
        Assert.True(attachment.IsActive);

        // It works as before: the table moved, the chair goes with it.
        Named("Table").Transform = Transform.Translation(0, 2000, 0);
        Assert.Equal(2000, Named("Chair").Transform.Apply(Point3d.Origin).Y, 1e-6);
    }

    [Fact]
    public void The_names_of_nodes_that_hold_only_geometry_survive_a_round_trip()
    {
        var document = new Document();
        MeshEntity Triangle(string name, double x) => new(
            [new(x, 0, 0), new(x + 100, 0, 0), new(x, 100, 0)], [new(0, 1, 2)]) { Name = name };
        document.Root.Entities.Add(Triangle("Sconce", 0));
        var part = new Block("Fittings") { Entities = { Triangle("Vent", 300), Triangle("Grille", 600) } };
        document.Blocks.Add(part);
        document.Root.Entities.Add(new BlockReference(part, Transform.Translation(0, 500, 0)) { Name = "Fittings" });

        Document copy = RoundTrip(document);

        string?[] names = [.. copy.Blocks.Prepend(copy.Root).SelectMany(block => block.Entities).OfType<MeshEntity>()
            .Select(mesh => mesh.Name)];
        Assert.Equal(["Grille", "Sconce", "Vent"], names.Order());
    }

    [Fact]
    public void What_glTF_cannot_hold_is_reported()
    {
        var document = new Document();
        document.Root.Entities.Add(new GuideEntity(default, Vector3d.UnitX));
        var kernel = new OcctKernel();
        document.Root.Entities.Add(new BrepEntity(kernel, kernel.MakeBox(10, 20, 30)));
        var warnings = new List<string>();

        _writer.Write(document, Path.Combine(_folder, "shapes.glb"), warnings);

        Assert.Contains(warnings, warning => warning.Contains("guide"));
        Assert.Contains(warnings, warning => warning.Contains("B-rep"));
        Document copy = _reader.Read(Path.Combine(_folder, "shapes.glb"), []);
        BoundingBox box = copy.GetBoundingBox();
        Assert.Equal(10, box.Max.X - box.Min.X, 1e-3);
        Assert.Equal(30, box.Max.Z - box.Min.Z, 1e-3);
    }

    [Fact]
    public void Saving_again_what_was_read_adds_no_level_to_a_mesh_placed_in_several_places()
    {
        // A plate placed twice in a cabinet, and the cabinet in the model, as a file of another program holds them.
        var document = new Document();
        var cabinet = new Block("Cabinet");
        var plate = new Block("Plate");
        plate.Entities.Add(new MeshEntity(
            [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0)], [new(0, 1, 2)]));
        cabinet.Entities.Add(new BlockReference(plate, Transform.Identity));
        cabinet.Entities.Add(new BlockReference(plate, Transform.Translation(0, 0, 500)));
        document.Blocks.AddRange([cabinet, plate]);
        document.Root.Entities.Add(new BlockReference(cabinet, Transform.Identity) { Name = "Cabinet" });

        Document once = RoundTrip(document);
        Document twice = RoundTrip(once);
        Document thrice = RoundTrip(twice);

        static int Depth(Block block) =>
            1 + block.Entities.OfType<BlockReference>().Select(reference => Depth(reference.Block)).DefaultIfEmpty(0).Max();
        Assert.Equal(Depth(once.Root), Depth(twice.Root));
        Assert.Equal(Depth(once.Root), Depth(thrice.Root));
        Assert.Equal(once.Blocks.Count, thrice.Blocks.Count);
    }

    [Fact]
    public void A_group_with_geometry_of_its_own_is_read_as_a_component_with_a_warning()
    {
        var document = new Document();
        var room = new Block("Room")
        {
            Entities = { new MeshEntity([new(0, 0, 0), new(100, 0, 0), new(0, 100, 0)], [new(0, 1, 2)]) },
        };
        document.Blocks.Add(room);
        document.Root.Entities.Add(new BlockReference(room, Transform.Identity) { Name = "Room", IsGroup = true });
        string path = Path.Combine(_folder, "room.glb");
        _writer.Write(document, path);
        var warnings = new List<string>();

        Document copy = _reader.Read(path, warnings);

        Assert.False(copy.Root.Entities.OfType<BlockReference>().Single(reference => reference.Name == "Room").IsGroup);
        Assert.Contains(warnings, warning => warning.Contains("group", StringComparison.Ordinal));
    }

    private Document RoundTrip(Document document)
    {
        string path = Path.Combine(_folder, Guid.NewGuid() + ".glb");
        _writer.Write(document, path);
        return _reader.Read(path, []);
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "assets", "gltf", name);

    // The vertices of the meshes in the model space, sorted, with references moved by a pose.
    private static List<Point3d> Placed(Document document, IReadOnlyDictionary<BlockReference, Transform> pose)
    {
        var points = new List<Point3d>();
        Visit(document.Root, Transform.Identity);
        return [.. points.OrderBy(p => Math.Round(p.X, 1)).ThenBy(p => Math.Round(p.Y, 1)).ThenBy(p => Math.Round(p.Z, 1))];

        void Visit(Block block, Transform placement)
        {
            foreach (Entity entity in block.Entities)
            {
                if (entity is MeshEntity mesh)
                {
                    points.AddRange(mesh.Vertices.Select(placement.Apply));
                }
                else if (entity is BlockReference reference)
                {
                    Visit(reference.Block, placement * pose.GetValueOrDefault(reference, reference.Transform));
                }
            }
        }
    }

    private static List<(double, double)> Coordinates(Document document) =>
        [.. document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities).OfType<MeshEntity>()
            .SelectMany(mesh => mesh.TextureCoordinates ?? [])
            .Select(uv => (Math.Round(uv.X, 4), Math.Round(uv.Y, 4))).Order()];

    private static void AssertSame(List<Point3d> expected, List<Point3d> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].X, actual[index].X, 0.01);
            Assert.Equal(expected[index].Y, actual[index].Y, 0.01);
            Assert.Equal(expected[index].Z, actual[index].Z, 0.01);
        }
    }
}

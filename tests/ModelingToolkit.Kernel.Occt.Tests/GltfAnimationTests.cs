using System.Globalization;
using System.Text.Json.Nodes;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Kernel.Occt.Tests;

// Expected positions computed from the glTF file as glTF defines them, then converted from metres and Y up to
// millimetres and Z up: glTF (x, y, z) is (x, -z, y).
public sealed class GltfAnimationTests : IDisposable
{
    private static readonly Point3d[] Triangle = [new(0, 0, 0), new(0, 1, 0), new(0.1, 0, 0)];
    private static readonly Vector3d PanelOffset = new(0.5, 0, 0);

    private readonly GltfReader _reader = new();
    private readonly List<string> _warnings = [];
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-gltf-animation-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void Animated_nodes_are_placed_as_glTF_places_them(double time)
    {
        Document document = _reader.Read(HingedPanel(), _warnings);

        Animation open = Assert.Single(document.Animations);
        Assert.Equal("Open", open.Name);
        Assert.Equal(2, open.Duration, 1e-6);
        BlockReference hinge = Assert.Single(open.Targets);
        Assert.Equal("Hinge", hinge.Name);

        // Between the keys: 30° to 120° around the vertical, 0 to 0.5 m sideways, over two seconds.
        double fraction = Math.Clamp(time / 2, 0, 1);
        double angle = (30 + 90 * fraction) * Math.PI / 180;
        var translation = new Vector3d(1, 0.5 * fraction, 2);
        Point3d[] expected = [.. Triangle.Select(p => Converted(translation + RotateAroundY(angle, new Vector3d(p.X, p.Y, p.Z) + PanelOffset)))];
        Point3d[] actual = PlacedVertices(document, open.PoseAt(time));
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].X, actual[index].X, 1e-3);
            Assert.Equal(expected[index].Y, actual[index].Y, 1e-3);
            Assert.Equal(expected[index].Z, actual[index].Z, 1e-3);
        }
    }

    [Fact]
    public void Motions_of_nodes_merged_into_their_parent_are_left_out_with_a_warning()
    {
        Document document = _reader.Read(HingedPanel(), _warnings);

        Assert.DoesNotContain(document.Animations, animation => animation.Name == "Panel only");
        Assert.Contains(_warnings, warning => warning.Contains("'Panel only'") && warning.Contains("'Panel'"));
    }

    [Fact]
    public void The_animation_of_the_sample_truck_turns_both_wheels()
    {
        Document document = _reader.Read(
            Path.Combine(AppContext.BaseDirectory, "assets", "gltf", "CesiumMilkTruck.glb"), _warnings);

        Animation wheels = Assert.Single(document.Animations);
        Assert.Equal("Wheels", wheels.Name);
        Assert.Equal(["Wheels", "Wheels.001"], wheels.Targets.Select(target => target.Name).Order());
        Assert.All(wheels.Channels, channel => Assert.Equal(31, channel.Times.Count));
        Assert.Equal(1.25, wheels.Duration, 1e-6);
        Assert.DoesNotContain(_warnings, warning => warning.Contains("nimation"));
    }

    [Fact]
    public void Files_without_animations_have_none()
    {
        Document document = _reader.Read(
            Path.Combine(AppContext.BaseDirectory, "assets", "gltf", "Box.glb"), _warnings);

        Assert.Empty(document.Animations);
    }

    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static Point3d Converted(Vector3d gltf) => new(gltf.X * 1000, -gltf.Z * 1000, gltf.Y * 1000);

    private static Vector3d RotateAroundY(double angle, Vector3d p) => new(
        Math.Cos(angle) * p.X + Math.Sin(angle) * p.Z, p.Y, -Math.Sin(angle) * p.X + Math.Cos(angle) * p.Z);

    // The vertices of the meshes in the model space, with the references moved by a pose.
    private static Point3d[] PlacedVertices(Document document, IReadOnlyDictionary<BlockReference, Transform> pose)
    {
        var vertices = new List<Point3d>();
        Visit(document.Root, Transform.Identity);
        return [.. vertices];

        void Visit(Block block, Transform placement)
        {
            foreach (Entity entity in block.Entities)
            {
                if (entity is MeshEntity mesh)
                {
                    vertices.AddRange(mesh.Vertices.Select(placement.Apply));
                }
                else if (entity is BlockReference reference)
                {
                    Visit(reference.Block, placement * pose.GetValueOrDefault(reference, reference.Transform));
                }
            }
        }
    }

    // A panel, as a triangle, on a hinge turned 30° around the vertical; "Open" turns the hinge to 120° and moves it,
    // "Panel only" turns the panel, which the reader merges into the hinge.
    private string HingedPanel()
    {
        var buffer = new MemoryStream();
        var writer = new BinaryWriter(buffer);
        foreach (Point3d p in Triangle)
        {
            writer.Write((float)p.X);
            writer.Write((float)p.Y);
            writer.Write((float)p.Z);
        }
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)2);
        writer.Write((ushort)0); // padding
        writer.Write(0f); // times, at 44
        writer.Write(2f);
        foreach (double degrees in (double[])[30, 120]) // rotations, at 52
        {
            double half = degrees * Math.PI / 360;
            writer.Write(0f);
            writer.Write((float)Math.Sin(half));
            writer.Write(0f);
            writer.Write((float)Math.Cos(half));
        }
        foreach (float y in (float[])[0, 0.5f]) // translations, at 84
        {
            writer.Write(1f);
            writer.Write(y);
            writer.Write(2f);
        }
        byte[] data = buffer.ToArray();

        double rest = 15 * Math.PI / 180;
        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [0] } ],
          "nodes": [
            { "name": "Hinge", "translation": [1, 0, 2], "rotation": [0, {{Invariant(Math.Sin(rest))}}, 0, {{Invariant(Math.Cos(rest))}}],
              "children": [1] },
            { "name": "Panel", "mesh": 0, "translation": [0.5, 0, 0] }
          ],
          "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
          "buffers": [ { "byteLength": {{data.Length}},
                         "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(data)}}" } ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 6 },
            { "buffer": 0, "byteOffset": 44, "byteLength": 64 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [0.1, 1, 0] },
            { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" },
            { "bufferView": 2, "byteOffset": 0, "componentType": 5126, "count": 2, "type": "SCALAR",
              "min": [0], "max": [2] },
            { "bufferView": 2, "byteOffset": 8, "componentType": 5126, "count": 2, "type": "VEC4" },
            { "bufferView": 2, "byteOffset": 40, "componentType": 5126, "count": 2, "type": "VEC3" }
          ],
          "animations": [
            { "name": "Open",
              "samplers": [ { "input": 2, "output": 3 }, { "input": 2, "output": 4, "interpolation": "LINEAR" } ],
              "channels": [ { "sampler": 0, "target": { "node": 0, "path": "rotation" } },
                            { "sampler": 1, "target": { "node": 0, "path": "translation" } } ] },
            { "name": "Panel only",
              "samplers": [ { "input": 2, "output": 3 } ],
              "channels": [ { "sampler": 0, "target": { "node": 1, "path": "rotation" } } ] }
          ]
        }
        """;
        _ = JsonNode.Parse(json); // a mistake in the test file fails here, not in the reader
        string path = Path.Combine(_folder, "hinged.gltf");
        File.WriteAllText(path, json);
        return path;
    }
}

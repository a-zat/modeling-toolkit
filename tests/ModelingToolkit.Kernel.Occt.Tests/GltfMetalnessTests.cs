using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.IO;

namespace ModelingToolkit.Kernel.Occt.Tests;

public sealed class GltfMetalnessTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mtk-gltf-metalness-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("""{ "baseColorFactor": [0.8, 0.7, 0.5, 1] }""", 0f)]
    [InlineData("""{ "baseColorFactor": [0.8, 0.7, 0.5, 1], "metallicFactor": 1 }""", 1f)]
    [InlineData("""{ "baseColorFactor": [0.8, 0.7, 0.5, 1], "metallicFactor": 0.25 }""", 0.25f)]
    public void Metalness_a_file_leaves_out_is_none_and_one_it_states_is_kept(string pbr, float expected)
    {
        Document document = new GltfReader().Read(Write($$"""{ "name": "Paint", "pbrMetallicRoughness": {{pbr}} }"""), []);

        Assert.Equal(expected, Assert.Single(document.Materials, material => material.Name == "Paint").Metallic);
    }

    // A triangle, with the material given.
    private string Write(string material)
    {
        string buffer = Convert.ToBase64String([.. new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }
            .SelectMany(BitConverter.GetBytes)]);
        string path = Path.Combine(_folder, "triangle.gltf");
        File.WriteAllText(path, $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0, "scenes": [ { "nodes": [0] } ], "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "material": 0 } ] } ],
              "materials": [ {{material}} ],
              "accessors": [ { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
                               "min": [0, 0, 0], "max": [1, 1, 0] } ],
              "bufferViews": [ { "buffer": 0, "byteLength": 36 } ],
              "buffers": [ { "byteLength": 36, "uri": "data:application/octet-stream;base64,{{buffer}}" } ]
            }
            """);
        return path;
    }
}

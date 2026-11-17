using System.Text.Json;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Kernel.Occt.Interop;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Reads glTF files (<c>.gltf</c>, <c>.glb</c>) with OpenCASCADE, converting them to millimetres and Z up.
/// Meshes used by several nodes become blocks, referenced by each node.
/// </summary>
public sealed class GltfReader : IDocumentReader
{
    public string FormatName => "glTF";

    public IReadOnlyList<string> Extensions { get; } = [".gltf", ".glb"];

    public unsafe Document Read(string path, ICollection<string> warnings, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(warnings);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.", fullPath);
        }

        GltfExtensions.Check(path, warnings);

        SceneHandle scene;
        using (var bridge = new ProgressBridge(progress, cancellationToken))
        {
            NativeProgress native = bridge.Native;
            NativeStatus status = NativeMethods.SceneReadGltf(fullPath, bridge.IsUsed ? &native : null, out scene);
            if (status == NativeStatus.KernelError)
            {
                throw new InvalidDataException(NativeMethods.LastErrorMessage());
            }
            bridge.ThrowIfFailed(status);
        }

        using (scene)
        {
            var converter = new SceneConverter(scene);
            Document document = converter.Convert();
            foreach (string warning in converter.Warnings.Select(WithoutFilePrefix).Distinct())
            {
                warnings.Add(warning);
            }
            ReadAnimations(fullPath, document, warnings);
            ReadExtras(fullPath, document);
            return document;
        }
    }

    // The geometry is read already: animations that cannot be read leave it as it is.
    private static void ReadAnimations(string path, Document document, ICollection<string> warnings)
    {
        var found = new List<string>();
        try
        {
            document.Animations.AddRange(GltfAnimations.Read(path, document, found));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or InvalidOperationException or ArgumentException)
        {
            found.Add($"The animations cannot be read: {exception.Message}");
        }
        foreach (string warning in found.Distinct())
        {
            warnings.Add(warning);
        }
    }

    // What the toolkit writes in the extras of glTF, which OpenCASCADE ignores: the hidden nodes, found by name as the
    // animated ones, and the size of the textures of materials; and the metalness of materials with a texture of it. Extras that cannot be read are of no harm.
    private static void ReadExtras(string path, Document document)
    {
        try
        {
            using JsonDocument file = JsonDocument.Parse(GltfAnimations.Load(path).Json);
            JsonElement root = file.RootElement;
            var entities = document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities)
                .Where(entity => entity.Name is not null).ToLookup(entity => entity.Name!);
            foreach (JsonElement node in Items(root, "nodes"))
            {
                if (Extra(node, "hidden") is { ValueKind: JsonValueKind.True } &&
                    node.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String &&
                    entities[name.GetString()!].ToList() is [Entity entity])
                {
                    entity.IsHidden = true;
                }
            }
            var materials = document.Materials.ToLookup(material => material.Name);
            foreach (JsonElement entry in Items(root, "materials"))
            {
                TextureMetalness(entry, materials);
                if (Extra(entry, "textureSize") is { ValueKind: JsonValueKind.Number } size &&
                    entry.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String &&
                    size.GetDouble() > 0)
                {
                    foreach (Material material in materials[name.GetString()!])
                    {
                        material.TextureSize = (float)size.GetDouble();
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or InvalidOperationException)
        {
        }

        // glTF makes the metallic factor 1 when a file leaves it out, and with a texture of metalness and roughness the
        // factors only scale it: the material would be all metal, white and shadowless, where the files that leave it
        // out are of materials that are not. So it is 0 then; a factor that the file states is kept, unless a texture
        // of it comes with it, which is not read.
        static void TextureMetalness(JsonElement entry, ILookup<string, Material> materials)
        {
            if (!entry.TryGetProperty("name", out JsonElement name) || name.ValueKind != JsonValueKind.String)
            {
                return;
            }
            bool stated = entry.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) &&
                pbr.ValueKind == JsonValueKind.Object;
            if (!stated || !pbr.TryGetProperty("metallicFactor", out _) || pbr.TryGetProperty("metallicRoughnessTexture", out _))
            {
                foreach (Material material in materials[name.GetString()!])
                {
                    material.Metallic = 0;
                }
            }
        }

        static IEnumerable<JsonElement> Items(JsonElement root, string property) =>
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out JsonElement array) &&
            array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];

        static JsonElement? Extra(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty("extras", out JsonElement extras) &&
            extras.ValueKind == JsonValueKind.Object && extras.TryGetProperty(name, out JsonElement value)
                ? value : null;
    }

    // OpenCASCADE starts each problem of the file with "File '<path>' defines invalid glTF!" on a line of its own.
    private static string WithoutFilePrefix(string warning)
    {
        const string marker = " defines invalid glTF!\n";
        int index = warning.IndexOf(marker, StringComparison.Ordinal);
        return warning.StartsWith("File '", StringComparison.Ordinal) && index >= 0
            ? warning[(index + marker.Length)..]
            : warning;
    }
}

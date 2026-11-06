using System.Buffers.Binary;
using System.Text.Json;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Checks the glTF extensions a file lists against those the reader of OpenCASCADE supports, which skips what it cannot
/// read instead of refusing the file.
/// </summary>
internal static class GltfExtensions
{
    // The extensions RWGltf_GltfJsonParser reads. Draco and WebP need OpenCASCADE as built by native/ports/opencascade.
    private static readonly HashSet<string> Supported =
        ["EXT_texture_webp", "KHR_binary_glTF", "KHR_draco_mesh_compression", "KHR_materials_common"];

    private const uint GlbMagic = 0x46546C67;
    private const uint GlbJsonChunk = 0x4E4F534A;
    private const int GlbHeaderSize = 20;

    /// <summary>
    /// Refuses the file when it requires an extension that is not supported, unless the extension only affects
    /// textures or materials: then, as for the unsupported extensions it only uses, a warning is added.
    /// </summary>
    /// <exception cref="InvalidDataException">The file requires extensions that would leave out geometry.</exception>
    public static void Check(string path, ICollection<string> warnings)
    {
        string[] required;
        string[] used;
        try
        {
            if (ReadJson(path) is not { } json)
            {
                return;
            }
            using JsonDocument document = JsonDocument.Parse(json);
            required = Names(document.RootElement, "extensionsRequired");
            used = Names(document.RootElement, "extensionsUsed");
        }
        catch (JsonException)
        {
            // Not a glTF file: the reader reports it.
            return;
        }

        string[] missing = [.. required.Where(name => !Supported.Contains(name) && !AffectsLookOnly(name)).Order()];
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"File '{path}' requires glTF extensions that are not supported: {string.Join(", ", missing)}.");
        }

        foreach (string name in required.Union(used).Where(name => !Supported.Contains(name)).Order())
        {
            warnings.Add(required.Contains(name)
                ? $"Unsupported glTF extension {name}: the textures or materials that need it are left out."
                : $"Unsupported glTF extension {name}: it is ignored.");
        }
    }

    private static bool AffectsLookOnly(string name) =>
        name.StartsWith("KHR_materials_", StringComparison.Ordinal)
        || name.StartsWith("KHR_texture_", StringComparison.Ordinal)
        || name.StartsWith("EXT_texture_", StringComparison.Ordinal);

    // The JSON of a .gltf file, or the JSON chunk of a .glb file; null when the binary header is not valid.
    private static byte[]? ReadJson(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[GlbHeaderSize];
        int read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (read < 4 || BinaryPrimitives.ReadUInt32LittleEndian(header) != GlbMagic)
        {
            return File.ReadAllBytes(path);
        }

        if (read < GlbHeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(header[16..]) != GlbJsonChunk)
        {
            return null;
        }
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (length > stream.Length - GlbHeaderSize)
        {
            return null;
        }
        byte[] json = new byte[length];
        stream.ReadExactly(json);
        return json;
    }

    private static string[] Names(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(property, out JsonElement names)
        && names.ValueKind == JsonValueKind.Array
            ? [.. names.EnumerateArray()
                .Where(name => name.ValueKind == JsonValueKind.String)
                .Select(name => name.GetString()!)]
            : [];
}

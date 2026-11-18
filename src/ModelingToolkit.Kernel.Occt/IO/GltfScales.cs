using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Scales of nodes of 0, which OpenCASCADE refuses: files hide parts so at rest, to grow them by an animation, such as
/// the leaves of a table that extends. They are read as a scale too small to be seen, so that the animation, relative
/// to the scale at rest, grows them all the same.
/// </summary>
internal static class GltfScales
{
    public const double Minimum = 1e-6;

    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;

    public static double AtLeastMinimum(double scale) =>
        Math.Abs(scale) >= Minimum ? scale : scale < 0 ? -Minimum : Minimum;

    /// <summary>
    /// A copy of the file with its scales of nodes raised to <see cref="Minimum"/>, to read in its place and delete
    /// after; null when it has none to raise, or cannot be copied. The copy of a .gltf file is in its folder, for the
    /// files it refers to.
    /// </summary>
    public static string? RaisedCopy(string path)
    {
        try
        {
            (byte[] json, byte[]? _) = GltfAnimations.Load(path);
            if (JsonNode.Parse(json) is not JsonObject root || root["nodes"] is not JsonArray nodes || !Raise(nodes))
            {
                return null;
            }
            byte[] raised = Encoding.UTF8.GetBytes(root.ToJsonString());
            byte[] bytes = File.ReadAllBytes(path);
            bool binary = bytes.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == GlbMagic;
            string copy = binary
                ? Path.Combine(Path.GetTempPath(), $"mtk-{Guid.NewGuid():N}.glb")
                : Path.Combine(Path.GetDirectoryName(path)!, $".mtk-{Guid.NewGuid():N}.gltf");
            File.WriteAllBytes(copy, binary ? WithJson(bytes, raised) : raised);
            return copy;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException
                                              or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Raise(JsonArray nodes)
    {
        bool raised = false;
        foreach (JsonArray scale in nodes.OfType<JsonObject>().Select(node => node["scale"]).OfType<JsonArray>())
        {
            for (int i = 0; i < scale.Count; i++)
            {
                if (scale[i] is JsonValue value && value.TryGetValue(out double s) && AtLeastMinimum(s) != s)
                {
                    scale[i] = AtLeastMinimum(s);
                    raised = true;
                }
            }
        }
        return raised;
    }

    // The .glb file with its first JSON chunk replaced, padded with spaces to 4 bytes; the other chunks as they are.
    private static byte[] WithJson(byte[] glb, byte[] json)
    {
        using var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(glb.AsSpan(0, 12));
        bool replaced = false;
        for (int offset = 12; offset + 8 <= glb.Length;)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(offset)));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(offset + 4));
            if (type == JsonChunk && !replaced)
            {
                int padded = (json.Length + 3) & ~3;
                writer.Write(padded);
                writer.Write(type);
                writer.Write(json);
                writer.Write(Enumerable.Repeat((byte)' ', padded - json.Length).ToArray());
                replaced = true;
            }
            else
            {
                writer.Write(glb.AsSpan(offset, 8 + length));
            }
            offset += 8 + length;
        }
        writer.Flush();
        byte[] bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        return bytes;
    }
}

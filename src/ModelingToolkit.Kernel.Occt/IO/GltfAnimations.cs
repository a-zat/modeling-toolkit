using System.Buffers.Binary;
using System.Text.Json;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Reads the animations of a glTF file, which OpenCASCADE leaves out, for the block references that the reader made of
/// its nodes. A node is found by its name, which must be that of one block reference of the document; the animations
/// of the others, such as nodes merged into their parents, are left out with a warning.
/// </summary>
/// <remarks>
/// The values of glTF replace a property of the node; those of the toolkit are relative to the transformation of the
/// reference (<see cref="AnimationChannel"/>). Both are converted as the reader converts the nodes: from metres to
/// millimetres and from Y up to Z up, where glTF (x, y, z) is (x, -z, y).
/// </remarks>
internal static class GltfAnimations
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;
    private const double MetresToMillimetres = 1000;

    public static List<Animation> Read(string path, Document document, ICollection<string> warnings)
    {
        (byte[] json, byte[]? binary) = Load(path);
        using JsonDocument file = JsonDocument.Parse(json);
        JsonElement root = file.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("animations", out JsonElement animations) ||
            animations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var context = new Context(root, binary, Path.GetDirectoryName(path) ?? ".");
        Dictionary<string, List<BlockReference>> references = ReferencesByName(document);
        var result = new List<Animation>();
        int index = 0;
        foreach (JsonElement animation in animations.EnumerateArray())
        {
            string name = String(animation, "name") ?? $"Animation {index + 1}";
            index++;
            var channels = new List<AnimationChannel>();
            foreach (JsonElement channel in Array(animation, "channels"))
            {
                try
                {
                    if (ReadChannel(context, animation, channel, references, name, warnings) is { } read)
                    {
                        channels.Add(read);
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or ArgumentException
                                                      or InvalidOperationException or FormatException
                                                      or IOException or KeyNotFoundException)
                {
                    warnings.Add($"Animation '{name}': a channel cannot be read ({exception.Message}); it is left out.");
                }
            }
            if (channels.Count > 0)
            {
                result.Add(new Animation(name, channels));
            }
        }
        return result;
    }

    private static AnimationChannel? ReadChannel(Context context, JsonElement animation, JsonElement channel,
        Dictionary<string, List<BlockReference>> references, string animationName, ICollection<string> warnings)
    {
        JsonElement target = channel.GetProperty("target");
        string property = String(target, "path") ?? "";
        AnimatedProperty? animated = property switch
        {
            "translation" => AnimatedProperty.Translation,
            "rotation" => AnimatedProperty.Rotation,
            "scale" => AnimatedProperty.Scale,
            _ => null,
        };
        if (animated is not { } kind)
        {
            warnings.Add($"Animation '{animationName}': {property} channels are not supported; they are left out.");
            return null;
        }
        if (!target.TryGetProperty("node", out JsonElement nodeIndex))
        {
            return null;
        }

        JsonElement node = Array(context.Root, "nodes")[nodeIndex.GetInt32()];
        string? nodeName = String(node, "name");
        List<BlockReference>? found = null;
        if (nodeName is null || !references.TryGetValue(nodeName, out found) || found.Count != 1)
        {
            string label = nodeName is null ? $"node {nodeIndex.GetInt32()}" : $"node '{nodeName}'";
            warnings.Add(found is { Count: > 1 }
                ? $"Animation '{animationName}': several parts are named as {label}; its motion is left out."
                : $"Animation '{animationName}': {label} is not a part of its own in the model; its motion is left out.");
            return null;
        }
        BlockReference reference = found[0];

        JsonElement sampler = Array(animation, "samplers")[channel.GetProperty("sampler").GetInt32()];
        AnimationInterpolation interpolation = (String(sampler, "interpolation") ?? "LINEAR") switch
        {
            "STEP" => AnimationInterpolation.Step,
            "CUBICSPLINE" => AnimationInterpolation.CubicSpline,
            _ => AnimationInterpolation.Linear,
        };
        double[] times = context.ReadAccessor(sampler.GetProperty("input").GetInt32(), out _);
        double[] values = context.ReadAccessor(sampler.GetProperty("output").GetInt32(), out int components);
        int expected = kind == AnimatedProperty.Rotation ? 4 : 3;
        if (components != expected)
        {
            throw new InvalidDataException($"{property} values have {components} components");
        }

        Rest rest = Rest.Of(node);
        bool cubic = interpolation == AnimationInterpolation.CubicSpline;
        double[] relative = new double[values.Length];
        for (int start = 0; start < values.Length; start += components)
        {
            // In a cubic spline, the in- and out-tangents of each key are differences: they are not offset.
            bool tangent = cubic && (start / components) % 3 != 1;
            double[] value = values.AsSpan(start, components).ToArray();
            double[] converted = kind switch
            {
                AnimatedProperty.Translation => Relative(reference.Transform, ConvertPosition(value), tangent),
                AnimatedProperty.Rotation => Quaternions.Multiply(Quaternions.Conjugate(rest.Rotation),
                    ConvertRotation(value)),
                _ => Divide(ConvertScale(value), rest.Scale),
            };
            converted.CopyTo(relative, start);
        }

        return new AnimationChannel(reference, kind, interpolation, times, relative);
    }

    // A translation of the node as an offset along the axes of the block it references: in the reference, the block is
    // turned and scaled by the linear part of its transformation, then moved by its translation.
    private static double[] Relative(Transform transform, Vector3d translation, bool tangent)
    {
        Transform linear = transform with { M03 = 0, M13 = 0, M23 = 0 };
        Vector3d offset = tangent ? translation : translation - new Vector3d(transform.M03, transform.M13, transform.M23);
        Vector3d local = linear.Inverse().ApplyVector(offset);
        return [local.X, local.Y, local.Z];
    }

    private static double[] Divide(double[] value, double[] by) =>
        [value[0] / by[0], value[1] / by[1], value[2] / by[2]];

    private static Vector3d ConvertPosition(double[] v) =>
        new Vector3d(v[0], -v[2], v[1]) * MetresToMillimetres;

    private static double[] ConvertRotation(double[] q) => [q[0], -q[2], q[1], q[3]];

    private static double[] ConvertScale(double[] s) => [s[0], s[2], s[1]];

    // The rotation and scale of a node, converted to Z up, from which its animated values are made relative.
    private sealed record Rest(double[] Rotation, double[] Scale)
    {
        public static Rest Of(JsonElement node)
        {
            if (node.TryGetProperty("matrix", out JsonElement matrix))
            {
                // Column-major; the rotation and the scale of an affine matrix without shear.
                double[] m = [.. matrix.EnumerateArray().Select(value => value.GetDouble())];
                double sx = Math.Sqrt(m[0] * m[0] + m[1] * m[1] + m[2] * m[2]);
                double sy = Math.Sqrt(m[4] * m[4] + m[5] * m[5] + m[6] * m[6]);
                double sz = Math.Sqrt(m[8] * m[8] + m[9] * m[9] + m[10] * m[10]);
                var rotation = new Transform(
                    m[0] / sx, m[4] / sy, m[8] / sz, 0,
                    m[1] / sx, m[5] / sy, m[9] / sz, 0,
                    m[2] / sx, m[6] / sy, m[10] / sz, 0);
                return new Rest(ConvertRotation(Quaternions.FromTransform(rotation)), ConvertScale([sx, sy, sz]));
            }

            double[] q = node.TryGetProperty("rotation", out JsonElement r)
                ? [.. r.EnumerateArray().Select(value => value.GetDouble())]
                : [0, 0, 0, 1];
            double[] s = node.TryGetProperty("scale", out JsonElement scale)
                ? [.. scale.EnumerateArray().Select(value => value.GetDouble())]
                : [1, 1, 1];
            return new Rest(Quaternions.Normalized(ConvertRotation(q)), ConvertScale(s));
        }
    }

    private static Dictionary<string, List<BlockReference>> ReferencesByName(Document document)
    {
        var byName = new Dictionary<string, List<BlockReference>>(StringComparer.Ordinal);
        foreach (Block block in document.Blocks.Prepend(document.Root))
        {
            foreach (BlockReference reference in block.Entities.OfType<BlockReference>())
            {
                if (reference.Name is { } name)
                {
                    if (!byName.TryGetValue(name, out List<BlockReference>? list))
                    {
                        byName[name] = list = [];
                    }
                    list.Add(reference);
                }
            }
        }
        return byName;
    }

    private static IReadOnlyList<JsonElement> Array(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray()]
            : [];

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The JSON of the file and, for a .glb file, its binary chunk.
    private static (byte[] Json, byte[]? Binary) Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic)
        {
            return (bytes, null);
        }

        byte[]? json = null, binary = null;
        int offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            if (length > bytes.Length - offset - 8)
            {
                throw new InvalidDataException("A chunk of the binary glTF file is truncated.");
            }
            byte[] data = bytes.AsSpan(offset + 8, length).ToArray();
            if (type == JsonChunk && json is null)
            {
                json = data;
            }
            else if (type == BinaryChunk && binary is null)
            {
                binary = data;
            }
            offset += 8 + length;
        }
        return (json ?? throw new InvalidDataException("The binary glTF file has no JSON."), binary);
    }

    // The buffers of the file, loaded once each, and the reading of accessors.
    private sealed class Context(JsonElement root, byte[]? binary, string folder)
    {
        private readonly Dictionary<int, byte[]> _buffers = [];

        public JsonElement Root { get; } = root;

        /// <summary>The values of an accessor as doubles, normalized integers converted, with their components.</summary>
        public double[] ReadAccessor(int index, out int components)
        {
            JsonElement accessor = Array(Root, "accessors")[index];
            if (accessor.TryGetProperty("sparse", out _))
            {
                throw new InvalidDataException("sparse accessors are not supported");
            }
            components = String(accessor, "type") switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                string type => throw new InvalidDataException($"accessors of type {type} are not supported"),
                null => throw new InvalidDataException("an accessor has no type"),
            };
            int componentType = accessor.GetProperty("componentType").GetInt32();
            int size = componentType switch
            {
                5120 or 5121 => 1,
                5122 or 5123 => 2,
                5125 or 5126 => 4,
                _ => throw new InvalidDataException($"component type {componentType} is not supported"),
            };
            bool normalized = accessor.TryGetProperty("normalized", out JsonElement n) && n.GetBoolean();
            int count = accessor.GetProperty("count").GetInt32();
            double[] values = new double[count * components];
            if (!accessor.TryGetProperty("bufferView", out JsonElement viewIndex))
            {
                return values; // all zeros, as glTF defines it
            }

            JsonElement view = Array(Root, "bufferViews")[viewIndex.GetInt32()];
            byte[] buffer = Buffer(view.GetProperty("buffer").GetInt32());
            int start = Int(view, "byteOffset") + Int(accessor, "byteOffset");
            int stride = Int(view, "byteStride") is > 0 and var given ? given : size * components;
            if (count > 0 && start + (count - 1) * stride + size * components > buffer.Length)
            {
                throw new InvalidDataException("an accessor goes beyond its buffer");
            }

            for (int element = 0; element < count; element++)
            {
                for (int component = 0; component < components; component++)
                {
                    ReadOnlySpan<byte> at = buffer.AsSpan(start + element * stride + component * size);
                    values[element * components + component] = componentType switch
                    {
                        5126 => BinaryPrimitives.ReadSingleLittleEndian(at),
                        5120 => normalized ? Math.Max((sbyte)at[0] / 127.0, -1) : (sbyte)at[0],
                        5121 => normalized ? at[0] / 255.0 : at[0],
                        5122 => normalized
                            ? Math.Max(BinaryPrimitives.ReadInt16LittleEndian(at) / 32767.0, -1)
                            : BinaryPrimitives.ReadInt16LittleEndian(at),
                        5123 => normalized
                            ? BinaryPrimitives.ReadUInt16LittleEndian(at) / 65535.0
                            : BinaryPrimitives.ReadUInt16LittleEndian(at),
                        _ => BinaryPrimitives.ReadUInt32LittleEndian(at),
                    };
                }
            }
            return values;
        }

        private byte[] Buffer(int index)
        {
            if (_buffers.TryGetValue(index, out byte[]? known))
            {
                return known;
            }

            JsonElement buffer = Array(Root, "buffers")[index];
            byte[] data;
            if (String(buffer, "uri") is not { } uri)
            {
                data = index == 0 && binary is not null
                    ? binary
                    : throw new InvalidDataException($"buffer {index} has no data");
            }
            else if (uri.StartsWith("data:", StringComparison.Ordinal))
            {
                int comma = uri.IndexOf(',', StringComparison.Ordinal);
                data = Convert.FromBase64String(uri[(comma + 1)..]);
            }
            else
            {
                data = File.ReadAllBytes(Path.Combine(folder, Uri.UnescapeDataString(uri)));
            }
            return _buffers[index] = data;
        }

        private static int Int(JsonElement element, string property) =>
            element.TryGetProperty(property, out JsonElement value) ? value.GetInt32() : 0;
    }
}

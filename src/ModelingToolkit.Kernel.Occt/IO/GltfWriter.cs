using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Kernel.Occt.IO;

/// <summary>
/// Writes documents as binary glTF (<c>.glb</c>), in metres and Y up: the model space as the root node, named after it,
/// each block reference as a node holding the entities of its block, meshes shared by the references to a block,
/// materials with their textures, the animations, and the hidden entities, in the extras of their nodes.
/// </summary>
/// <remarks>
/// What glTF cannot hold is reported as warnings: B-rep shapes are written as their tessellation, and guides are left
/// out. The conversions are those of <see cref="GltfReader"/> in reverse: a point (x, y, z) in millimetres and Z up is
/// (x, z, -y) / 1000 in glTF.
/// </remarks>
public sealed class GltfWriter : IDocumentWriter
{
    private const double MillimetresToMetres = 0.001;

    // The relative deflection of the tessellation of shapes, as in the 3D view.
    private const double RelativeDeflection = 0.001;
    private const double MinimumDeflection = 0.001;

    public string FormatName => "glTF";

    public IReadOnlyList<string> Extensions { get; } = [".glb"];

    public void Write(Document document, string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        Write(document, path, [], progress, cancellationToken);

    /// <summary>As <see cref="Write(Document, string, IProgress{double}?, CancellationToken)"/>, with what is lost.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822",
        Justification = "The writer is used through its instance, as the other writers.")]
    public void Write(Document document, string path, ICollection<string> warnings, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(warnings);

        using var builder = new Builder(document, cancellationToken);
        byte[] glb = builder.Build();
        foreach (string warning in builder.Warnings)
        {
            warnings.Add(warning);
        }

        // Written beside the file, then put in its place: a failure leaves the file as it was.
        string fullPath = Path.GetFullPath(path);
        string temporary = fullPath + ".writing";
        try
        {
            File.WriteAllBytes(temporary, glb);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        progress?.Report(1);
    }

    // glTF (x, y, z) is (x, -z, y) here: the rotation from glTF to the toolkit, and back.
    private static Vector3d ToGltf(Vector3d v) => new(v.X, v.Z, -v.Y);

    private static Vector3d FromGltf(Vector3d v) => new(v.X, -v.Z, v.Y);

    private static double[] RotationToGltf(double[] q) => [q[0], q[2], -q[1], q[3]];

    private static double[] ScaleToGltf(double[] s) => [s[0], s[2], s[1]];

    private sealed class Builder(Document document, CancellationToken cancellationToken) : IDisposable
    {
        private readonly MemoryStream _binary = new();
        private readonly JsonArray _nodes = [];
        private readonly JsonArray _meshes = [];
        private readonly JsonArray _accessors = [];
        private readonly JsonArray _bufferViews = [];
        private readonly JsonArray _materials = [];
        private readonly JsonArray _textures = [];
        private readonly JsonArray _images = [];
        private readonly HashSet<string> _extensionsUsed = [];
        private readonly Dictionary<Material, int> _materialIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Texture, int> _textureIndices = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Entity, int> _meshIndices = new(ReferenceEqualityComparer.Instance);
        // The nodes placing each block reference: one per placement of the block holding it.
        private readonly Dictionary<BlockReference, List<int>> _referenceNodes = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<BlockReference> _animated =
            new(document.Animations.SelectMany(animation => animation.Targets), ReferenceEqualityComparer.Instance);
        private int _guides;
        private int _shapes;

        public List<string> Warnings { get; } = [];

        public void Dispose() => _binary.Dispose();

        public byte[] Build()
        {
            int root = AddNode(new JsonObject { ["name"] = document.Root.Name });
            _nodes[root]!["children"] = Children(document.Root);
            JsonArray animations = Animations();

            var json = new JsonObject
            {
                ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "Modeling Toolkit" },
                ["scene"] = 0,
                ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = new JsonArray(root) }),
                ["nodes"] = _nodes,
            };
            AddIfAny(json, "meshes", _meshes);
            AddIfAny(json, "materials", _materials);
            AddIfAny(json, "textures", _textures);
            AddIfAny(json, "images", _images);
            AddIfAny(json, "animations", animations);
            AddIfAny(json, "accessors", _accessors);
            AddIfAny(json, "bufferViews", _bufferViews);
            if (_binary.Length > 0)
            {
                json["buffers"] = new JsonArray(new JsonObject { ["byteLength"] = _binary.Length });
            }
            if (_extensionsUsed.Count > 0)
            {
                json["extensionsUsed"] = new JsonArray([.. _extensionsUsed.Order().Select(name => JsonValue.Create(name))]);
            }

            if (_shapes > 0)
            {
                Warnings.Add($"{_shapes} B-rep shape(s) are written as triangles: glTF cannot hold their exact geometry.");
            }
            if (_guides > 0)
            {
                Warnings.Add($"{_guides} guide(s) are left out: glTF has no guides.");
            }
            return Glb(json);
        }

        private static void AddIfAny(JsonObject json, string name, JsonArray array)
        {
            if (array.Count > 0)
            {
                json[name] = array;
            }
        }

        private int AddNode(JsonObject node)
        {
            _nodes.Add(node);
            return _nodes.Count - 1;
        }

        // The nodes of the entities of a block, placed anew for each reference that reaches it.
        private JsonArray Children(Block block)
        {
            var children = new JsonArray();
            foreach (Entity entity in block.Entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int? node = entity switch
                {
                    BlockReference reference => ReferenceNode(reference),
                    MeshEntity mesh when mesh.Triangles.Length > 0 => MeshNode(mesh, MeshOf(mesh)),
                    BrepEntity brep => ShapeOf(brep) is { } shapeMesh ? MeshNode(brep, shapeMesh) : null,
                    GuideEntity => CountGuide(),
                    _ => null,
                };
                if (node is { } index)
                {
                    children.Add(index);
                }
            }
            return children;
        }

        private int? CountGuide()
        {
            _guides++;
            return null;
        }

        private int MeshNode(Entity entity, int mesh)
        {
            var node = new JsonObject { ["mesh"] = mesh };
            if (entity.Name is { } name)
            {
                node["name"] = name;
            }
            MarkHidden(node, entity);
            return AddNode(node);
        }

        private static void MarkHidden(JsonObject node, Entity entity)
        {
            if (entity.IsHidden)
            {
                node["extras"] = new JsonObject { ["hidden"] = true };
            }
        }

        private int ReferenceNode(BlockReference reference)
        {
            var node = new JsonObject { ["name"] = reference.Name ?? reference.Block.Name };
            Transform transform = ToGltf(reference.Transform);
            if (_animated.Contains(reference))
            {
                // Animated nodes need their transformation as translation, rotation and scale.
                (Vector3d translation, double[] rotation, double[] scale) = Decompose(transform);
                node["translation"] = Array(translation.X, translation.Y, translation.Z);
                node["rotation"] = Array(rotation);
                node["scale"] = Array(scale);
            }
            else if (!transform.IsIdentity)
            {
                node["matrix"] = Array(
                    transform.M00, transform.M10, transform.M20, 0,
                    transform.M01, transform.M11, transform.M21, 0,
                    transform.M02, transform.M12, transform.M22, 0,
                    transform.M03, transform.M13, transform.M23, 1);
            }
            MarkHidden(node, reference);
            int index = AddNode(node);
            if (!_referenceNodes.TryGetValue(reference, out List<int>? nodes))
            {
                _referenceNodes[reference] = nodes = [];
            }
            nodes.Add(index);
            JsonArray children = Children(reference.Block);
            if (children.Count > 0)
            {
                node["children"] = children;
            }
            return index;
        }

        // A transformation of the toolkit as glTF sees it: conjugated by the rotation of the axes, in metres.
        private static Transform ToGltf(Transform t)
        {
            Vector3d[] columns = [.. new[] { Vector3d.UnitX, Vector3d.UnitY, Vector3d.UnitZ }
                .Select(axis => GltfWriter.ToGltf(t.ApplyVector(FromGltf(axis))))];
            Vector3d translation = GltfWriter.ToGltf(new Vector3d(t.M03, t.M13, t.M23)) * MillimetresToMetres;
            return new Transform(
                columns[0].X, columns[1].X, columns[2].X, translation.X,
                columns[0].Y, columns[1].Y, columns[2].Y, translation.Y,
                columns[0].Z, columns[1].Z, columns[2].Z, translation.Z);
        }

        // Translation, rotation and scale of a transformation without shear.
        private static (Vector3d Translation, double[] Rotation, double[] Scale) Decompose(Transform t)
        {
            var x = new Vector3d(t.M00, t.M10, t.M20);
            var y = new Vector3d(t.M01, t.M11, t.M21);
            var z = new Vector3d(t.M02, t.M12, t.M22);
            double sx = x.Length, sy = y.Length, sz = z.Length;
            if (x.Cross(y).Dot(z) < 0)
            {
                sx = -sx; // a mirror, as a negative scale along X
            }
            var rotation = new Transform(
                t.M00 / sx, t.M01 / sy, t.M02 / sz, 0,
                t.M10 / sx, t.M11 / sy, t.M12 / sz, 0,
                t.M20 / sx, t.M21 / sy, t.M22 / sz, 0);
            return (new Vector3d(t.M03, t.M13, t.M23), Quaternions.FromTransform(rotation), [sx, sy, sz]);
        }

        private int MeshOf(MeshEntity mesh)
        {
            if (_meshIndices.TryGetValue(mesh, out int known))
            {
                return known;
            }
            var primitive = Primitive(mesh, mesh.Triangles, mesh.TextureCoordinates);
            if (mesh.Material is { } material)
            {
                primitive["material"] = MaterialOf(material);
            }
            _meshes.Add(new JsonObject { ["primitives"] = new JsonArray(primitive) });
            return _meshIndices[mesh] = _meshes.Count - 1;
        }

        // The tessellation of a shape, one primitive for the faces of each material; null for one without geometry.
        private int? ShapeOf(BrepEntity brep)
        {
            if (_meshIndices.TryGetValue(brep, out int known))
            {
                return known;
            }
            ShapeTessellation tessellation;
            try
            {
                BoundingBox box = brep.Kernel.GetBoundingBox(brep.Shape);
                if (box.IsEmpty)
                {
                    return null;
                }
                double diagonal = (box.Max - box.Min).Length;
                tessellation = brep.Kernel.Tessellate(brep.Shape, Math.Max(diagonal * RelativeDeflection, MinimumDeflection));
            }
            catch (GeometryKernelException exception)
            {
                Warnings.Add($"{brep.Name ?? "A shape"} is left out: {exception.Message}");
                return null;
            }
            _shapes++;

            MeshEntity mesh = tessellation.Mesh;
            var primitives = new JsonArray();
            foreach (IGrouping<Material?, int> group in Enumerable.Range(0, tessellation.FaceCount)
                         .GroupBy(brep.MaterialOf, ReferenceEqualityComparer.Instance as IEqualityComparer<Material?>))
            {
                Triangle[] triangles = [.. group.SelectMany(face => mesh.Triangles[tessellation.TrianglesOf(face)])];
                if (triangles.Length == 0)
                {
                    continue;
                }
                // The faces of a textured material repeat the texture every texture size.
                Vector2[]? coordinates = mesh.TextureCoordinates;
                if (coordinates is not null && group.Key is { BaseColorTexture: not null } textured)
                {
                    float scale = 1f / Math.Max(textured.TextureSize, 1e-3f);
                    coordinates = [.. coordinates.Select(uv => uv * scale)];
                }
                JsonObject primitive = Primitive(mesh, triangles, coordinates);
                if (group.Key is { } material)
                {
                    primitive["material"] = MaterialOf(material);
                }
                primitives.Add(primitive);
            }
            if (primitives.Count == 0)
            {
                return null;
            }
            _meshes.Add(new JsonObject { ["primitives"] = primitives });
            return _meshIndices[brep] = _meshes.Count - 1;
        }

        private JsonObject Primitive(MeshEntity mesh, Triangle[] triangles, Vector2[]? coordinates)
        {
            var positions = new float[mesh.Vertices.Length * 3];
            float[] min = [float.MaxValue, float.MaxValue, float.MaxValue];
            float[] max = [float.MinValue, float.MinValue, float.MinValue];
            for (int vertex = 0; vertex < mesh.Vertices.Length; vertex++)
            {
                Point3d p = mesh.Vertices[vertex];
                Vector3d g = GltfWriter.ToGltf(new Vector3d(p.X, p.Y, p.Z)) * MillimetresToMetres;
                float[] xyz = [(float)g.X, (float)g.Y, (float)g.Z];
                for (int axis = 0; axis < 3; axis++)
                {
                    positions[vertex * 3 + axis] = xyz[axis];
                    min[axis] = Math.Min(min[axis], xyz[axis]);
                    max[axis] = Math.Max(max[axis], xyz[axis]);
                }
            }
            var attributes = new JsonObject
            {
                ["POSITION"] = Accessor(positions, "VEC3", min, max, target: 34962),
            };
            if (mesh.Normals is { } normals)
            {
                var values = new float[normals.Length * 3];
                for (int vertex = 0; vertex < normals.Length; vertex++)
                {
                    Vector3d g = GltfWriter.ToGltf(new Vector3d(normals[vertex].X, normals[vertex].Y, normals[vertex].Z));
                    values[vertex * 3] = (float)g.X;
                    values[vertex * 3 + 1] = (float)g.Y;
                    values[vertex * 3 + 2] = (float)g.Z;
                }
                attributes["NORMAL"] = Accessor(values, "VEC3", target: 34962);
            }
            if (coordinates is not null)
            {
                // glTF puts the origin of textures at their top left, OpenCASCADE at their bottom left.
                var values = new float[coordinates.Length * 2];
                for (int vertex = 0; vertex < coordinates.Length; vertex++)
                {
                    values[vertex * 2] = coordinates[vertex].X;
                    values[vertex * 2 + 1] = 1f - coordinates[vertex].Y;
                }
                attributes["TEXCOORD_0"] = Accessor(values, "VEC2", target: 34962);
            }
            var indices = new uint[triangles.Length * 3];
            for (int triangle = 0; triangle < triangles.Length; triangle++)
            {
                indices[triangle * 3] = (uint)triangles[triangle].A;
                indices[triangle * 3 + 1] = (uint)triangles[triangle].B;
                indices[triangle * 3 + 2] = (uint)triangles[triangle].C;
            }
            return new JsonObject { ["attributes"] = attributes, ["indices"] = IndexAccessor(indices) };
        }

        private int MaterialOf(Material material)
        {
            if (_materialIndices.TryGetValue(material, out int known))
            {
                return known;
            }
            var pbr = new JsonObject
            {
                ["baseColorFactor"] = Array(material.BaseColor.R, material.BaseColor.G, material.BaseColor.B,
                    material.BaseColor.A),
                ["metallicFactor"] = material.Metallic,
                ["roughnessFactor"] = material.Roughness,
            };
            var entry = new JsonObject { ["name"] = material.Name, ["pbrMetallicRoughness"] = pbr };
            if (material.BaseColor.A < 1)
            {
                entry["alphaMode"] = "BLEND";
            }
            if (material.BaseColorTexture is { } texture)
            {
                pbr["baseColorTexture"] = new JsonObject { ["index"] = TextureOf(texture) };
                entry["extras"] = new JsonObject { ["textureSize"] = material.TextureSize };
            }
            _materials.Add(entry);
            return _materialIndices[material] = _materials.Count - 1;
        }

        private int TextureOf(Texture texture)
        {
            if (_textureIndices.TryGetValue(texture, out int known))
            {
                return known;
            }
            _images.Add(new JsonObject
            {
                ["name"] = texture.Name,
                ["bufferView"] = View(texture.Data, target: null),
                ["mimeType"] = texture.MediaType,
            });
            int image = _images.Count - 1;
            JsonObject entry = texture.MediaType == "image/webp"
                ? new JsonObject
                {
                    ["extensions"] = new JsonObject { ["EXT_texture_webp"] = new JsonObject { ["source"] = image } },
                }
                : new JsonObject { ["source"] = image };
            if (texture.MediaType == "image/webp")
            {
                _extensionsUsed.Add("EXT_texture_webp");
            }
            _textures.Add(entry);
            return _textureIndices[texture] = _textures.Count - 1;
        }

        // The channels of each animation, for every node placing the references it moves, as glTF values: absolute,
        // where those of the toolkit are relative to the transformation of the reference.
        private JsonArray Animations()
        {
            var animations = new JsonArray();
            foreach (Animation animation in document.Animations)
            {
                var samplers = new JsonArray();
                var channels = new JsonArray();
                foreach (AnimationChannel channel in animation.Channels)
                {
                    if (!_referenceNodes.TryGetValue(channel.Target, out List<int>? nodes))
                    {
                        continue; // no longer in the model
                    }
                    samplers.Add(new JsonObject
                    {
                        ["input"] = Accessor([.. channel.Times.Select(time => (float)time)], "SCALAR",
                            [(float)channel.Times[0]], [(float)channel.Times[^1]]),
                        ["output"] = Accessor(AbsoluteValues(channel), channel.Property == AnimatedProperty.Rotation
                            ? "VEC4" : "VEC3"),
                        ["interpolation"] = channel.Interpolation switch
                        {
                            AnimationInterpolation.Step => "STEP",
                            AnimationInterpolation.CubicSpline => "CUBICSPLINE",
                            _ => "LINEAR",
                        },
                    });
                    foreach (int node in nodes)
                    {
                        channels.Add(new JsonObject
                        {
                            ["sampler"] = samplers.Count - 1,
                            ["target"] = new JsonObject
                            {
                                ["node"] = node,
                                ["path"] = channel.Property.ToString().ToLowerInvariant(),
                            },
                        });
                    }
                }
                if (channels.Count > 0)
                {
                    animations.Add(new JsonObject
                    {
                        ["name"] = animation.Name, ["samplers"] = samplers, ["channels"] = channels,
                    });
                }
            }
            return animations;
        }

        private static float[] AbsoluteValues(AnimationChannel channel)
        {
            Transform transform = channel.Target.Transform;
            Transform linear = transform with { M03 = 0, M13 = 0, M23 = 0 };
            var origin = new Vector3d(transform.M03, transform.M13, transform.M23);
            (_, double[] rest, double[] scale) = Decompose(transform);
            bool cubic = channel.Interpolation == AnimationInterpolation.CubicSpline;
            int components = channel.Property == AnimatedProperty.Rotation ? 4 : 3;
            var result = new float[channel.Values.Count];
            for (int start = 0; start < channel.Values.Count; start += components)
            {
                bool tangent = cubic && (start / components) % 3 != 1;
                double[] value = [.. channel.Values.Skip(start).Take(components)];
                double[] absolute = channel.Property switch
                {
                    AnimatedProperty.Translation => Translation(linear, origin, value, tangent),
                    AnimatedProperty.Rotation => RotationToGltf(Quaternions.Multiply(rest, value)),
                    _ => ScaleToGltf([scale[0] * value[0], scale[1] * value[1], scale[2] * value[2]]),
                };
                for (int c = 0; c < components; c++)
                {
                    result[start + c] = (float)absolute[c];
                }
            }
            return result;
        }

        private static double[] Translation(Transform linear, Vector3d origin, double[] offset, bool tangent)
        {
            Vector3d moved = linear.ApplyVector(new Vector3d(offset[0], offset[1], offset[2]));
            Vector3d g = GltfWriter.ToGltf(tangent ? moved : origin + moved) * MillimetresToMetres;
            return [g.X, g.Y, g.Z];
        }

        // Accessors and buffer views, in the binary chunk.

        private int Accessor(float[] values, string type, float[]? min = null, float[]? max = null, int? target = null)
        {
            byte[] bytes = new byte[values.Length * 4];
            for (int index = 0; index < values.Length; index++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), values[index]);
            }
            int components = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, _ => 4 };
            var accessor = new JsonObject
            {
                ["bufferView"] = View(bytes, target),
                ["componentType"] = 5126,
                ["count"] = values.Length / components,
                ["type"] = type,
            };
            if (min is not null && max is not null)
            {
                accessor["min"] = Array([.. min.Select(value => (double)value)]);
                accessor["max"] = Array([.. max.Select(value => (double)value)]);
            }
            _accessors.Add(accessor);
            return _accessors.Count - 1;
        }

        private int IndexAccessor(uint[] indices)
        {
            byte[] bytes = new byte[indices.Length * 4];
            for (int index = 0; index < indices.Length; index++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), indices[index]);
            }
            _accessors.Add(new JsonObject
            {
                ["bufferView"] = View(bytes, 34963),
                ["componentType"] = 5125,
                ["count"] = indices.Length,
                ["type"] = "SCALAR",
            });
            return _accessors.Count - 1;
        }

        private int View(byte[] bytes, int? target)
        {
            while (_binary.Length % 4 != 0)
            {
                _binary.WriteByte(0);
            }
            var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = _binary.Length, ["byteLength"] = bytes.Length };
            if (target is { } kind)
            {
                view["target"] = kind;
            }
            _binary.Write(bytes);
            _bufferViews.Add(view);
            return _bufferViews.Count - 1;
        }

        private static JsonArray Array(params double[] values) =>
            new([.. values.Select(value => JsonValue.Create(value))]);

        private byte[] Glb(JsonObject json)
        {
            byte[] text = JsonSerializer.SerializeToUtf8Bytes(json);
            int jsonLength = (text.Length + 3) / 4 * 4;
            while (_binary.Length % 4 != 0)
            {
                _binary.WriteByte(0);
            }
            int binaryLength = (int)_binary.Length;
            int total = 12 + 8 + jsonLength + (binaryLength > 0 ? 8 + binaryLength : 0);
            byte[] glb = new byte[total];
            BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)total);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)jsonLength);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
            text.CopyTo(glb, 20);
            glb.AsSpan(20 + text.Length, jsonLength - text.Length).Fill((byte)' ');
            if (binaryLength > 0)
            {
                int offset = 20 + jsonLength;
                BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(offset), (uint)binaryLength);
                BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(offset + 4), 0x004E4942);
                _binary.ToArray().CopyTo(glb, offset + 8);
            }
            return glb;
        }
    }
}

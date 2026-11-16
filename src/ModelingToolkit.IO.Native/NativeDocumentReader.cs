using System.IO.Compression;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.IO.Native;

/// <summary>Reads documents in the native format (<see cref="NativeFormat"/>).</summary>
/// <param name="kernel">The geometry kernel that reads the B-rep shapes of the documents.</param>
public sealed class NativeDocumentReader(IGeometryKernel kernel) : IDocumentReader
{
    public string FormatName => "Modeling Toolkit";

    public IReadOnlyList<string> Extensions { get; } = [NativeFormat.Extension];

    public Document Read(string path, ICollection<string> warnings, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(warnings);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("The native format is read on little-endian platforms only.");
        }
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File '{path}' does not exist.", fullPath);
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(fullPath);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"'{path}' is not a {NativeFormat.Extension} file: it is not a zip archive.",
                exception);
        }

        using (archive)
        {
            try
            {
                return new ArchiveReader(archive, kernel, progress, cancellationToken).Read();
            }
            catch (Exception exception) when (exception is JsonException or EndOfStreamException
                                                  or ArgumentException or InvalidOperationException
                                                  or GeometryKernelException)
            {
                throw new InvalidDataException(
                    $"'{path}' is not a valid {NativeFormat.Extension} file: {exception.Message}", exception);
            }
        }
    }

    private sealed class ArchiveReader(
        ZipArchive archive, IGeometryKernel kernel, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // A step for each file of the archive that the manifest lists.
        private Steps _steps = new(0, progress, cancellationToken);

        public Document Read()
        {
            Manifest manifest = ReadManifest();
            int files = manifest.Textures.Count + new[] { manifest.Root }.Concat(manifest.Blocks)
                .Sum(block => block.Entities.Count(entity => entity is MeshEntry or BrepEntry));
            _steps = new Steps(files, progress, cancellationToken);
            var document = new Document();

            document.Textures.AddRange(manifest.Textures.Select(ReadTexture));
            document.Materials.AddRange(manifest.Materials.Select(entry => ToMaterial(entry, document.Textures)));

            // Every block exists before any is filled, so that references can point to all of them.
            document.Blocks.AddRange(manifest.Blocks.Select(entry => new Block(entry.Name)));
            document.Root.Name = manifest.Root.Name;
            Fill(document.Root, manifest.Root, document);
            for (int index = 0; index < manifest.Blocks.Count; index++)
            {
                Fill(document.Blocks[index], manifest.Blocks[index], document);
            }
            document.Animations.AddRange((manifest.Animations ?? []).Select(entry => ToAnimation(entry, document)));
            return document;
        }

        private Manifest ReadManifest()
        {
            ZipArchiveEntry entry = Entry(NativeFormat.ManifestEntry);
            using Stream stream = entry.Open();
            Manifest manifest = JsonSerializer.Deserialize(stream, ManifestContext.Default.Manifest)
                ?? throw new InvalidDataException("The manifest is empty.");

            if (manifest.Format != NativeFormat.FormatId)
            {
                throw new InvalidDataException($"Unknown format '{manifest.Format}'.");
            }
            if (manifest.Version > NativeFormat.Version)
            {
                throw new InvalidDataException(
                    $"Format version {manifest.Version} is newer than the supported one ({NativeFormat.Version}).");
            }
            if (manifest.Version < NativeFormat.OldestVersion)
            {
                throw new InvalidDataException(
                    $"Format version {manifest.Version} is no longer read: save the model again from its source.");
            }
            if (manifest.Units != NativeFormat.Units || manifest.UpAxis != NativeFormat.UpAxis)
            {
                throw new InvalidDataException($"Unsupported units '{manifest.Units}' or up axis '{manifest.UpAxis}'.");
            }
            CheckComplete(manifest);
            return manifest;
        }

        // The JSON may leave out, or set to null, what the format requires: found here rather than as a null reference
        // later. Strings are not checked: the objects they are given to reject null.
        private static void CheckComplete(Manifest manifest)
        {
            List<BlockEntry> blocks = [Required(manifest.Root, "root block"), .. Required(manifest.Blocks, "blocks")];
            Required(manifest.Textures, "textures").ForEach(texture => Required(texture, "texture"));
            foreach (MaterialEntry material in Required(manifest.Materials, "materials"))
            {
                Required(Required(material, "material").BaseColor, "base colour");
            }
            foreach (BlockEntry block in blocks)
            {
                foreach (EntityEntry entity in Required(Required(block, "block").Entities, "entities"))
                {
                    if (Required(entity, "entity") is BlockReferenceEntry reference)
                    {
                        Required(reference.Transform, "transformation");
                    }
                }
            }
        }

        private static T Required<T>(T? value, string what)
            where T : class =>
            value ?? throw new InvalidDataException($"The manifest has a missing or null {what}.");

        private Texture ReadTexture(TextureEntry entry)
        {
            _steps.Next();
            using Stream stream = Entry(entry.Path).Open();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            return new Texture(entry.Name, entry.MediaType, data.ToArray());
        }

        private static Material ToMaterial(MaterialEntry entry, List<Texture> textures)
        {
            if (entry.BaseColor.Length != 4)
            {
                throw new InvalidDataException($"Material '{entry.Name}' has an invalid base colour.");
            }
            return new Material(entry.Name)
            {
                BaseColor = new Color(entry.BaseColor[0], entry.BaseColor[1], entry.BaseColor[2], entry.BaseColor[3]),
                Metallic = entry.Metallic,
                Roughness = entry.Roughness,
                BaseColorTexture = entry.BaseColorTexture is int texture ? At(textures, texture, "texture") : null,
                TextureSize = entry.TextureSize is > 0 and float size && float.IsFinite(size) ? size : 1000f,
            };
        }

        private void Fill(Block block, BlockEntry entry, Document document)
        {
            foreach (EntityEntry entityEntry in entry.Entities)
            {
                Entity entity = entityEntry switch
                {
                    MeshEntry mesh => ReadMesh(mesh, document.Materials),
                    BrepEntry shape => ReadShape(shape, document.Materials),
                    BlockReferenceEntry reference => ToBlockReference(reference, document.Blocks),
                    GuideEntry guide => ToGuide(guide),
                    _ => throw new InvalidDataException($"Unknown entity in block '{entry.Name}'."),
                };
                entity.Name = entityEntry.Name;
                block.Entities.Add(entity);
            }
        }

        private static GuideEntity ToGuide(GuideEntry entry)
        {
            if (entry.Point is not { Length: 3 } point || entry.Direction is not { Length: 3 } direction)
            {
                throw new InvalidDataException("A guide needs a point and a direction of 3 values each.");
            }
            try
            {
                return new GuideEntity(new Point3d(point[0], point[1], point[2]), new Vector3d(direction[0], direction[1], direction[2]));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }

        private MeshEntity ReadMesh(MeshEntry entry, IList<Material> materials)
        {
            _steps.Next();
            ZipArchiveEntry file = Entry(entry.Path);
            // Checked before anything is allocated, so that wrong counts cannot ask for gigabytes.
            if (entry.VertexCount < 0 || entry.TriangleCount < 0 || file.Length != MeshFileLength(entry))
            {
                throw new InvalidDataException(
                    $"'{entry.Path}' does not hold {entry.VertexCount} vertices and {entry.TriangleCount} triangles.");
            }
            var vertices = new Point3d[entry.VertexCount];
            var triangles = new Triangle[entry.TriangleCount];
            Vector3[]? normals = entry.HasNormals ? new Vector3[entry.VertexCount] : null;
            Vector2[]? textureCoordinates = entry.HasTextureCoordinates ? new Vector2[entry.VertexCount] : null;

            using (Stream stream = file.Open())
            {
                stream.ReadExactly(MemoryMarshal.AsBytes(vertices.AsSpan()));
                stream.ReadExactly(MemoryMarshal.AsBytes(triangles.AsSpan()));
                if (normals is not null)
                {
                    stream.ReadExactly(MemoryMarshal.AsBytes(normals.AsSpan()));
                }
                if (textureCoordinates is not null)
                {
                    stream.ReadExactly(MemoryMarshal.AsBytes(textureCoordinates.AsSpan()));
                }
            }
            return new MeshEntity(vertices, triangles, normals, textureCoordinates)
            {
                Material = entry.Material is int material ? At(materials, material, "material") : null,
            };
        }

        // The bytes of a mesh file with the counts and the arrays of a manifest entry; see NativeFormat.
        private static long MeshFileLength(MeshEntry entry)
        {
            long perVertex = Unsafe.SizeOf<Point3d>()
                + (entry.HasNormals ? Unsafe.SizeOf<Vector3>() : 0)
                + (entry.HasTextureCoordinates ? Unsafe.SizeOf<Vector2>() : 0);
            return perVertex * entry.VertexCount + (long)Unsafe.SizeOf<Triangle>() * entry.TriangleCount;
        }

        private BrepEntity ReadShape(BrepEntry entry, IList<Material> materials)
        {
            _steps.Next();
            using Stream stream = Entry(entry.Path).Open();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            var faceMaterials = new Dictionary<int, Material>();
            foreach (FaceMaterialEntry face in entry.FaceMaterials ?? [])
            {
                if (face.Face < 0)
                {
                    throw new InvalidDataException($"'{entry.Path}' paints a face of negative index.");
                }
                faceMaterials[face.Face] = At(materials, face.Material, "material");
            }
            return new BrepEntity(kernel, kernel.Deserialize(data.ToArray()), faceMaterials);
        }

        private static BlockReference ToBlockReference(BlockReferenceEntry entry, IList<Block> blocks)
        {
            double[] m = entry.Transform;
            if (m.Length != 12)
            {
                throw new InvalidDataException("A block reference has an invalid transformation.");
            }
            var transform = new Transform(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11]);
            return new BlockReference(At(blocks, entry.Block, "block"), transform);
        }

        private static Animation ToAnimation(AnimationEntry entry, Document document)
        {
            var channels = new List<AnimationChannel>();
            foreach (ChannelEntry channel in Required(entry.Channels, "channels"))
            {
                Block block = channel.Block is { } index ? At(document.Blocks, index, "block") : document.Root;
                if (At(block.Entities, channel.Entity, "entity") is not BlockReference target)
                {
                    throw new InvalidDataException("An animation moves an entity that is not a block reference.");
                }
                try
                {
                    channels.Add(new AnimationChannel(
                        target,
                        Parse<AnimatedProperty>(channel.Property),
                        Parse<AnimationInterpolation>(channel.Interpolation),
                        Required(channel.Times, "times"),
                        Required(channel.Values, "values")));
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException($"Animation '{entry.Name}' is invalid: {exception.Message}", exception);
                }
            }
            if (channels.Count == 0)
            {
                throw new InvalidDataException($"Animation '{entry.Name}' has no channels.");
            }
            return new Animation(Required(entry.Name, "animation name"), channels);

            static T Parse<T>(string? value)
                where T : struct, Enum =>
                Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new InvalidDataException($"'{value}' is not a valid {typeof(T).Name}.");
        }

        private ZipArchiveEntry Entry(string path) =>
            archive.GetEntry(path) ?? throw new InvalidDataException($"The archive has no '{path}' entry.");

        private static T At<T>(IList<T> items, int index, string kind) =>
            (uint)index < (uint)items.Count
                ? items[index]
                : throw new InvalidDataException($"The manifest refers to {kind} {index}, which does not exist.");
    }
}

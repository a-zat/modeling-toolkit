using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using ModelingToolkit.Core.IO;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.IO.Native;

/// <summary>Writes documents in the native format (<see cref="NativeFormat"/>).</summary>
public sealed class NativeDocumentWriter : IDocumentWriter
{
    public string FormatName => "Modeling Toolkit";

    public IReadOnlyList<string> Extensions { get; } = [NativeFormat.Extension];

    /// <exception cref="InvalidOperationException">
    /// An entity uses a block, a material or a texture that is not part of the document.
    /// </exception>
    public void Write(Document document, string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("The native format is written on little-endian platforms only.");
        }

        // Written next to the destination and moved at the end, so a failure never leaves a truncated file.
        string fullPath = Path.GetFullPath(path);
        string temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (FileStream stream = File.Create(temporaryPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                new ArchiveWriter(document, archive, progress, cancellationToken).Write();
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch
        {
            // Best effort: when the folder does not exist, for example, deleting fails too, and must not hide why the
            // writing failed.
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            throw;
        }
    }

    private sealed class ArchiveWriter(
        Document document, ZipArchive archive, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // A step for each file of the archive but the manifest.
        private readonly Steps _steps = new(
            document.Textures.Count + new[] { document.Root }.Concat(document.Blocks)
                .Sum(block => block.Entities.Count(entity => entity is MeshEntity or BrepEntity)),
            progress, cancellationToken);

        private readonly Dictionary<Texture, int> _textures = IndexOf(document.Textures);
        private readonly Dictionary<Material, int> _materials = IndexOf(document.Materials);
        private readonly Dictionary<Block, int> _blocks = IndexOf(document.Blocks);
        private int _meshCount;
        private int _shapeCount;

        public void Write()
        {
            var manifest = new Manifest(
                NativeFormat.FormatId,
                NativeFormat.Version,
                NativeFormat.Units,
                NativeFormat.UpAxis,
                document.Textures.Select(WriteTexture).ToList(),
                document.Materials.Select(ToEntry).ToList(),
                ToEntry(document.Root),
                document.Blocks.Select(ToEntry).ToList(),
                AnimationEntries());

            using Stream stream = archive.CreateEntry(NativeFormat.ManifestEntry, CompressionLevel.Optimal).Open();
            JsonSerializer.Serialize(stream, manifest, ManifestContext.Default.Manifest);
        }

        // The channels of block references that are no longer in the document are left out, and so are the animations
        // left without any.
        private List<AnimationEntry>? AnimationEntries()
        {
            var entries = new List<AnimationEntry>();
            foreach (Animation animation in document.Animations)
            {
                var channels = new List<ChannelEntry>();
                foreach (AnimationChannel channel in animation.Channels)
                {
                    int? block;
                    if (channel.Target.Owner is not { } owner)
                    {
                        continue;
                    }
                    if (owner == document.Root)
                    {
                        block = null;
                    }
                    else if (_blocks.TryGetValue(owner, out int index))
                    {
                        block = index;
                    }
                    else
                    {
                        continue;
                    }
                    channels.Add(new ChannelEntry(
                        block,
                        owner.Entities.IndexOf(channel.Target),
                        JsonNamingPolicy.CamelCase.ConvertName(channel.Property.ToString()),
                        JsonNamingPolicy.CamelCase.ConvertName(channel.Interpolation.ToString()),
                        [.. channel.Times],
                        [.. channel.Values]));
                }
                if (channels.Count > 0)
                {
                    entries.Add(new AnimationEntry(animation.Name, channels));
                }
            }
            return entries.Count > 0 ? entries : null;
        }

        private TextureEntry WriteTexture(Texture texture, int index)
        {
            _steps.Next();
            string path = $"textures/{index}{ExtensionOf(texture.MediaType)}";
            // Images are already compressed: storing them as they are is faster and as small.
            using Stream stream = archive.CreateEntry(path, CompressionLevel.NoCompression).Open();
            stream.Write(texture.Data);
            return new TextureEntry(texture.Name, texture.MediaType, path);
        }

        private MaterialEntry ToEntry(Material material) => new(
            material.Name,
            [material.BaseColor.R, material.BaseColor.G, material.BaseColor.B, material.BaseColor.A],
            material.Metallic,
            material.Roughness,
            material.BaseColorTexture is null ? null : Find(_textures, material.BaseColorTexture, "texture"),
            material.BaseColorTexture is null ? null : material.TextureSize);

        private BlockEntry ToEntry(Block block) => new(block.Name, block.Entities.Select(ToEntry).ToList());

        private EntityEntry ToEntry(Entity entity)
        {
            return entity switch
            {
                MeshEntity mesh => WriteMesh(mesh, mesh.Material is null ? null : Find(_materials, mesh.Material, "material")),
                BrepEntity brep => WriteShape(brep),
                BlockReference reference => new BlockReferenceEntry(
                    reference.Name,
                    Find(_blocks, reference.Block, "block"),
                    [
                        reference.Transform.M00, reference.Transform.M01, reference.Transform.M02, reference.Transform.M03,
                        reference.Transform.M10, reference.Transform.M11, reference.Transform.M12, reference.Transform.M13,
                        reference.Transform.M20, reference.Transform.M21, reference.Transform.M22, reference.Transform.M23,
                    ]),
                GuideEntity guide => new GuideEntry(guide.Name, [guide.Point.X, guide.Point.Y, guide.Point.Z],
                    [guide.Direction.X, guide.Direction.Y, guide.Direction.Z]),
                _ => throw new NotSupportedException($"Entities of type {entity.GetType().Name} cannot be written."),
            };
        }

        private MeshEntry WriteMesh(MeshEntity mesh, int? material)
        {
            _steps.Next();
            string path = $"meshes/{_meshCount++}.bin";
            using (Stream stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open())
            {
                stream.Write(MemoryMarshal.AsBytes(mesh.Vertices.AsSpan()));
                stream.Write(MemoryMarshal.AsBytes(mesh.Triangles.AsSpan()));
                if (mesh.Normals is not null)
                {
                    stream.Write(MemoryMarshal.AsBytes(mesh.Normals.AsSpan()));
                }
                if (mesh.TextureCoordinates is not null)
                {
                    stream.Write(MemoryMarshal.AsBytes(mesh.TextureCoordinates.AsSpan()));
                }
            }
            return new MeshEntry(
                mesh.Name,
                material,
                path,
                mesh.Vertices.Length,
                mesh.Triangles.Length,
                mesh.Normals is not null,
                mesh.TextureCoordinates is not null);
        }

        private BrepEntry WriteShape(BrepEntity brep)
        {
            _steps.Next();
            string path = $"shapes/{_shapeCount++}.brep";
            using (Stream stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open())
            {
                stream.Write(brep.Kernel.Serialize(brep.Shape));
            }
            List<FaceMaterialEntry>? faces = brep.FaceMaterials.Count == 0
                ? null
                : [.. brep.FaceMaterials.OrderBy(pair => pair.Key)
                    .Select(pair => new FaceMaterialEntry(pair.Key, Find(_materials, pair.Value, "material")))];
            return new BrepEntry(brep.Name, path, faces);
        }

        private static Dictionary<T, int> IndexOf<T>(IList<T> items)
            where T : class
        {
            var indices = new Dictionary<T, int>(ReferenceEqualityComparer.Instance);
            for (int index = 0; index < items.Count; index++)
            {
                indices.TryAdd(items[index], index);
            }
            return indices;
        }

        private static int Find<T>(Dictionary<T, int> indices, T item, string kind)
            where T : notnull =>
            indices.TryGetValue(item, out int index)
                ? index
                : throw new InvalidOperationException($"A {kind} used by the document is not in its list of {kind}s.");

        private static string ExtensionOf(string mediaType) => mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".bin",
        };
    }
}

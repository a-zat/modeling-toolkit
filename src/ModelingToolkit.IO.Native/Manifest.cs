using System.Text.Json.Serialization;

namespace ModelingToolkit.IO.Native;

// The content of manifest.json. Objects refer to each other by their index in the lists of the manifest.

internal sealed record Manifest(
    string Format,
    int Version,
    string Units,
    string UpAxis,
    List<TextureEntry> Textures,
    List<MaterialEntry> Materials,
    BlockEntry Root,
    List<BlockEntry> Blocks);

internal sealed record TextureEntry(string Name, string MediaType, string Path);

internal sealed record MaterialEntry(
    string Name,
    float[] BaseColor,
    float Metallic,
    float Roughness,
    int? BaseColorTexture);

internal sealed record BlockEntry(string Name, List<EntityEntry> Entities);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MeshEntry), "mesh")]
[JsonDerivedType(typeof(BrepEntry), "brep")]
[JsonDerivedType(typeof(BlockReferenceEntry), "blockReference")]
internal abstract record EntityEntry(string? Name);

internal sealed record MeshEntry(
    string? Name,
    int? Material,
    string Path,
    int VertexCount,
    int TriangleCount,
    bool HasNormals,
    bool HasTextureCoordinates) : EntityEntry(Name);

/// <param name="FaceMaterials">The faces painted, by their index in the shape; null for none.</param>
internal sealed record BrepEntry(string? Name, string Path, List<FaceMaterialEntry>? FaceMaterials) : EntityEntry(Name);

internal sealed record FaceMaterialEntry(int Face, int Material);

internal sealed record BlockReferenceEntry(string? Name, int Block, double[] Transform) : EntityEntry(Name);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(Manifest))]
internal sealed partial class ManifestContext : JsonSerializerContext
{
}

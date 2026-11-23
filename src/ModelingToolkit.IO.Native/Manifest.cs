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
    List<BlockEntry> Blocks,
    List<AnimationEntry>? Animations = null,
    List<AttachmentEntry>? Attachments = null,
    List<LayerEntry>? Layers = null);

/// <param name="Hidden">True for a hidden layer; absent otherwise.</param>
internal sealed record LayerEntry(string Name, bool? Hidden = null);

internal sealed record TextureEntry(string Name, string MediaType, string Path);

/// <param name="TextureSize">With a texture, the millimetres it covers once on the faces of shapes.</param>
internal sealed record MaterialEntry(
    string Name,
    float[] BaseColor,
    float Metallic,
    float Roughness,
    int? BaseColorTexture,
    float? TextureSize = null);

internal sealed record BlockEntry(string Name, List<EntityEntry> Entities);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(MeshEntry), "mesh")]
[JsonDerivedType(typeof(BrepEntry), "brep")]
[JsonDerivedType(typeof(BlockReferenceEntry), "blockReference")]
[JsonDerivedType(typeof(GuideEntry), "guide")]
[JsonDerivedType(typeof(AnchorEntry), "anchor")]
internal abstract record EntityEntry(string? Name)
{
    /// <summary>True for a hidden entity; absent otherwise.</summary>
    public bool? Hidden { get; init; }

    /// <summary>The index of the layer of the entity in the layers of the manifest; absent for none.</summary>
    public int? Layer { get; init; }
}

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

internal sealed record GuideEntry(string? Name, double[] Point, double[] Direction) : EntityEntry(Name);

/// <param name="Placement">The 12 values of the transform of the anchor, by rows.</param>
/// <param name="Kind">attach or pointOfView.</param>
internal sealed record AnchorEntry(string? Name, double[] Placement, string Kind) : EntityEntry(Name);

/// <param name="Block">The block holding the follower, null for the root.</param>
/// <param name="Follower">The index of the follower among the entities of its block.</param>
/// <param name="Chain">
/// The references down to the block of the anchor, each by its index among the entities of the block of the one before,
/// the first in the block of the follower.
/// </param>
/// <param name="Anchor">The index of the anchor among the entities of the block of the last reference.</param>
/// <param name="Offset">The 12 values of the offset from the anchor, by rows.</param>
internal sealed record AttachmentEntry(int? Block, int Follower, int[] Chain, int Anchor, double[] Offset);

internal sealed record BlockReferenceEntry(string? Name, int Block, double[] Transform) : EntityEntry(Name);

internal sealed record AnimationEntry(string Name, List<ChannelEntry> Channels);

/// <param name="Block">The block holding the reference moved, null for the root.</param>
/// <param name="Entity">The index of the reference among the entities of its block.</param>
/// <param name="Property">translation, rotation or scale.</param>
/// <param name="Interpolation">step, linear or cubicSpline.</param>
internal sealed record ChannelEntry(
    int? Block, int Entity, string Property, string Interpolation, double[] Times, double[] Values);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(Manifest))]
internal sealed partial class ManifestContext : JsonSerializerContext
{
}

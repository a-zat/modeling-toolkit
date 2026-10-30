using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A 3D model: the entities of <see cref="Root"/>, the blocks they reference, and the materials and textures they use.
/// Lengths are in millimetres and the Z axis points up.
/// </summary>
public sealed class Document
{
    /// <summary>The model space: the block holding the top-level entities, referenced by no other block.</summary>
    public Block Root { get; } = new("Model");

    /// <summary>The blocks referenced by <see cref="BlockReference"/>s.</summary>
    public List<Block> Blocks { get; } = [];

    public List<Material> Materials { get; } = [];

    public List<Texture> Textures { get; } = [];

    public BoundingBox GetBoundingBox() => Root.GetBoundingBox();
}

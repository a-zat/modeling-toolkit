using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A 3D model: the entities of <see cref="Root"/>, the blocks they reference, and the materials and textures they use.
/// Lengths are in millimetres and the Z axis points up.
/// </summary>
public sealed class Document
{
    public Document()
    {
        Root = new Block("Model") { Document = this };
        Blocks = new BlockCollection(this);
    }

    /// <summary>The model space: the block holding the top-level entities, referenced by no other block.</summary>
    public Block Root { get; }

    /// <summary>
    /// The blocks referenced by <see cref="BlockReference"/>s. Only the changes of the blocks listed here, and of
    /// <see cref="Root"/>, are raised by <see cref="Changed"/>.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Raised after every change of the structure of the document or of the properties of its blocks and entities,
    /// on the thread that made it. Materials and textures do not raise it.
    /// </summary>
    public event EventHandler<DocumentChange>? Changed;

    public List<Material> Materials { get; } = [];

    public List<Texture> Textures { get; } = [];

    public BoundingBox GetBoundingBox() => Root.GetBoundingBox();

    internal void Raise(DocumentChange change) => Changed?.Invoke(this, change);
}

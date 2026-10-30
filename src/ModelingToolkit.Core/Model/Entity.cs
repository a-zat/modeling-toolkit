using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>Base class of the objects that make up a <see cref="Block"/>: meshes, references to other blocks...</summary>
/// <remarks>The set of entity kinds is closed: only this assembly can derive from this class.</remarks>
public abstract class Entity
{
    private protected Entity()
    {
    }

    public string? Name { get; set; }

    /// <summary>
    /// Material of the entity. Null means "by block", as in CAD: the material of the <see cref="BlockReference"/>
    /// through which the entity is displayed, or the default material.
    /// </summary>
    public Material? Material { get; set; }

    /// <summary>Bounding box in the coordinates of the containing block.</summary>
    public BoundingBox GetBoundingBox() => ComputeBoundingBox(new Dictionary<Block, BoundingBox>());

    /// <param name="blockBoxes">Boxes of the blocks already computed, so that shared blocks are measured once.</param>
    internal abstract BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes);
}

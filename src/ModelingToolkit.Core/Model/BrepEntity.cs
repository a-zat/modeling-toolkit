using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// An exact shape of a geometry kernel (a solid, for example), in the coordinates of its block. Unlike a
/// <see cref="MeshEntity"/> it is not made of triangles: they are made when needed, see
/// <see cref="IGeometryKernel.Tessellate"/>.
/// </summary>
public sealed class BrepEntity : Entity
{
    public BrepEntity(IGeometryKernel kernel, IKernelShape shape)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(shape);

        Kernel = kernel;
        Shape = shape;
    }

    /// <summary>The kernel that made the shape and operates on it.</summary>
    public IGeometryKernel Kernel { get; }

    /// <summary>
    /// The shape, made by <see cref="Kernel"/>. Shapes are immutable: an edit replaces the shape with a new one.
    /// </summary>
    public IKernelShape Shape
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ReferenceEquals(field, value))
            {
                IKernelShape? old = field;
                field = value;
                if (old is not null)
                {
                    Raise(new ShapeReplaced(this, old, value));
                }
            }
        }
    }

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes) =>
        Kernel.GetBoundingBox(Shape);
}

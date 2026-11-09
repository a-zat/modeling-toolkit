using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Kernel;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// An exact shape of a geometry kernel (a solid, for example), in the coordinates of its block, with a material on each
/// face that is painted. Unlike a <see cref="MeshEntity"/> it is not made of triangles: they are made when needed, see
/// <see cref="IGeometryKernel.Tessellate"/>.
/// </summary>
public sealed class BrepEntity : Entity
{
    private Dictionary<int, Material> _faceMaterials;

    /// <param name="faceMaterials">The materials of the faces painted, by their index in the shape.</param>
    /// <exception cref="ArgumentOutOfRangeException">A face index is negative.</exception>
    public BrepEntity(IGeometryKernel kernel, IKernelShape shape, IReadOnlyDictionary<int, Material>? faceMaterials = null)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(shape);

        Kernel = kernel;
        _shape = shape;
        _faceMaterials = Checked(faceMaterials);
    }

    /// <summary>The kernel that made the shape and operates on it.</summary>
    public IGeometryKernel Kernel { get; }

    /// <summary>
    /// The shape, made by <see cref="Kernel"/>. Shapes are immutable: an edit replaces the shape with a new one, whose
    /// faces are not painted; <see cref="Replace"/> gives them their materials.
    /// </summary>
    public IKernelShape Shape
    {
        get => _shape;
        set => Replace(value, null);
    }

    private IKernelShape _shape;

    /// <summary>
    /// The materials of the faces painted, by the index of the face in the shape (see
    /// <see cref="IGeometryKernel.Tessellate"/>); the other faces have the default material.
    /// </summary>
    public IReadOnlyDictionary<int, Material> FaceMaterials => _faceMaterials;

    /// <summary>The material of a face; null for the default material.</summary>
    public Material? MaterialOf(int face) => _faceMaterials.GetValueOrDefault(face);

    /// <summary>Paints faces with a material, or takes their paint away with null.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A face index is negative.</exception>
    public void Paint(IEnumerable<int> faces, Material? material)
    {
        ArgumentNullException.ThrowIfNull(faces);
        bool changed = false;
        foreach (int face in faces)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(face, nameof(faces));
            if (ReferenceEquals(MaterialOf(face), material))
            {
                continue;
            }
            if (material is null)
            {
                _faceMaterials.Remove(face);
            }
            else
            {
                _faceMaterials[face] = material;
            }
            changed = true;
        }
        if (changed)
        {
            Raise(new PropertyChanged(this, nameof(FaceMaterials)));
        }
    }

    /// <summary>
    /// Replaces the shape, and the materials of the faces with those of the faces of the new one: the faces of a shape
    /// are numbered anew, so the materials of the old faces mean nothing for the new shape.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A face index is negative.</exception>
    public void Replace(IKernelShape shape, IReadOnlyDictionary<int, Material>? faceMaterials)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Dictionary<int, Material> materials = Checked(faceMaterials);
        if (ReferenceEquals(_shape, shape))
        {
            return;
        }

        IKernelShape old = _shape;
        _shape = shape;
        _faceMaterials = materials;
        Raise(new ShapeReplaced(this, old, shape));
    }

    private static Dictionary<int, Material> Checked(IReadOnlyDictionary<int, Material>? faceMaterials)
    {
        var materials = new Dictionary<int, Material>();
        foreach ((int face, Material material) in faceMaterials ?? new Dictionary<int, Material>())
        {
            ArgumentOutOfRangeException.ThrowIfNegative(face, nameof(faceMaterials));
            ArgumentNullException.ThrowIfNull(material, nameof(faceMaterials));
            materials[face] = material;
        }
        return materials;
    }

    internal override BoundingBox ComputeBoundingBox(Dictionary<Block, BoundingBox> blockBoxes) =>
        Kernel.GetBoundingBox(Shape);
}

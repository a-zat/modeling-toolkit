using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>Gives an entity a name, or takes its name away with null.</summary>
public sealed class RenameCommand(Entity entity, string? name) : IEditCommand
{
    private readonly Entity _entity = entity ?? throw new ArgumentNullException(nameof(entity));
    private readonly string? _before = entity.Name;

    public string Name => "Rename";

    public void Apply() => _entity.Name = name;

    public void Revert() => _entity.Name = _before;
}

/// <summary>Gives a mesh a material, or the default material with null.</summary>
public sealed class SetMeshMaterialCommand(MeshEntity mesh, Material? material) : IEditCommand
{
    private readonly MeshEntity _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
    private readonly Material? _before = mesh.Material;

    public string Name => "Paint";

    public void Apply() => _mesh.Material = material;

    public void Revert() => _mesh.Material = _before;
}

/// <summary>Paints faces of a shape with a material, or takes their paint away with null.</summary>
public sealed class PaintFacesCommand : IEditCommand
{
    private readonly BrepEntity _entity;
    private readonly int[] _faces;
    private readonly Material? _material;
    private readonly Material?[] _before;

    /// <exception cref="ArgumentOutOfRangeException">A face index is negative.</exception>
    public PaintFacesCommand(BrepEntity entity, IEnumerable<int> faces, Material? material)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(faces);
        _entity = entity;
        _faces = [.. faces.Distinct()];
        foreach (int face in _faces)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(face, nameof(faces));
        }
        _material = material;
        _before = [.. _faces.Select(entity.MaterialOf)];
    }

    public string Name => "Paint";

    public void Apply() => _entity.Paint(_faces, _material);

    public void Revert()
    {
        foreach (IGrouping<Material?, int> faces in _faces.Zip(_before).GroupBy(pair => pair.Second, pair => pair.First))
        {
            _entity.Paint(faces, faces.Key);
        }
    }
}

/// <summary>Removes an entity from its block; undoing puts it back where it was.</summary>
public sealed class DeleteCommand : IEditCommand
{
    private readonly Entity _entity;
    private readonly Block _block;
    private readonly int _index;

    /// <exception cref="ArgumentException">The entity is in no block.</exception>
    public DeleteCommand(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _entity = entity;
        _block = entity.Owner ?? throw new ArgumentException("The entity is in no block.", nameof(entity));
        _index = _block.Entities.IndexOf(entity);
    }

    public string Name => "Delete";

    public void Apply() => _block.Entities.RemoveAt(_index);

    public void Revert() => _block.Entities.Insert(_index, _entity);
}

/// <summary>
/// Replaces the shape of a B-rep entity with the result of an operation. The result is checked first: one that is not
/// valid is refused, and the entity keeps its shape.
/// </summary>
public sealed class ReplaceShapeCommand : IEditCommand
{
    private readonly BrepEntity _entity;
    private readonly IKernelShape _before;
    private readonly Dictionary<int, Material> _beforeMaterials;
    private readonly IKernelShape _after;
    private readonly IReadOnlyDictionary<int, Material>? _afterMaterials;
    private bool _checked;

    /// <param name="faceMaterials">
    /// The materials of the faces of the new shape, carried from those of the old one through the operation; null for
    /// none painted.
    /// </param>
    public ReplaceShapeCommand(
        string name, BrepEntity entity, IKernelShape shape, IReadOnlyDictionary<int, Material>? faceMaterials = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(shape);
        Name = name;
        _entity = entity;
        _before = entity.Shape;
        _beforeMaterials = new Dictionary<int, Material>(entity.FaceMaterials);
        _after = shape;
        _afterMaterials = faceMaterials;
    }

    public string Name { get; }

    /// <exception cref="EditRefusedException">The new shape is not valid.</exception>
    public void Apply()
    {
        if (!_checked)
        {
            if (!_entity.Kernel.IsValid(_after))
            {
                throw new EditRefusedException($"{Name} gives a shape that is not valid: the shape stays as it was.");
            }
            _checked = true;
        }

        _entity.Replace(_after, _afterMaterials);
    }

    public void Revert() => _entity.Replace(_before, _beforeMaterials);
}

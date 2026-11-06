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

/// <summary>Gives an entity a material, or null for the material "by block".</summary>
public sealed class SetMaterialCommand(Entity entity, Material? material) : IEditCommand
{
    private readonly Entity _entity = entity ?? throw new ArgumentNullException(nameof(entity));
    private readonly Material? _before = entity.Material;

    public string Name => "Change Material";

    public void Apply() => _entity.Material = material;

    public void Revert() => _entity.Material = _before;
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
    private readonly IKernelShape _after;
    private bool _checked;

    public ReplaceShapeCommand(string name, BrepEntity entity, IKernelShape shape)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(shape);
        Name = name;
        _entity = entity;
        _before = entity.Shape;
        _after = shape;
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

        _entity.Shape = _after;
    }

    public void Revert() => _entity.Shape = _before;
}

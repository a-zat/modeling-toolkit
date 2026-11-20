using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>Gives an entity a name, or takes its name away with null.</summary>
public sealed class RenameCommand : IEditCommand
{
    private readonly Entity _entity;
    private readonly string? _name;
    private readonly string? _before;
    private readonly Block? _block;
    private readonly string? _blockBefore;

    /// <summary>
    /// Renames an entity; given the document, a reference renames its block too when it is the only one placing it and
    /// no other block has the name, so that a block made for one place is not shown by its old name beside it.
    /// </summary>
    public RenameCommand(Entity entity, string? name, Document? document = null)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _name = name;
        _before = entity.Name;
        if (document is not null && entity is BlockReference { Block: var block } && !string.IsNullOrWhiteSpace(name) &&
            document.Blocks.Prepend(document.Root).SelectMany(b => b.Entities).OfType<BlockReference>()
                .Count(reference => reference.Block == block) == 1 &&
            !document.Blocks.Any(other => other != block && other.Name == name))
        {
            _block = block;
            _blockBefore = block.Name;
        }
    }

    public string Name => "Rename";

    public void Apply()
    {
        _entity.Name = _name;
        if (_block is not null)
        {
            _block.Name = _name!;
        }
    }

    public void Revert()
    {
        _entity.Name = _before;
        if (_block is not null)
        {
            _block.Name = _blockBefore!;
        }
    }
}

/// <summary>Renames a block, which every reference to it shows when it has no name of its own.</summary>
public sealed class RenameBlockCommand : IEditCommand
{
    private readonly Block _block;
    private readonly string _name;
    private readonly string _before;

    /// <exception cref="EditRefusedException">The name is empty, or another block of the document has it.</exception>
    public RenameBlockCommand(Block block, string name, Document? document = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new EditRefusedException("A block needs a name.");
        }
        if (document is not null && document.Blocks.Any(other => other != block && other.Name == name.Trim()))
        {
            throw new EditRefusedException($"Another block is named {name.Trim()}.");
        }
        _block = block;
        _name = name.Trim();
        _before = block.Name;
    }

    public string Name => "Rename block";

    public void Apply() => _block.Name = _name;

    public void Revert() => _block.Name = _before;
}

/// <summary>Shows or hides entities, as layers are switched on or off, as one command.</summary>
public sealed class SetHiddenCommand : IEditCommand
{
    private readonly (Entity Entity, bool Before)[] _entities;
    private readonly bool _hidden;

    public SetHiddenCommand(IEnumerable<Entity> entities, bool hidden)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entities = [.. entities.Select(entity => (entity, entity.IsHidden))];
        _hidden = hidden;
    }

    public string Name => _hidden ? "Hide" : "Show";

    public void Apply()
    {
        foreach ((Entity entity, _) in _entities)
        {
            entity.IsHidden = _hidden;
        }
    }

    public void Revert()
    {
        foreach ((Entity entity, bool before) in _entities)
        {
            entity.IsHidden = before;
        }
    }
}

/// <summary>
/// Adds a material to the materials of a document, with its texture to the textures, as one chosen from a library or
/// made from an image is when first used.
/// </summary>
public sealed class AddMaterialCommand(Document document, Material material) : IEditCommand
{
    private readonly Document _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly Material _material = material ?? throw new ArgumentNullException(nameof(material));
    private bool _addedTexture;

    public string Name => "Add Material";

    public void Apply()
    {
        if (!_document.Materials.Contains(_material))
        {
            _document.Materials.Add(_material);
        }
        if (_material.BaseColorTexture is { } texture && !_document.Textures.Contains(texture))
        {
            _document.Textures.Add(texture);
            _addedTexture = true;
        }
    }

    public void Revert()
    {
        _document.Materials.Remove(_material);
        if (_addedTexture)
        {
            _document.Textures.Remove(_material.BaseColorTexture!);
            _addedTexture = false;
        }
    }
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

/// <summary>Adds an entity at the end of a block; undoing takes it away.</summary>
public sealed class AddEntityCommand(string name, Block block, Entity entity) : IEditCommand
{
    private readonly Block _block = block ?? throw new ArgumentNullException(nameof(block));
    private readonly Entity _entity = entity ?? throw new ArgumentNullException(nameof(entity));

    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public void Apply() => _block.Entities.Add(_entity);

    public void Revert() => _block.Entities.Remove(_entity);
}

/// <summary>
/// Adds an empty block to a document, placed in another block by a reference at its origin, to group what goes into it;
/// undoing takes both away.
/// </summary>
public sealed class NewBlockCommand : IEditCommand
{
    private readonly Document _document;
    private readonly Block _into;

    public NewBlockCommand(Document document, Block into, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _document = document;
        _into = into;
        // A name of its own among the blocks: "Room", "Room 2"...
        string unique = name;
        for (int n = 2; document.Blocks.Any(block => block.Name == unique); n++)
        {
            unique = $"{name} {n}";
        }
        Reference = new BlockReference(new Block(unique), Geometry.Transform.Identity);
    }

    /// <summary>The reference that places the new block.</summary>
    public BlockReference Reference { get; }

    public string Name => "New block";

    public void Apply()
    {
        _document.Blocks.Add(Reference.Block);
        _into.Entities.Add(Reference);
    }

    public void Revert()
    {
        _into.Entities.Remove(Reference);
        _document.Blocks.Remove(Reference.Block);
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

    /// <summary>
    /// Replaces the shape with the result of an edit by the kernel. Each face of the new shape takes the material of
    /// the face it comes from, so that the parts of a painted face that was split stay painted; the faces the edit made
    /// have none. The kernel has checked the parts it changed, so the result is not checked again.
    /// </summary>
    public ReplaceShapeCommand(string name, BrepEntity entity, ShapeEdit edit)
        : this(name, entity, (edit ?? throw new ArgumentNullException(nameof(edit))).Shape, MaterialsAfter(entity, edit))
    {
        _checked = true;
    }

    public string Name { get; }

    private static Dictionary<int, Material> MaterialsAfter(BrepEntity entity, ShapeEdit edit)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var materials = new Dictionary<int, Material>();
        for (int face = 0; face < edit.FaceOrigins.Count; face++)
        {
            if (edit.FaceOrigins[face] is >= 0 and int origin && entity.MaterialOf(origin) is { } material)
            {
                materials[face] = material;
            }
        }
        return materials;
    }

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

/// <summary>Makes a reference follow an anchor, or stops it from following one; undoing does the other.</summary>
public sealed class AttachCommand(string name, Document document, Attachment attachment, bool attach) : IEditCommand
{
    private readonly Document _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly Attachment _attachment = attachment ?? throw new ArgumentNullException(nameof(attachment));

    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public void Apply() => Set(attach);

    public void Revert() => Set(!attach);

    private void Set(bool attached)
    {
        if (attached)
        {
            try
            {
                _document.Attach(_attachment);
            }
            catch (ArgumentException exception)
            {
                throw new EditRefusedException(exception.Message);
            }
        }
        else
        {
            _document.Detach(_attachment);
        }
    }
}

/// <summary>Makes a reference a group, or a component again; undoing does the other.</summary>
public sealed class SetGroupCommand(BlockReference reference, bool group) : IEditCommand
{
    private readonly BlockReference _reference = reference ?? throw new ArgumentNullException(nameof(reference));

    public string Name => group ? "Make group" : "Convert to component";

    public void Apply() => _reference.IsGroup = group;

    public void Revert() => _reference.IsGroup = !group;
}

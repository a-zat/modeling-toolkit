using System.Numerics;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>The commands that move, rotate and copy entities.</summary>
public static class TransformCommands
{
    /// <summary>
    /// Transforms an entity in the coordinates of its block, or adds a copy of it so transformed: a reference takes the
    /// transform before its own; a B-rep shape is placed again, its faces keeping their materials; a mesh has its
    /// vertices and normals transformed. The transform must be a rotation and a translation.
    /// </summary>
    /// <exception cref="ArgumentException">The entity is in no block.</exception>
    public static IEditCommand Of(string name, Entity entity, Transform transform, bool copy = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(transform);
        Block block = entity.Owner ?? throw new ArgumentException("The entity is in no block.", nameof(entity));
        if (!copy && entity is BlockReference moved)
        {
            return new SetTransformCommand(name, moved, transform * moved.Transform);
        }
        if (!copy && entity is BrepEntity brep)
        {
            return new ReplaceShapeCommand(
                name, brep, brep.Kernel.Place(brep.Shape, transform), new Dictionary<int, Material>(brep.FaceMaterials));
        }
        Entity made = Transformed(entity, transform);
        made.Name = entity.Name;
        return copy ? new AddEntityCommand(name, block, made) : new ReplaceEntityCommand(name, entity, made);
    }

    private static Entity Transformed(Entity entity, Transform transform) => entity switch
    {
        BlockReference reference => new BlockReference(reference.Block, transform * reference.Transform),
        BrepEntity brep => new BrepEntity(brep.Kernel, brep.Kernel.Place(brep.Shape, transform), brep.FaceMaterials),
        MeshEntity mesh => new MeshEntity(
            [.. mesh.Vertices.Select(transform.Apply)],
            mesh.Triangles,
            mesh.Normals?.Select(normal => Turned(normal, transform)).ToArray(),
            mesh.TextureCoordinates) { Material = mesh.Material },
        _ => throw new ArgumentException($"An entity of type {entity.GetType().Name} cannot be transformed.", nameof(entity)),
    };

    private static Vector3 Turned(Vector3 normal, Transform transform)
    {
        Vector3d turned = transform.ApplyVector(new Vector3d(normal.X, normal.Y, normal.Z));
        return Vector3.Normalize(new Vector3((float)turned.X, (float)turned.Y, (float)turned.Z));
    }
}

/// <summary>Sets the transform of a reference; undoing puts the old one back.</summary>
public sealed class SetTransformCommand(string name, BlockReference reference, Transform transform) : IEditCommand
{
    private readonly BlockReference _reference = reference ?? throw new ArgumentNullException(nameof(reference));
    private readonly Transform _before = reference.Transform;
    private readonly Transform _after = transform ?? throw new ArgumentNullException(nameof(transform));

    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public void Apply() => _reference.Transform = _after;

    public void Revert() => _reference.Transform = _before;
}

/// <summary>Puts an entity in the place of another in its block; undoing puts the old one back.</summary>
public sealed class ReplaceEntityCommand : IEditCommand
{
    private readonly Entity _old;
    private readonly Entity _new;
    private readonly Block _block;

    /// <exception cref="ArgumentException">The old entity is in no block.</exception>
    public ReplaceEntityCommand(string name, Entity old, Entity replacement)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(replacement);
        Name = name;
        _old = old;
        _new = replacement;
        _block = old.Owner ?? throw new ArgumentException("The entity is in no block.", nameof(old));
    }

    public string Name { get; }

    public void Apply() => _block.Entities[_block.Entities.IndexOf(_old)] = _new;

    public void Revert() => _block.Entities[_block.Entities.IndexOf(_new)] = _old;
}

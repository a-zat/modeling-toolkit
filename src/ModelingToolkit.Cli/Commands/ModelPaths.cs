using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Cli.Commands;

/// <summary>
/// Entities of a model named by paths, as the outline shows them: the names from the top of the model down, separated
/// by "/", each the name of the entity or, for a reference without one, of its block; "." is the model itself.
/// </summary>
internal static class ModelPaths
{
    public static string NameOf(Entity entity) =>
        entity.Name ?? (entity as BlockReference)?.Block.Name ?? (entity as MeshEntity)?.Material?.Name ?? entity.GetType().Name;

    /// <summary>The name of an entity in a path: <see cref="NameOf"/>, with "#N" after it when others before it in its block have it.</summary>
    public static string StepOf(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        string name = NameOf(entity);
        if (entity.Owner is not { } block)
        {
            return name;
        }
        int same = 0;
        foreach (Entity other in block.Entities)
        {
            if (NameOf(other) == name)
            {
                same++;
            }
            if (ReferenceEquals(other, entity))
            {
                break;
            }
        }
        return same > 1 ? $"{name}#{same}" : name;
    }

    /// <summary>
    /// The references that only wrap the model, as the root nodes of glTF files do: from the top, each alone in its
    /// block. The outline starts inside them, and so do the paths.
    /// </summary>
    public static IReadOnlyList<Entity> Wrappers(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var wrappers = new List<Entity>();
        Block block = document.Root;
        while (block.Entities is [BlockReference only] && wrappers.Count < 16)
        {
            wrappers.Add(only);
            block = only.Block;
        }
        return wrappers;
    }

    /// <summary>Takes a model read from a glTF file out of the node that wraps it, as the application does.</summary>
    public static void UnwrapGltf(Document document, string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is ".glb" or ".gltf")
        {
            DocumentRoot.Unwrap(document);
        }
    }

    /// <summary>The block of the model, inside its wrappers.</summary>
    public static Block ModelBlock(Document document) =>
        Wrappers(document) is [.., BlockReference last] ? last.Block : document.Root;

    /// <summary>The entities from the top of the model to the one named; empty for "."; null when none is.</summary>
    public static IReadOnlyList<Entity>? Find(Document document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Trim() is "." or "")
        {
            return Wrappers(document);
        }
        var found = new List<Entity>(Wrappers(document));
        Block block = found.Count == 0 ? document.Root : ((BlockReference)found[^1]).Block;
        string[] names = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < names.Length; i++)
        {
            Entity? entity = block.Entities.FirstOrDefault(candidate => StepOf(candidate) == names[i]);
            if (entity is null)
            {
                return null;
            }
            found.Add(entity);
            if (i < names.Length - 1)
            {
                if (entity is not BlockReference reference)
                {
                    return null;
                }
                block = reference.Block;
            }
        }
        return found;
    }
}

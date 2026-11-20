using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>How a click goes through the groups (<see cref="BlockReference.IsGroup"/>) to what they hold.</summary>
public static class Groups
{
    /// <summary>
    /// The index in a path of what a click on it selects, given the index of the first entity in the block being edited:
    /// past the groups, to the reference inside them; a group holding the mesh clicked itself is selected.
    /// </summary>
    public static int Through(IReadOnlyList<Entity> path, int index)
    {
        ArgumentNullException.ThrowIfNull(path);
        while (index + 1 < path.Count && path[index] is BlockReference { IsGroup: true } &&
               path[index + 1] is BlockReference)
        {
            index++;
        }
        return index;
    }

    /// <summary>
    /// Whether a block holds only references, as a group does: a group is a container of components, with no geometry
    /// of its own.
    /// </summary>
    public static bool HoldsOnlyComponents(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Entities.All(entity => entity is BlockReference);
    }

    /// <summary>
    /// Makes components of the groups of a document that hold geometry of their own, as a file from elsewhere may have
    /// them, so that a group is only a container of components. Returns how many it made components.
    /// </summary>
    public static int Repair(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int repaired = 0;
        foreach (BlockReference group in document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities)
                     .OfType<BlockReference>().Where(reference => reference.IsGroup && !HoldsOnlyComponents(reference.Block)))
        {
            group.IsGroup = false;
            repaired++;
        }
        return repaired;
    }
}

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
}

namespace ModelingToolkit.Core.Model;

/// <summary>Changes of the model space of a document as a whole.</summary>
public static class DocumentRoot
{
    /// <summary>
    /// Takes the entities of the model space out of the blocks that only wrap them: while the model space holds a single
    /// visible reference, placed as it is, to a block placed nowhere else, the entities of that block become those of
    /// the model space, which takes its name. A glTF file whose scene is a node holding the whole model thus shows that
    /// node as the model. Returns whether anything changed.
    /// </summary>
    public static bool Unwrap(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        bool changed = false;
        while (document.Root.Entities is [BlockReference { IsHidden: false } wrapper] &&
               wrapper.Transform.IsIdentity && wrapper.Block != document.Root && !IsPlacedElsewhere(document, wrapper) &&
               !document.Animations.Any(animation => animation.Targets.Contains(wrapper)))
        {
            Block block = wrapper.Block;
            document.Root.Entities.Remove(wrapper);
            while (block.Entities.Count > 0)
            {
                Entity entity = block.Entities[0];
                block.Entities.RemoveAt(0);
                document.Root.Entities.Add(entity);
            }
            document.Blocks.Remove(block);
            document.Root.Name = block.Name;
            changed = true;
        }
        return changed;
    }

    private static bool IsPlacedElsewhere(Document document, BlockReference wrapper) =>
        document.Blocks.SelectMany(block => block.Entities).OfType<BlockReference>()
            .Any(reference => reference != wrapper && reference.Block == wrapper.Block);
}

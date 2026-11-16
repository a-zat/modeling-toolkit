using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>
/// Brings a model read from a file into a document: its top-level entities become a block named after it, placed by a
/// reference at the origin of a block of the document, with the blocks, materials, textures and animations they use.
/// Undoing takes them all away again.
/// </summary>
public sealed class ImportCommand : IEditCommand
{
    private readonly Document _document;
    private readonly Block _into;
    private readonly List<Block> _blocks;
    private readonly List<Material> _materials;
    private readonly List<Texture> _textures;
    private readonly List<Animation> _animations;

    /// <param name="imported">The model read, which the command takes apart: it must not be used afterwards.</param>
    /// <param name="name">The name of the block, such as the name of the file.</param>
    /// <exception cref="ArgumentException">
    /// The model read has nothing, or <paramref name="into"/> is not a block of the document.
    /// </exception>
    public ImportCommand(Document document, Block into, Document imported, string name)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!ReferenceEquals(into.Document, document))
        {
            throw new ArgumentException("The block to import into must be one of the document.", nameof(into));
        }
        if (imported.Root.Entities.Count == 0)
        {
            throw new ArgumentException("The model has nothing to import.", nameof(imported));
        }

        _document = document;
        _into = into;

        // The entities and blocks leave the model read, which no longer owns them.
        var block = new Block(UniqueName(document, name));
        while (imported.Root.Entities.Count > 0)
        {
            Entity entity = imported.Root.Entities[0];
            imported.Root.Entities.RemoveAt(0);
            block.Entities.Add(entity);
        }
        _blocks = [.. imported.Blocks];
        while (imported.Blocks.Count > 0)
        {
            imported.Blocks.RemoveAt(imported.Blocks.Count - 1);
        }
        _blocks.Add(block);
        _materials = [.. imported.Materials.Where(material => !document.Materials.Contains(material))];
        _textures = [.. imported.Textures.Where(texture => !document.Textures.Contains(texture))];
        _animations = [.. imported.Animations];
        Reference = new BlockReference(block, Transform.Identity);
    }

    public string Name => "Import";

    /// <summary>The reference that places the model imported.</summary>
    public BlockReference Reference { get; }

    public void Apply()
    {
        _document.Textures.AddRange(_textures);
        _document.Materials.AddRange(_materials);
        _document.Blocks.AddRange(_blocks);
        _document.Animations.AddRange(_animations);
        _into.Entities.Add(Reference);
    }

    public void Revert()
    {
        _into.Entities.Remove(Reference);
        foreach (Animation animation in _animations)
        {
            _document.Animations.Remove(animation);
        }
        foreach (Block block in _blocks)
        {
            _document.Blocks.Remove(block);
        }
        foreach (Material material in _materials)
        {
            _document.Materials.Remove(material);
        }
        foreach (Texture texture in _textures)
        {
            _document.Textures.Remove(texture);
        }
    }

    // "Chair", or "Chair 2" when the document has a block named "Chair" already.
    private static string UniqueName(Document document, string name)
    {
        string unique = name;
        for (int number = 2; document.Blocks.Any(block => block.Name == unique); number++)
        {
            unique = $"{name} {number}";
        }
        return unique;
    }
}

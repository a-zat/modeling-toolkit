using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>Adds a layer to the layers of a document, after the others; undoing takes it away.</summary>
public sealed class AddLayerCommand(Document document, Layer layer) : IEditCommand
{
    private readonly Document _document = document ?? throw new ArgumentNullException(nameof(document));
    private readonly Layer _layer = layer ?? throw new ArgumentNullException(nameof(layer));

    public string Name => "New layer";

    public void Apply() => _document.AddLayer(_layer);

    public void Revert() => _document.RemoveLayer(_layer);
}

/// <summary>
/// Takes a layer away from a document, the entities on it left on none; undoing lists it again where it was, with its
/// entities.
/// </summary>
public sealed class DeleteLayerCommand : IEditCommand
{
    private readonly Document _document;
    private readonly Layer _layer;
    private readonly int _index;
    private readonly Entity[] _entities;

    public DeleteLayerCommand(Document document, Layer layer)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _index = document.Layers.ToList().IndexOf(layer);
        if (_index < 0)
        {
            throw new ArgumentException("The layer is not in the document.", nameof(layer));
        }
        _entities = [.. document.Blocks.Prepend(document.Root).SelectMany(block => block.Entities)
            .Where(entity => ReferenceEquals(entity.Layer, layer))];
    }

    public string Name => "Delete layer";

    public void Apply()
    {
        foreach (Entity entity in _entities)
        {
            entity.Layer = null;
        }
        _document.RemoveLayer(_layer);
    }

    public void Revert()
    {
        _document.AddLayer(_layer, _index);
        foreach (Entity entity in _entities)
        {
            entity.Layer = _layer;
        }
    }
}

/// <summary>Renames a layer; undoing gives it back its name.</summary>
public sealed class RenameLayerCommand(Layer layer, string name) : IEditCommand
{
    private readonly Layer _layer = layer ?? throw new ArgumentNullException(nameof(layer));
    private readonly string _name = name ?? throw new ArgumentNullException(nameof(name));
    private readonly string _before = layer.Name;

    public string Name => "Rename layer";

    public void Apply() => _layer.Name = _name;

    public void Revert() => _layer.Name = _before;
}

/// <summary>Shows or hides the entities of a layer; undoing does the other.</summary>
public sealed class SetLayerHiddenCommand(Layer layer, bool hidden) : IEditCommand
{
    private readonly Layer _layer = layer ?? throw new ArgumentNullException(nameof(layer));

    public string Name => hidden ? "Hide layer" : "Show layer";

    public void Apply() => _layer.IsHidden = hidden;

    public void Revert() => _layer.IsHidden = !hidden;
}

/// <summary>Puts entities on a layer, or on none with null, as one command; undoing puts each back on its own.</summary>
public sealed class SetLayerCommand : IEditCommand
{
    private readonly (Entity Entity, Layer? Before)[] _entities;
    private readonly Layer? _layer;

    public SetLayerCommand(IEnumerable<Entity> entities, Layer? layer)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entities = [.. entities.Select(entity => (entity, entity.Layer))];
        _layer = layer;
    }

    public string Name => "Set layer";

    public void Apply()
    {
        foreach ((Entity entity, _) in _entities)
        {
            entity.Layer = _layer;
        }
    }

    public void Revert()
    {
        foreach ((Entity entity, Layer? before) in _entities)
        {
            entity.Layer = before;
        }
    }
}

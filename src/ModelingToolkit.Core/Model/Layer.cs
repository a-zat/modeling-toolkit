namespace ModelingToolkit.Core.Model;

/// <summary>
/// A named set of entities across the structure of a model, such as the rooms of a house or its systems, shown or
/// hidden together. An entity is on one layer at most; one on none is on the layer of the nearest reference above it
/// that has one, or on none.
/// </summary>
public sealed class Layer
{
    public Layer(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    public string Name
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (field != value)
            {
                field = value;
                Document?.Raise(new PropertyChanged(this, nameof(Name)));
            }
        }
    }

    /// <summary>Whether the 3D view leaves out the entities on the layer, with all they place.</summary>
    public bool IsHidden
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                Document?.Raise(new PropertyChanged(this, nameof(IsHidden)));
            }
        }
    }

    /// <summary>The document that lists the layer; null for none.</summary>
    public Document? Document { get; internal set; }

    /// <summary>
    /// The layer an entity is on, given the path of entities from the model to it: its own, or else that of the nearest
    /// entity above it that has one; null for none.
    /// </summary>
    public static Layer? Of(IReadOnlyList<Entity> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        for (int i = path.Count - 1; i >= 0; i--)
        {
            if (path[i].Layer is { } layer)
            {
                return layer;
            }
        }
        return null;
    }
}

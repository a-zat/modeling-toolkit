namespace ModelingToolkit.Core.Model;

/// <summary>The kinds of the elements of a B-rep shape that can be referred to.</summary>
public enum ElementKind
{
    Face,
    Edge,
    Vertex,
}

/// <summary>
/// A face, an edge or a vertex of the shape of a B-rep entity, as placed in the model: the path of the entity, the
/// entities from the model to it, and the index of the element in the order of the shape. It refers to one version of
/// the shape: once the shape is replaced, the same index may be another element. Two references are equal when they
/// have the same entities, kind and index.
/// </summary>
public sealed class ElementReference : IEquatable<ElementReference>
{
    /// <exception cref="ArgumentException">The path is empty or does not end with a B-rep entity.</exception>
    public ElementReference(IReadOnlyList<Entity> path, ElementKind kind, int index)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (path.Count == 0 || path[^1] is not BrepEntity)
        {
            throw new ArgumentException("The path of an element must end with a B-rep entity.", nameof(path));
        }

        Path = [.. path];
        Kind = kind;
        Index = index;
    }

    /// <summary>The entities from the model to the B-rep entity, the last one.</summary>
    public IReadOnlyList<Entity> Path { get; }

    public BrepEntity Entity => (BrepEntity)Path[^1];

    public ElementKind Kind { get; }

    public int Index { get; }

    public bool Equals(ElementReference? other) =>
        other is not null && Kind == other.Kind && Index == other.Index && Path.Count == other.Path.Count &&
        Path.Zip(other.Path).All(pair => ReferenceEquals(pair.First, pair.Second));

    public override bool Equals(object? obj) => Equals(obj as ElementReference);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (Entity entity in Path)
        {
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entity));
        }
        hash.Add(Kind);
        hash.Add(Index);
        return hash.ToHashCode();
    }

    public override string ToString() => $"{Kind} {Index} of {Entity.Name ?? "a shape"}";
}

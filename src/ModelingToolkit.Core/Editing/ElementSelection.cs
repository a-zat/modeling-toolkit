using ModelingToolkit.Core.Kernel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>
/// The faces, edges and vertices selected in a document, in the order they were selected, each once. An element stays
/// selected while its entities are in the document along its path and its shape is the one it was selected on: the
/// changes of the document that take this away unselect it, as the same index may then be another element.
/// </summary>
public sealed class ElementSelection
{
    private readonly Document _document;
    private readonly List<(ElementReference Element, IKernelShape Shape)> _selected = [];

    /// <summary>An empty selection of the elements of a document, which follows its changes from now on.</summary>
    public ElementSelection(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        document.Changed += OnDocumentChanged;
    }

    public IReadOnlyList<ElementReference> Elements => _selected.Select(entry => entry.Element).ToArray();

    public int Count => _selected.Count;

    /// <summary>Raised after the elements selected change.</summary>
    public event EventHandler? Changed;

    public bool Contains(ElementReference element) => _selected.Exists(entry => entry.Element.Equals(element));

    /// <summary>Selects only the element.</summary>
    /// <exception cref="ArgumentException">The element is not one of the document.</exception>
    public void Select(ElementReference element)
    {
        CheckInDocument(element);
        if (_selected.Count == 1 && _selected[0].Element.Equals(element))
        {
            return;
        }

        _selected.Clear();
        _selected.Add((element, element.Entity.Shape));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds the element to the selection, or takes it out if it is already selected.</summary>
    /// <exception cref="ArgumentException">The element is not one of the document.</exception>
    public void Toggle(ElementReference element)
    {
        CheckInDocument(element);
        int index = _selected.FindIndex(entry => entry.Element.Equals(element));
        if (index >= 0)
        {
            _selected.RemoveAt(index);
        }
        else
        {
            _selected.Add((element, element.Entity.Shape));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void CheckInDocument(ElementReference element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!IsInDocument(element.Path))
        {
            throw new ArgumentException("The element is not one of the document.", nameof(element));
        }
    }

    // Each entity of the path in the block of the one before it, the first one in the model.
    private bool IsInDocument(IReadOnlyList<Entity> path)
    {
        Block block = _document.Root;
        for (int i = 0; i < path.Count; i++)
        {
            if (!ReferenceEquals(path[i].Owner, block) || block.Document != _document)
            {
                return false;
            }
            if (i < path.Count - 1)
            {
                if (path[i] is not BlockReference reference)
                {
                    return false;
                }
                block = reference.Block;
            }
        }
        return true;
    }

    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        if (change is PropertyChanged || _selected.Count == 0)
        {
            return;
        }

        int removed = _selected.RemoveAll(entry =>
            !ReferenceEquals(entry.Element.Entity.Shape, entry.Shape) || !IsInDocument(entry.Element.Path));
        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

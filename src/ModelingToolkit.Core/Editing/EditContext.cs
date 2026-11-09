using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing;

/// <summary>
/// Where the edits of a document happen: the model, or the block of a reference entered from it, possibly through
/// other references. The tools work on the entities of that block, in its coordinates, and the rest of the model is
/// only shown. A change of the document that takes a reference of the path away leaves the context back to the
/// references that remain.
/// </summary>
public sealed class EditContext
{
    private readonly Document _document;
    private readonly List<BlockReference> _path = [];

    /// <summary>The context of the model of a document, which follows its changes from now on.</summary>
    public EditContext(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _document = document;
        document.Changed += OnDocumentChanged;
    }

    /// <summary>The references entered, from the model; empty in the model.</summary>
    public IReadOnlyList<BlockReference> Path => [.. _path];

    public bool IsModel => _path.Count == 0;

    /// <summary>The block whose entities are edited: the root of the document in the model.</summary>
    public Block Block => _path.Count == 0 ? _document.Root : _path[^1].Block;

    /// <summary>The placement of the block in the model: the transforms of the references entered, composed.</summary>
    public Transform Placement => _path.Aggregate(Transform.Identity, (placement, reference) => placement * reference.Transform);

    /// <summary>Raised after the context changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Enters the block of a reference of the current block.</summary>
    /// <exception cref="ArgumentException">The reference is not an entity of <see cref="Block"/>.</exception>
    public void Enter(BlockReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!ReferenceEquals(reference.Owner, Block))
        {
            throw new ArgumentException("Only a reference of the block being edited can be entered.", nameof(reference));
        }

        _path.Add(reference);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Goes back to the block the current one was entered from; nothing in the model.</summary>
    public void Leave()
    {
        if (_path.Count > 0)
        {
            _path.RemoveAt(_path.Count - 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Goes back to the model.</summary>
    public void LeaveAll()
    {
        if (_path.Count > 0)
        {
            _path.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// What a path of entities from the model is in this context: <see langword="null"/> for a path outside it,
    /// otherwise the entities of the path from the block being edited, the first one an entity of that block.
    /// </summary>
    public IReadOnlyList<Entity>? Within(IReadOnlyList<Entity> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count <= _path.Count)
        {
            return null;
        }
        for (int i = 0; i < _path.Count; i++)
        {
            if (!ReferenceEquals(path[i], _path[i]))
            {
                return null;
            }
        }
        return path.Skip(_path.Count).ToArray();
    }

    // The references that remain, each in the block of the one before it, the first in the model.
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        if (_path.Count == 0 || change is PropertyChanged)
        {
            return;
        }

        Block block = _document.Root;
        int kept = 0;
        while (kept < _path.Count && ReferenceEquals(_path[kept].Owner, block) && block.Document == _document)
        {
            block = _path[kept].Block;
            kept++;
        }
        if (kept < _path.Count)
        {
            _path.RemoveRange(kept, _path.Count - kept);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

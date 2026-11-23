using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// The outline by layer: each layer of the model, shown or hidden by its eye, with the entities on it of their own, and
/// last the entities of the model space on none. A click on an entity selects it, as in the outline of the structure.
/// </summary>
public sealed partial class LayersViewModel : ObservableObject
{
    private Document? _document;
    private bool _stale;

    /// <summary>The layers, then the entities on none.</summary>
    public ObservableCollection<LayerRowViewModel> Rows { get; } = [];

    /// <summary>Runs the commands that add, rename, show, hide and delete layers; null when nothing can be edited.</summary>
    public Func<IEditCommand, bool>? Edit { get; set; }

    /// <summary>Selects an entity in the model, given its path.</summary>
    public Action<IReadOnlyList<Entity>>? Select { get; set; }

    /// <summary>Whether the outline by layer is the one shown: it follows the changes of the model only then.</summary>
    public bool IsShown
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && value && _stale)
            {
                Rebuild();
            }
        }
    }

    public bool IsEmpty => _document is null;

    /// <summary>The item selected, whose entity is selected in the model.</summary>
    [ObservableProperty]
    public partial object? SelectedItem { get; set; }

    partial void OnSelectedItemChanged(object? value)
    {
        if (value is LayerItemViewModel item)
        {
            Select?.Invoke(item.Path);
        }
    }

    /// <summary>Shows the layers of a document, and follows its changes; none for null.</summary>
    public void Show(Document? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }
        _document = document;
        if (document is not null)
        {
            document.Changed += OnDocumentChanged;
        }
        OnPropertyChanged(nameof(IsEmpty));
        Rebuild();
    }

    /// <summary>A name for a new layer, unlike those of the layers of a document: "Layer 1", "Layer 2"...</summary>
    public static string NewName(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int n = 1; ; n++)
        {
            string name = $"Layer {n}";
            if (!document.Layers.Any(layer => layer.Name == name))
            {
                return name;
            }
        }
    }

    /// <summary>Adds a layer after the others, its name edited at once.</summary>
    [RelayCommand]
    private void AddLayer()
    {
        if (_document is not { } document || Edit is not { } edit)
        {
            return;
        }
        var layer = new Layer(NewName(document));
        if (edit(new AddLayerCommand(document, layer)) && Rows.FirstOrDefault(row => ReferenceEquals(row.Layer, layer)) is { } added)
        {
            added.StartRename();
        }
    }

    [RelayCommand]
    private void ToggleVisible(LayerRowViewModel? row)
    {
        if (row?.Layer is { } layer)
        {
            Edit?.Invoke(new SetLayerHiddenCommand(layer, !layer.IsHidden));
        }
    }

    /// <summary>Deletes a layer, the entities on it left on none.</summary>
    [RelayCommand]
    private void Delete(LayerRowViewModel? row)
    {
        if (row?.Layer is { } layer && _document is { } document)
        {
            Edit?.Invoke(new DeleteLayerCommand(document, layer));
        }
    }

    [RelayCommand]
    private void StartRename(LayerRowViewModel? row)
    {
        if (Edit is not null)
        {
            row?.StartRename();
        }
    }

    /// <summary>Keeps the name typed for a layer: an empty one, or the same, changes nothing.</summary>
    [RelayCommand]
    private void CommitRename(LayerRowViewModel? row)
    {
        if (row is not { IsRenaming: true, Layer: { } layer })
        {
            return;
        }
        row.IsRenaming = false;
        string name = row.EditedName.Trim();
        if (name.Length > 0 && name != layer.Name)
        {
            Edit?.Invoke(new RenameLayerCommand(layer, name));
        }
    }

    // What the outline by layer shows changes with the layers, the entities on them, and their names.
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        bool relevant = change switch
        {
            LayersChanged or EntityAdded or EntityRemoved or EntityReplaced => true,
            PropertyChanged { Target: Layer } => true,
            PropertyChanged { Property: nameof(Entity.Layer) or nameof(Entity.Name) or nameof(Entity.IsHidden) } => true,
            _ => false,
        };
        if (!relevant)
        {
            return;
        }
        if (IsShown)
        {
            Rebuild();
        }
        else
        {
            _stale = true;
        }
    }

    private void Rebuild()
    {
        _stale = false;
        Dictionary<Layer, LayerRowViewModel> before = Rows.Where(row => row.Layer is not null)
            .ToDictionary(row => row.Layer!, ReferenceEqualityComparer.Instance as IEqualityComparer<Layer>);
        bool noneExpanded = Rows.FirstOrDefault(row => row.Layer is null)?.IsExpanded ?? false;
        Rows.Clear();
        if (_document is not { } document)
        {
            return;
        }

        var items = new Dictionary<Layer, List<LayerItemViewModel>>(ReferenceEqualityComparer.Instance);
        var path = new List<Entity>();
        void Visit(Block block, int depth)
        {
            foreach (Entity entity in block.Entities)
            {
                path.Add(entity);
                if (entity.Layer is { } layer && layer.Document == document)
                {
                    if (!items.TryGetValue(layer, out List<LayerItemViewModel>? list))
                    {
                        items[layer] = list = [];
                    }
                    list.Add(Item(path));
                }
                if (entity is BlockReference reference && depth < 64)
                {
                    Visit(reference.Block, depth + 1);
                }
                path.RemoveAt(path.Count - 1);
            }
        }
        Visit(document.Root, 0);

        foreach (Layer layer in document.Layers)
        {
            LayerRowViewModel row = new(layer, items.GetValueOrDefault(layer) ?? [])
            {
                IsExpanded = before.GetValueOrDefault(layer)?.IsExpanded ?? false,
            };
            Rows.Add(row);
        }
        List<LayerItemViewModel> none = [.. document.Root.Entities.Where(entity => entity.Layer is null)
            .Select(entity => Item([entity]))];
        Rows.Add(new LayerRowViewModel(null, none) { IsExpanded = noneExpanded });
    }

    private static LayerItemViewModel Item(List<Entity> path)
    {
        (string name, _) = ModelNodeViewModel.Label(path[^1]);
        string? where = path.Count > 1
            ? string.Join(" › ", path.Take(path.Count - 1).Select(entity => ModelNodeViewModel.Label(entity).Name))
            : null;
        return new LayerItemViewModel(name, where, [.. path], !path.Any(entity => entity.IsHidden));
    }
}

/// <summary>A layer in the outline by layer, or the entities on none, with what is on it.</summary>
public sealed partial class LayerRowViewModel : ObservableObject
{
    internal LayerRowViewModel(Layer? layer, IReadOnlyList<LayerItemViewModel> items)
    {
        Layer = layer;
        Items = items;
    }

    /// <summary>The layer; null for the entities on none.</summary>
    public Layer? Layer { get; }

    public string Name => Layer?.Name ?? "No layer";

    /// <summary>How many entities are on it, after its name.</summary>
    public string Detail => Formatting.Count(Items.Count);

    public IReadOnlyList<LayerItemViewModel> Items { get; }

    public bool IsLayer => Layer is not null;

    public bool IsVisible => Layer is not { IsHidden: true };

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    [ObservableProperty]
    public partial string EditedName { get; set; } = string.Empty;

    internal void StartRename()
    {
        if (Layer is not null)
        {
            EditedName = Layer.Name;
            IsRenaming = true;
        }
    }
}

/// <summary>An entity on a layer, by its name and the components it is in.</summary>
/// <param name="Name">Its name, as the outline shows it.</param>
/// <param name="Where">The components above it, "Living room › Table"; null for an entity of the model space.</param>
/// <param name="Path">The entities from the model to it.</param>
/// <param name="IsVisible">Whether it and all above it are shown, its layer aside.</param>
public sealed record LayerItemViewModel(string Name, string? Where, IReadOnlyList<Entity> Path, bool IsVisible)
{
    /// <summary>The components above it, after its name, with the space between them; empty for none.</summary>
    public string WhereSpaced => Where is null ? string.Empty : "  " + Where;
}

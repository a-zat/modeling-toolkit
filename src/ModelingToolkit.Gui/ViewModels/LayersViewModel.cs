using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A layer: an entity of the model space, shown or hidden with all it places.</summary>
public sealed partial class LayerViewModel : ObservableObject
{
    private readonly LayersViewModel _owner;
    private bool _refreshing;

    internal LayerViewModel(Entity entity, LayersViewModel owner)
    {
        Entity = entity;
        _owner = owner;
        Refresh(); // without a command
    }

    public Entity Entity { get; }

    public string Name => ModelNodeViewModel.Label(Entity).Name;

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    internal void Refresh()
    {
        _refreshing = true;
        IsVisible = !Entity.IsHidden;
        _refreshing = false;
        OnPropertyChanged(nameof(Name));
    }

    // Shown or hidden by a command, which can be undone and is saved with the document.
    partial void OnIsVisibleChanged(bool value)
    {
        if (!_refreshing && !_owner.SetHidden([Entity], !value))
        {
            Refresh();
        }
    }
}

/// <summary>
/// The layers of the open document: the entities of its model space, such as the groups under the root node of a glTF
/// file, each shown or hidden in the 3D view.
/// </summary>
public sealed partial class LayersViewModel : ObservableObject
{
    private Document? _document;

    public ObservableCollection<LayerViewModel> Items { get; } = [];

    /// <summary>Runs the commands that show or hide the layers; null when nothing can be edited.</summary>
    public Func<IEditCommand, bool>? Edit { get; set; }

    [ObservableProperty]
    public partial bool HasItems { get; private set; }

    public void Show(Document? document)
    {
        _document = document;
        Items.Clear();
        Refresh();
    }

    /// <summary>Follows the entities added to, removed from or hidden in the model space.</summary>
    public void Refresh()
    {
        IReadOnlyList<Entity> entities = _document is null ? [] : [.. _document.Root.Entities];
        if (!Items.Select(item => item.Entity).SequenceEqual(entities))
        {
            Items.Clear();
            foreach (Entity entity in entities)
            {
                Items.Add(new LayerViewModel(entity, this));
            }
        }
        foreach (LayerViewModel item in Items)
        {
            item.Refresh();
        }
        HasItems = Items.Count > 0;
    }

    /// <summary>Shows or hides all the layers.</summary>
    public void SetAllVisible(bool visible)
    {
        SetHidden([.. Items.Select(item => item.Entity)], !visible);
        Refresh();
    }

    internal bool SetHidden(IReadOnlyList<Entity> entities, bool hidden) =>
        Edit?.Invoke(new SetHiddenCommand(entities, hidden)) == true;
}

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Editing;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// An anchor or a point of view of the component selected, in its properties: its name, its place in the component and
/// the way it faces, each edited by a command; removed, or looked from.
/// </summary>
public sealed partial class AnchorRowViewModel : ObservableObject
{
    private readonly Func<IEditCommand, bool> _edit;
    private readonly Action<AnchorRowViewModel>? _look;

    internal AnchorRowViewModel(AnchorEntity anchor, IReadOnlyList<Entity> path, Func<IEditCommand, bool> edit,
        Action<AnchorRowViewModel>? look)
    {
        Anchor = anchor;
        Path = path;
        _edit = edit;
        _look = look;
    }

    public AnchorEntity Anchor { get; }

    /// <summary>The path of the component holding it, from the model.</summary>
    public IReadOnlyList<Entity> Path { get; }

    public bool IsPointOfView => Anchor.Kind == AnchorKind.PointOfView;

    /// <summary>What it is, as the tooltip of its icon says.</summary>
    public string KindName => IsPointOfView ? "Point of view" : "Anchor";

    /// <summary>The name, as typed: setting it renames the anchor; empty gives it back its kind as a name.</summary>
    public string Name
    {
        get => Anchor.Name ?? KindName;
        set
        {
            string name = string.IsNullOrWhiteSpace(value) ? KindName : value.Trim();
            if (name != Anchor.Name)
            {
                _edit(new RenameCommand(Anchor, name));
            }
            OnPropertyChanged();
        }
    }

    /// <summary>Where it is in the component, in millimetres, along its axes.</summary>
    public string X
    {
        get => Length(Anchor.Point.X);
        set => Place(value, 0);
    }

    public string Y
    {
        get => Length(Anchor.Point.Y);
        set => Place(value, 1);
    }

    public string Z
    {
        get => Length(Anchor.Point.Z);
        set => Place(value, 2);
    }

    /// <summary>The way it faces, in degrees from the X axis of the component around its Z axis.</summary>
    public string Facing
    {
        get
        {
            Vector3d ahead = Anchor.Placement.ApplyVector(Vector3d.UnitX);
            double degrees = Math.Round(Math.Atan2(ahead.Y, ahead.X) * 180 / Math.PI, 1) + 0.0;
            return degrees.ToString("0.#", CultureInfo.InvariantCulture);
        }
        set
        {
            // A field left as it shows changes nothing: the value shown is rounded.
            if (value?.Trim() != Facing && Parse(value) is { } degrees)
            {
                Point3d at = Anchor.Point;
                Transform placement = Transform.Translation(at.X, at.Y, at.Z) *
                    Transform.Rotation(Point3d.Origin, Vector3d.UnitZ, degrees * Math.PI / 180);
                _edit(new SetAnchorCommand(IsPointOfView ? "Turn point of view" : "Turn anchor", Anchor, placement));
            }
            Refresh();
        }
    }

    /// <summary>Shows the values of the anchor again, as after an edit or its undoing.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Z));
        OnPropertyChanged(nameof(Facing));
    }

    [RelayCommand]
    private void Remove() => _edit(new DeleteCommand(Anchor));

    /// <summary>Looks from the point of view, walking, as a click on its component does.</summary>
    [RelayCommand]
    private void Look() => _look?.Invoke(this);

    public bool CanLook => IsPointOfView && _look is not null;

    // The place moved along one axis, the way it faces kept.
    private void Place(string text, int axis)
    {
        string shown = axis switch { 0 => X, 1 => Y, _ => Z };
        if (text?.Trim() != shown && Parse(text) is { } value)
        {
            Point3d at = Anchor.Point;
            Vector3d by = axis switch
            {
                0 => new Vector3d(value - at.X, 0, 0),
                1 => new Vector3d(0, value - at.Y, 0),
                _ => new Vector3d(0, 0, value - at.Z),
            };
            if (by.Length > 1e-9)
            {
                _edit(new SetAnchorCommand(IsPointOfView ? "Move point of view" : "Move anchor", Anchor,
                    Transform.Translation(by.X, by.Y, by.Z) * Anchor.Placement));
            }
        }
        Refresh();
    }

    private static string Length(double value) =>
        (Math.Round(value, 1) + 0.0).ToString("0.#", CultureInfo.InvariantCulture);

    // A number as typed, with a point or a comma before the decimals.
    private static double? Parse(string? text) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
        double.IsFinite(value) ? value : null;
}

/// <summary>An anchor that the component selected can follow, as the list of the attachment offers it.</summary>
/// <param name="Label">The anchor and the component holding it.</param>
/// <param name="Attachment">The attachment to make, from where the component is; null for following nothing.</param>
public sealed record AttachChoice(string Label, Attachment? Attachment)
{
    /// <summary>Makes the component follow this anchor, or nothing; set by the properties that offer it.</summary>
    public System.Windows.Input.ICommand? Choose { get; init; }

    /// <summary>Whether it is what the component follows now.</summary>
    public bool IsChosen { get; init; }

    public override string ToString() => Label;
}

/// <summary>A layer that the selection can be put on, as the list of the layer offers it.</summary>
/// <param name="Label">The name of the layer, "None", or "New layer".</param>
/// <param name="Layer">The layer; null for none, and for a new one.</param>
public sealed record LayerChoice(string Label, Layer? Layer)
{
    /// <summary>Puts the selection on this layer; set by the properties that offer it.</summary>
    public System.Windows.Input.ICommand? Choose { get; init; }

    /// <summary>Whether it is the layer of the selection now.</summary>
    public bool IsChosen { get; init; }

    /// <summary>Whether it makes a new layer, which the selection goes on.</summary>
    public bool IsNew { get; init; }

    public override string ToString() => Label;
}

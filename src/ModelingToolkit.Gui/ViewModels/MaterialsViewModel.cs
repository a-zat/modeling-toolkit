using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A material of the materials panel.</summary>
/// <param name="BaseColor">Linear RGB, as in the model; the view converts it for the screen.</param>
public sealed record MaterialItem(string Name, Color BaseColor, string Description);

/// <summary>The materials of the document.</summary>
public sealed partial class MaterialsViewModel : ObservableObject
{
    public MaterialsViewModel() => Items = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<MaterialItem> Items { get; private set; }

    public bool IsEmpty => Items.Count == 0;

    public void Show(Document? document) => Items = document is null ? [] : [.. document.Materials.Select(ToItem)];

    // Qualified: the Material.Avalonia theme of the prototype (#10) brings a Material namespace.
    private static MaterialItem ToItem(Core.Model.Material material)
    {
        string texture = material.BaseColorTexture is null ? string.Empty : " · texture";
        return new(
            material.Name,
            material.BaseColor,
            string.Create(CultureInfo.InvariantCulture,
                $"metallic {material.Metallic:0.##} · roughness {material.Roughness:0.##}{texture}"));
    }
}

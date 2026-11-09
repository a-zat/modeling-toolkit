using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModelingToolkit.Core.Model;
using MaterialColor = ModelingToolkit.Core.Model.Color;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>A material to choose, as the picker shows it: a swatch of its colour and its name.</summary>
/// <param name="Material">One of the document; null for the default material, or for one of the library.</param>
/// <param name="Library">One of the library, made into a material of the document when chosen.</param>
public sealed record MaterialSwatch(string Name, IBrush Brush, Material? Material, LibraryMaterial? Library)
{
    /// <summary>The default material, which takes the paint away.</summary>
    public bool IsDefault => Material is null && Library is null;

    internal static MaterialSwatch Of(Material? material) =>
        new(material?.Name ?? "Default", BrushOf(material), material, null);

    internal static IBrush BrushOf(Material? material) => material is null
        ? new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x6C, 0x73, 0x80))
        : new Avalonia.Media.Immutable.ImmutableSolidColorBrush(ColorSpaces.ToSrgb(material.BaseColor));
}

/// <summary>
/// The choice of a material for the surfaces selected: one of the document, one of the library, a custom colour, or the
/// default material. The material chosen goes to <see cref="Chosen"/>, possibly a new one, not yet in the document.
/// </summary>
public sealed partial class MaterialPickerViewModel : ObservableObject
{
    private Document? _document;

    public MaterialPickerViewModel()
    {
        SelectedCategory = MaterialLibrary.Categories[0];
        CustomColor = Avalonia.Media.Color.FromRgb(0xB8, 0x35, 0x2D);
        Roughness = 0.6;
    }

    /// <summary>Receives the material chosen, null for the default one; the picker closes.</summary>
    public Action<Material?>? Chosen { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The materials of the document, after the default one.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MaterialSwatch> ModelItems { get; private set; } = [];

    public IReadOnlyList<string> Categories { get; } = MaterialLibrary.Categories;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryItems))]
    public partial string SelectedCategory { get; set; }

    public IReadOnlyList<MaterialSwatch> LibraryItems => [.. MaterialLibrary.In(SelectedCategory).Select(material =>
        new MaterialSwatch(material.Name, MaterialSwatch.BrushOf(material.Create()), null, material))];

    /// <summary>The colour of a custom material, as it is seen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CustomHex), nameof(CustomBrush))]
    public partial Avalonia.Media.Color CustomColor { get; set; }

    /// <summary>The custom colour as "#RRGGBB"; a text that is not a colour is ignored.</summary>
    public string CustomHex
    {
        get => $"#{CustomColor.R:X2}{CustomColor.G:X2}{CustomColor.B:X2}";
        set
        {
            if (Avalonia.Media.Color.TryParse(value?.Trim(), out Avalonia.Media.Color color))
            {
                CustomColor = Avalonia.Media.Color.FromRgb(color.R, color.G, color.B);
            }
            OnPropertyChanged();
        }
    }

    public IBrush CustomBrush => new Avalonia.Media.Immutable.ImmutableSolidColorBrush(CustomColor);

    /// <summary>From 0, smooth, to 1, rough.</summary>
    [ObservableProperty]
    public partial double Roughness { get; set; }

    /// <summary>From 0, dielectric, to 1, metal.</summary>
    [ObservableProperty]
    public partial double Metallic { get; set; }

    /// <summary>Asks for an image file, for a material with a texture; null for no file chosen.</summary>
    public Func<Task<string?>>? ChooseImage { get; set; }

    /// <summary>The millimetres that the texture of a new material covers once on the faces of shapes.</summary>
    [ObservableProperty]
    public partial decimal? TextureSize { get; set; } = 1000;

    /// <summary>
    /// Chooses a new material with the image of a file as its texture, of the size, roughness and metallic of the picker;
    /// a file that cannot be read, or is not a PNG, JPEG or WebP image, chooses nothing.
    /// </summary>
    [RelayCommand]
    private async Task PickTextureAsync()
    {
        IsOpen = false; // the dialog takes the focus, which would close the picker anyway
        if (ChooseImage is null || await ChooseImage() is not { } path)
        {
            return;
        }

        string? mediaType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => null,
        };
        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (mediaType is null)
        {
            return;
        }

        string name = Path.GetFileNameWithoutExtension(path);
        Choose(Named(new Material(name)
        {
            BaseColorTexture = new Texture(name, mediaType, data),
            TextureSize = (float)Math.Max(TextureSize ?? 1000, 1),
            Roughness = (float)Math.Round(Roughness, 2),
            Metallic = (float)Math.Round(Metallic, 2),
        }));
    }

    /// <summary>Opens the picker on the materials of a document.</summary>
    public void Open(Document? document)
    {
        _document = document;
        ModelItems = [MaterialSwatch.Of(null), .. (document?.Materials ?? []).Select(MaterialSwatch.Of)];
        IsOpen = true;
    }

    [RelayCommand]
    private void ShowCategory(string category) => SelectedCategory = category;

    /// <summary>
    /// Chooses a swatch: a material of the document as it is; one of the library as a material of the document with
    /// its values, if any, or else a new one.
    /// </summary>
    [RelayCommand]
    private void Pick(MaterialSwatch swatch)
    {
        Material? material = swatch.Library is { } library
            ? _document?.Materials.FirstOrDefault(library.Matches) ?? Named(library.Create())
            : swatch.Material;
        Choose(material);
    }

    // A new material, renamed "Blue 2" when the document has another one named "Blue".
    private Material Named(Material material)
    {
        string name = material.Name;
        for (int number = 2; _document?.Materials.Any(existing => existing.Name == material.Name) == true; number++)
        {
            material.Name = $"{name} {number}";
        }
        return material;
    }

    /// <summary>Chooses a material of the custom colour, roughness and metallic, reusing an identical one of the document.</summary>
    [RelayCommand]
    private void PickCustom()
    {
        string name = $"Custom {CustomColor.R:X2}{CustomColor.G:X2}{CustomColor.B:X2}";
        MaterialColor color = ColorSpaces.ToLinear(CustomColor.R, CustomColor.G, CustomColor.B);
        float roughness = (float)Math.Round(Roughness, 2), metallic = (float)Math.Round(Metallic, 2);
        Material material = _document?.Materials.FirstOrDefault(existing =>
                existing.Name == name && existing.BaseColor == color && existing.Roughness == roughness &&
                existing.Metallic == metallic && existing.BaseColorTexture is null)
            ?? Named(new Material(name) { BaseColor = color, Roughness = roughness, Metallic = metallic });
        Choose(material);
    }

    private void Choose(Material? material)
    {
        IsOpen = false;
        Chosen?.Invoke(material);
    }
}

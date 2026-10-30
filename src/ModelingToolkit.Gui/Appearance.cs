using Avalonia;
using Avalonia.Styling;
using ModelingToolkit.Gui.ViewModels;

namespace ModelingToolkit.Gui;

/// <summary>Switches the application between the dark and the light variant of the theme.</summary>
internal sealed class Appearance : IAppearance
{
    public void Apply(bool dark)
    {
        Application application = Application.Current ?? throw new InvalidOperationException("Avalonia is not initialized.");
        application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}

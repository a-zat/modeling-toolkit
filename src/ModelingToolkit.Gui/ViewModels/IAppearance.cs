namespace ModelingToolkit.Gui.ViewModels;

/// <summary>Applies the look of the application.</summary>
public interface IAppearance
{
    /// <param name="dark">Dark or light variant of the theme.</param>
    void Apply(bool dark);
}

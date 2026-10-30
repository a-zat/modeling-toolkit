namespace ModelingToolkit.App.ViewModels;

/// <summary>The themes compared by the prototype (#10).</summary>
public enum ThemeKind
{
    Fluent,
    Simple,
    Semi,
    Material,
    FluentAvalonia,
}

/// <summary>Applies the look of the application.</summary>
public interface IAppearance
{
    /// <param name="theme">The theme of the controls.</param>
    /// <param name="dark">Dark or light variant of the theme.</param>
    /// <param name="compact">Compact density, for the themes that have one.</param>
    void Apply(ThemeKind theme, bool dark, bool compact);
}

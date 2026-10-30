using System.Globalization;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Themes.Simple;
using FluentAvalonia.Styling;
using Material.Colors;
using Material.Styles.Themes;
using Material.Styles.Themes.Base;
using ModelingToolkit.Gui.ViewModels;
using Semi.Avalonia;

namespace ModelingToolkit.Gui;

/// <summary>Applies the theme of the application; the prototype (#10) replaces it at run time to compare themes.</summary>
internal sealed class Appearance : IAppearance
{
    private ThemeKind? _theme;
    private bool _dark;
    private bool _compact;
    private IStyle? _style;

    /// <summary>Raised when the theme is replaced: windows are created again to use its control templates.</summary>
    public event EventHandler? ThemeReplaced;

    public void Apply(ThemeKind theme, bool dark, bool compact)
    {
        Application application = Application.Current ?? throw new InvalidOperationException("Avalonia is not initialized.");
        application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        // Material has its own light and dark modes, and changes them in the background: it is created again instead.
        bool sameVariant = dark == _dark || theme != ThemeKind.Material;
        if (theme == _theme && compact == _compact && sameVariant)
        {
            return;
        }

        // The theme is the first of the application styles, before our own.
        bool replacing = _style is not null;
        _style = Create(theme, dark, compact);
        if (replacing)
        {
            application.Styles[0] = _style;
        }
        else
        {
            application.Styles.Insert(0, _style);
        }

        _theme = theme;
        _dark = dark;
        _compact = compact;
        if (replacing)
        {
            ThemeReplaced?.Invoke(this, EventArgs.Empty);
        }
    }

    private static IStyle Create(ThemeKind theme, bool dark, bool compact) => theme switch
    {
        ThemeKind.Fluent => new FluentTheme { DensityStyle = compact ? DensityStyle.Compact : DensityStyle.Normal },
        ThemeKind.Simple => new SimpleTheme(),
        ThemeKind.Semi => new SemiTheme { Locale = CultureInfo.GetCultureInfo("en-US") },
        // The service provider is for XAML loading; the base class of MaterialTheme accepts null, as FluentTheme does.
        ThemeKind.Material => new MaterialTheme(null!)
        {
            BaseTheme = MaterialBaseTheme(dark),
            PrimaryColor = PrimaryColor.Blue,
            SecondaryColor = SecondaryColor.Teal,
        },
        ThemeKind.FluentAvalonia => new FluentAvaloniaTheme { PreferSystemTheme = false, PreferUserAccentColor = false },
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    // Following the variant of the application ("Inherit") throws when Material replaces another theme, in
    // Material.Avalonia 3.21: the mode is given explicitly.
    private static BaseThemeMode MaterialBaseTheme(bool dark) => dark ? BaseThemeMode.Dark : BaseThemeMode.Light;
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace ModelingToolkit.Gui.Views;

/// <summary>The resource of the application with a key, such as an icon named by a view model.</summary>
public sealed class ResourceConverter : IValueConverter
{
    public static ResourceConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key, out object? resource) == true ? resource : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

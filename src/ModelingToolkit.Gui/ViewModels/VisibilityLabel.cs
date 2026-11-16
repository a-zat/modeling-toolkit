using System.Globalization;
using Avalonia.Data.Converters;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>For the nodes of the outline: the command that changes whether they are shown, and how faint they are.</summary>
public sealed class VisibilityLabel : IValueConverter
{
    /// <summary>"Hide" for what is shown, "Show" for what is hidden.</summary>
    public static VisibilityLabel Instance { get; } = new(visible => visible ? "Hide" : "Show");

    /// <summary>Full for what is shown, faint for what is hidden.</summary>
    public static VisibilityLabel Opacity { get; } = new(visible => visible ? 1.0 : 0.45);

    private readonly Func<bool, object> _convert;

    private VisibilityLabel(Func<bool, object> convert) => _convert = convert;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        _convert(value is true);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>Numbers and lengths as the user interface shows them; lengths are in millimetres.</summary>
internal static class Formatting
{
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // Adding zero turns a negative zero, which a tiny negative number rounds to, into zero.
    public static string Length(double value) =>
        (Math.Round(value, 1) + 0.0).ToString("N1", CultureInfo.InvariantCulture);

    public static string Volume(double cubicMillimetres) => cubicMillimetres switch
    {
        >= 1e9 => $"{(cubicMillimetres / 1e9).ToString("N2", CultureInfo.InvariantCulture)} m³",
        >= 1e3 => $"{(cubicMillimetres / 1e3).ToString("N2", CultureInfo.InvariantCulture)} cm³",
        _ => $"{cubicMillimetres.ToString("N2", CultureInfo.InvariantCulture)} mm³",
    };

    public static string Point(double x, double y, double z) => $"({Length(x)}, {Length(y)}, {Length(z)}) mm";

    public static string Size(BoundingBox box) => box.IsEmpty
        ? "empty"
        : $"{Length(box.Max.X - box.Min.X)} × {Length(box.Max.Y - box.Min.Y)} × {Length(box.Max.Z - box.Min.Z)} mm";
}

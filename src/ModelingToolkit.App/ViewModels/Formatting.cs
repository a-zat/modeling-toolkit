using System.Globalization;
using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.App.ViewModels;

/// <summary>Numbers and lengths as the user interface shows them; lengths are in millimetres.</summary>
internal static class Formatting
{
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Length(double value) => value.ToString("N1", CultureInfo.InvariantCulture);

    public static string Point(double x, double y, double z) => $"({Length(x)}, {Length(y)}, {Length(z)}) mm";

    public static string Size(BoundingBox box) => box.IsEmpty
        ? "empty"
        : $"{Length(box.Max.X - box.Min.X)} × {Length(box.Max.Y - box.Min.Y)} × {Length(box.Max.Z - box.Min.Z)} mm";
}

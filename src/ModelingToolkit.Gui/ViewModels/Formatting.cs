using System.Globalization;
using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>Numbers and lengths as the user interface shows them; lengths are in millimetres.</summary>
internal static class Formatting
{
    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A count and what it counts, such as "1 mesh" or "3 meshes".</summary>
    public static string Count(long value, string singular, string plural) =>
        $"{Count(value)} {(value == 1 ? singular : plural)}";

    // Adding zero turns a negative zero, which a tiny negative number rounds to, into zero.
    public static string Length(double value) =>
        (Math.Round(value, 1) + 0.0).ToString("N1", CultureInfo.InvariantCulture);

    public static string Volume(double cubicMillimetres) => cubicMillimetres switch
    {
        >= 1e9 => $"{(cubicMillimetres / 1e9).ToString("N2", CultureInfo.InvariantCulture)} m³",
        >= 1e3 => $"{(cubicMillimetres / 1e3).ToString("N2", CultureInfo.InvariantCulture)} cm³",
        _ => $"{cubicMillimetres.ToString("N2", CultureInfo.InvariantCulture)} mm³",
    };

    public static string Area(double squareMillimetres) =>
        $"{(Math.Round(squareMillimetres, 1) + 0.0).ToString("N1", CultureInfo.InvariantCulture)} mm²";

    /// <summary>A direction of unit length, as its components: "(0.000, 0.000, 1.000)".</summary>
    public static string Direction(Vector3d direction) =>
        $"({Component(direction.X)}, {Component(direction.Y)}, {Component(direction.Z)})";

    /// <summary>
    /// A plane as its equation, the first coefficient positive: "z = 100.0 mm", "0.707x + 0.707y = 70.7 mm".
    /// </summary>
    public static string Plane(Plane plane)
    {
        double[] normal = [plane.Normal.X, plane.Normal.Y, plane.Normal.Z];
        double[] rounded = normal.Select(value => Math.Round(value, 3)).ToArray();
        double sign = rounded.First(value => value != 0) < 0 ? -1 : 1;
        List<string> terms = [];
        for (int axis = 0; axis < 3; axis++)
        {
            double coefficient = sign * rounded[axis] + 0.0;
            if (coefficient == 0)
            {
                continue;
            }

            string variable = "xyz"[axis].ToString();
            string magnitude = Math.Abs(coefficient) == 1
                ? variable
                : Math.Abs(coefficient).ToString("0.###", CultureInfo.InvariantCulture) + variable;
            terms.Add(terms.Count == 0
                ? (coefficient < 0 ? "-" : "") + magnitude
                : (coefficient < 0 ? " - " : " + ") + magnitude);
        }
        return $"{string.Concat(terms)} = {Length(sign * plane.Offset)} mm";
    }

    private static string Component(double value) =>
        (Math.Round(value, 3) + 0.0).ToString("0.000", CultureInfo.InvariantCulture);

    public static string Point(double x, double y, double z) => $"({Length(x)}, {Length(y)}, {Length(z)}) mm";

    public static string Size(BoundingBox box) => box.IsEmpty
        ? "empty"
        : $"{Length(box.Max.X - box.Min.X)} × {Length(box.Max.Y - box.Min.Y)} × {Length(box.Max.Z - box.Min.Z)} mm";
}

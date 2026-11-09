using System.Globalization;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>Lengths as the measurement field shows them and takes them typed.</summary>
public static class Measurements
{
    private static readonly (string Unit, double Millimetres)[] Units =
        [("mm", 1), ("cm", 10), ("m", 1000), ("in", 25.4), ("\"", 25.4), ("ft", 304.8), ("'", 304.8)];

    /// <summary>A length in millimetres as the field shows it: "1250 mm", "12.5 mm".</summary>
    public static string Format(double millimetres) =>
        $"{Math.Round(millimetres, 1).ToString("0.#", CultureInfo.InvariantCulture)} mm";

    /// <summary>Two lengths as the field shows them: "1200 × 800 mm".</summary>
    public static string Format(double first, double second) =>
        $"{Math.Round(first, 1).ToString("0.#", CultureInfo.InvariantCulture)} × {Format(second)}";

    /// <summary>
    /// Two lengths typed, as for the sides of a rectangle: separated by a comma, a semicolon, "x" or "×", each as
    /// <see cref="TryParseLength"/> takes it, with a point as the decimal separator.
    /// </summary>
    public static bool TryParseLengths(string? text, out double first, out double second)
    {
        first = second = 0;
        string[] parts = (text ?? string.Empty).Split([',', ';', 'x', 'X', '×'], StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TryParseLength(parts[0], out first) && TryParseLength(parts[1], out second);
    }

    /// <summary>
    /// A length typed: a number in millimetres, or with a unit (mm, cm, m, in or ", ft or '); the decimal separator is a
    /// point or a comma. False for anything else, and for lengths that are not positive.
    /// </summary>
    public static bool TryParseLength(string? text, out double millimetres)
    {
        millimetres = 0;
        string value = (text ?? string.Empty).Trim().ToLowerInvariant().Replace(',', '.');
        double factor = 1;
        foreach ((string unit, double size) in Units)
        {
            if (value.EndsWith(unit, StringComparison.Ordinal))
            {
                value = value[..^unit.Length].TrimEnd();
                factor = size;
                break;
            }
        }
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
            !double.IsFinite(number) || number <= 0)
        {
            return false;
        }
        millimetres = number * factor;
        return true;
    }
}

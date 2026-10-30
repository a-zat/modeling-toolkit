using Avalonia.Data.Converters;
using Avalonia.Media;
using CoreColor = ModelingToolkit.Core.Model.Color;

namespace ModelingToolkit.App.Views;

/// <summary>Conversions of the colours of the model for the screen.</summary>
public static class ColorConverters
{
    /// <summary>Brush of a material colour: the model stores linear RGB, the screen expects sRGB.</summary>
    public static FuncValueConverter<CoreColor, IBrush> ToBrush { get; } = new(color => new SolidColorBrush(
        Color.FromArgb(ToByte(color.A), ToByte(ToSrgb(color.R)), ToByte(ToSrgb(color.G)), ToByte(ToSrgb(color.B)))));

    private static float ToSrgb(float linear) =>
        linear <= 0.0031308f ? 12.92f * linear : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;

    private static byte ToByte(float value) => (byte)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f);
}

namespace ModelingToolkit.Core.Model;

/// <summary>Linear RGB colour with opacity; every component is between 0 and 1, and alpha 1 is opaque.</summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static Color White { get; } = new(1f, 1f, 1f);
}

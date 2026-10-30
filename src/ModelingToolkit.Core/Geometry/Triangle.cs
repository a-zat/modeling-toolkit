namespace ModelingToolkit.Core.Geometry;

/// <summary>Indices of the three vertices of a triangle, counter-clockwise when the triangle is seen from its front.</summary>
public readonly record struct Triangle(int A, int B, int C);

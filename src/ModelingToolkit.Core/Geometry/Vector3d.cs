namespace ModelingToolkit.Core.Geometry;

/// <summary>A direction or a displacement in 3D space; components in millimetres, or none for a unit direction.</summary>
public readonly record struct Vector3d(double X, double Y, double Z);

namespace ModelingToolkit.Core.Geometry;

/// <summary>A plane: the points p with Normal · p = Offset.</summary>
/// <param name="Normal">Of unit length; for the plane of a face, out of the solid the face bounds.</param>
/// <param name="Offset">The signed distance of the plane from the origin, along the normal, in millimetres.</param>
public readonly record struct Plane(Vector3d Normal, double Offset);

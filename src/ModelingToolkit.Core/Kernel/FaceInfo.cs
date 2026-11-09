using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Kernel;

/// <summary>The measures of a face of a shape, in the coordinates of the shape.</summary>
/// <param name="Area">In square millimetres.</param>
/// <param name="Plane">The plane the face lies on; null for a curved face.</param>
public sealed record FaceInfo(double Area, Plane? Plane);

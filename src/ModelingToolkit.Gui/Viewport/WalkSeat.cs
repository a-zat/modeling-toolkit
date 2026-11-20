using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>A point of view taken in the walk mode, as sitting on a chair: where the eyes are and where they look.</summary>
/// <param name="Eye">In the model space.</param>
/// <param name="Yaw">Around the vertical, in radians, counterclockwise from the X axis.</param>
public sealed record WalkSeat(Point3d Eye, double Yaw);

/// <summary>Something that can be used in the walk mode, where the view marks it with a dot when it can be seen.</summary>
/// <param name="Point">Where the dot is, in the model space.</param>
/// <param name="Size">How deep in the part the dot may be, in millimetres: what is hit nearer than that hides it.</param>
public sealed record WalkBeacon(Point3d Point, double Size);

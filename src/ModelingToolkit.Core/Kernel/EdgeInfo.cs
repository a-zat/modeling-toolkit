using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// The geometry of an edge of a shape, in the coordinates of the shape, as a tool infers points from it. The start and
/// the end follow the orientation of the edge, and the tangents, of unit length, go that way too.
/// </summary>
/// <param name="IsClosed">Whether its ends are the same point, as for a circle.</param>
/// <param name="Length">In millimetres.</param>
/// <param name="Middle">The point at half its length.</param>
/// <param name="Circle">For a circle or an arc; null for other curves.</param>
public sealed record EdgeInfo(
    CurveKind Kind,
    bool IsClosed,
    double Length,
    Point3d Start,
    Point3d End,
    Point3d Middle,
    Vector3d StartTangent,
    Vector3d EndTangent,
    CircleInfo? Circle);

/// <summary>
/// The result of an edit of a shape by the kernel: the new shape, owned by the caller, and what became of the faces of
/// the old one, so that the faces keep their materials.
/// </summary>
/// <param name="FaceOrigins">
/// For each face of the new shape, in order, the index of the face of the old shape it comes from, such as each part of
/// a face that was split; -1 for a face that the edit made.
/// </param>
public sealed record ShapeEdit(IKernelShape Shape, IReadOnlyList<int> FaceOrigins);

using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// Exact-geometry operations (B-rep solids, NURBS, booleans, tessellation...) provided by a geometry kernel.
/// The core depends only on this interface; implementations wrap a specific kernel, such as OpenCASCADE.
/// </summary>
/// <remarks>
/// Lengths are in millimetres. The operations do not change the shapes they are given, and may run on several threads
/// at the same time, on the same shapes too: the application tessellates in the background while the interface reads
/// the same shapes, for example.
/// </remarks>
public interface IGeometryKernel
{
    KernelInfo Info { get; }

    // The primitives are solids with their own axes, which Place moves to where they are wanted.

    /// <summary>A box filling [0, dx] × [0, dy] × [0, dz].</summary>
    /// <exception cref="ArgumentException">A dimension is not a positive finite number.</exception>
    IKernelShape MakeBox(double dx, double dy, double dz);

    /// <summary>
    /// A cylinder around the Z axis, standing on the XY plane: from z = 0 to z = <paramref name="height"/>.
    /// </summary>
    /// <exception cref="ArgumentException">A dimension is not a positive finite number.</exception>
    IKernelShape MakeCylinder(double radius, double height);

    /// <summary>A sphere centred at the origin.</summary>
    /// <exception cref="ArgumentException">The radius is not a positive finite number.</exception>
    IKernelShape MakeSphere(double radius);

    /// <summary>
    /// A cone, or a truncated cone, around the Z axis, standing on the XY plane, which narrows or widens from
    /// <paramref name="bottomRadius"/> at z = 0 to <paramref name="topRadius"/> at z = <paramref name="height"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A radius is negative or not finite, both are zero or they are equal (that is a cylinder), or the height is not a
    /// positive finite number.
    /// </exception>
    IKernelShape MakeCone(double bottomRadius, double topRadius, double height);

    /// <summary>A torus centred at the origin, around the Z axis.</summary>
    /// <exception cref="ArgumentException">
    /// A radius is not a positive finite number, or the minor radius is not smaller than the major radius.
    /// </exception>
    IKernelShape MakeTorus(double majorRadius, double minorRadius);

    /// <summary>
    /// A new shape: <paramref name="shape"/> moved by <paramref name="placement"/>, which must be a rotation and a
    /// translation, without scaling or reflection. The original shape is not changed.
    /// </summary>
    /// <exception cref="ArgumentException">The placement is not a rotation and a translation.</exception>
    IKernelShape Place(IKernelShape shape, Transform placement);

    /// <summary>A new shape: <paramref name="shape"/> scaled by a factor from a point, the same in every direction.</summary>
    /// <exception cref="ArgumentException">The factor is not positive and finite.</exception>
    IKernelShape Scale(IKernelShape shape, Point3d centre, double factor);

    // The long operations, booleans and tessellation, report their progress, from 0 to 1, to an optional progress,
    // possibly from other threads, and stop when the token is cancelled, throwing OperationCanceledException.

    // The boolean operations make a new shape and leave their operands as they are. The faces and edges of the result
    // that lie on the same surface or curve are merged: two boxes side by side unite into a box of six faces.

    /// <summary>What is in either shape.</summary>
    /// <exception cref="GeometryKernelException">The operation fails, for example on invalid shapes.</exception>
    IKernelShape Union(IKernelShape first, IKernelShape second, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>What is in <paramref name="shape"/> and not in <paramref name="tool"/>.</summary>
    /// <exception cref="GeometryKernelException">
    /// The operation fails, or leaves nothing, as when a shape is subtracted from itself.
    /// </exception>
    IKernelShape Subtract(IKernelShape shape, IKernelShape tool, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>What is in both shapes.</summary>
    /// <exception cref="GeometryKernelException">
    /// The operation fails, or leaves nothing, as for shapes that do not meet.
    /// </exception>
    IKernelShape Intersect(IKernelShape first, IKernelShape second, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds curves to a shape, as a drawing does, one after the other, in a new shape. Each curve splits the edges it
    /// meets and the faces it crosses, and lies on the faces it touches; a closed loop drawn on a face splits it in two.
    /// Curves off the faces that close a planar loop with the edges of the shape make a face, as a circle alone does.
    /// Points closer than 1e-7 mm are the same point, so drawings snap to the points of the shape.
    /// </summary>
    /// <param name="shape">The shape drawn on; null for nothing yet.</param>
    /// <exception cref="ArgumentException">There are no curves, or one is degenerate or not finite.</exception>
    /// <exception cref="GeometryKernelException">The result would not be a valid shape.</exception>
    ShapeEdit AddEdges(IKernelShape? shape, IReadOnlyList<Curve> curves);

    /// <summary>
    /// Pushes or pulls a planar face along its normal, as <see cref="GetFaceInfo"/> gives it, by a distance, in a new
    /// shape. The face sweeps a prism: for a face of a solid, a positive distance, out of the solid, adds the prism to
    /// it and a negative one takes it away, and the faces of the prism on the plane of the faces of the solid they meet
    /// merge with them, as the sides of a box whose top is pulled; a face outside solids becomes the solid of the prism.
    /// The faces swept come from the face pushed or pulled.
    /// </summary>
    /// <exception cref="ArgumentException">The face is not planar, or the distance is zero or not finite.</exception>
    /// <exception cref="GeometryKernelException">The result would not be valid, or would leave nothing.</exception>
    ShapeEdit PushPull(IKernelShape shape, int face, double distance);

    /// <summary>The volume of the solids of the shape, in cubic millimetres.</summary>
    double ComputeVolume(IKernelShape shape);

    /// <summary>
    /// Whether the geometry and the topology of the shape are consistent, so that further operations can rely on them.
    /// An edit whose result is not valid is refused.
    /// </summary>
    bool IsValid(IKernelShape shape);

    // The faces, edges and vertices of a shape are numbered from 0, in the same order as Tessellate numbers them; an
    // index that the shape does not have throws ArgumentOutOfRangeException.

    /// <summary>How many solids, faces, edges and vertices the shape has, each counted once.</summary>
    ShapeTopology CountElements(IKernelShape shape);

    /// <summary>The area of a face and its plane, if it has one, in the coordinates of the shape.</summary>
    FaceInfo GetFaceInfo(IKernelShape shape, int face);

    /// <summary>
    /// Erases faces and edges of a shape, in a new shape. A face erased leaves its edges; an edge erased takes the
    /// faces it bounds with it. A solid that loses a face is left as the faces that remain. Erasing everything gives an
    /// empty shape, without faces or edges.
    /// </summary>
    /// <exception cref="ArgumentException">An index is not one of the shape.</exception>
    ShapeEdit RemoveElements(IKernelShape shape, IReadOnlyList<int> faces, IReadOnlyList<int> edges);

    /// <summary>The edges that bound a face, each once, wire after wire.</summary>
    IReadOnlyList<int> GetFaceEdges(IKernelShape shape, int face);

    /// <summary>The kind, the length, the ends, the middle and the tangents of an edge, and its circle if it has one.</summary>
    EdgeInfo GetEdgeInfo(IKernelShape shape, int edge);

    /// <summary>The point of a vertex, in the coordinates of the shape.</summary>
    Point3d GetVertexPoint(IKernelShape shape, int vertex);

    /// <summary>
    /// The shape as bytes in a format of the kernel, that only the same kind of kernel reads back with
    /// <see cref="Deserialize"/>. The geometry, the placements and the topology are kept; triangles are not.
    /// </summary>
    byte[] Serialize(IKernelShape shape);

    /// <summary>A shape from bytes written by <see cref="Serialize"/>.</summary>
    /// <exception cref="ArgumentException">The bytes are empty or are not a shape of this kernel.</exception>
    IKernelShape Deserialize(ReadOnlySpan<byte> data);

    /// <summary>
    /// Turns the faces of a shape into one triangle mesh, in the coordinates of the shape, and its edges into
    /// polylines that lie on the triangles. Each face has its own vertices, with the normals of its surface, so that
    /// the edges between faces look sharp; the front of the triangles is on the outside of a solid. The shape is not
    /// changed.
    /// </summary>
    /// <param name="linearDeflection">The largest distance, in millimetres, between a triangle and its surface.</param>
    /// <param name="angularDeflection">
    /// The largest angle, in radians, between the normals of neighbouring triangles: greater than 0 and not greater
    /// than π.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A deflection is not valid, or the shape has no faces, or too many triangles for the deflection.
    /// </exception>
    ShapeTessellation Tessellate(IKernelShape shape, double linearDeflection, double angularDeflection = 0.5,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The smallest box with its sides along the axes that contains the shape; <see cref="BoundingBox.Empty"/> for a
    /// shape without geometry.
    /// </summary>
    BoundingBox GetBoundingBox(IKernelShape shape);
}

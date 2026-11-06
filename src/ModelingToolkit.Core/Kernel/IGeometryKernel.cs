using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

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

    /// <summary>The volume of the solids of the shape, in cubic millimetres.</summary>
    double ComputeVolume(IKernelShape shape);

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

    /// <summary>The smallest box with its sides along the axes that contains the shape.</summary>
    /// <exception cref="ArgumentException">The shape has no geometry, as an empty compound.</exception>
    BoundingBox GetBoundingBox(IKernelShape shape);
}

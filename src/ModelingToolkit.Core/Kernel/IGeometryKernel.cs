using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Kernel;

/// <summary>
/// Exact-geometry operations (B-rep solids, NURBS, booleans, tessellation...) provided by a geometry kernel.
/// The core depends only on this interface; implementations wrap a specific kernel, such as OpenCASCADE.
/// </summary>
/// <remarks>Lengths are in millimetres.</remarks>
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

    double ComputeVolume(IKernelShape shape);

    /// <summary>The smallest box with its sides along the axes that contains the shape.</summary>
    BoundingBox GetBoundingBox(IKernelShape shape);
}

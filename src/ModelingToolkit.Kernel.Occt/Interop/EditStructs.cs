using System.Runtime.InteropServices;

namespace ModelingToolkit.Kernel.Occt.Interop;

// Mirrors of mtk_curve and mtk_edge_info in occt_bridge.h. Field order and types must match the C declarations; the
// native static_asserts and the managed tests check the sizes on both sides.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeCurve
{
    public const int Size = 88;

    public int Kind;
    public fixed double Points[9];
    public double Radius;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeEdgeInfo
{
    public const int Size = 192;

    public int Kind;
    public int Closed;
    public double Length;
    public fixed double Start[3];
    public fixed double End[3];
    public fixed double Middle[3];
    public fixed double StartTangent[3];
    public fixed double EndTangent[3];
    public fixed double Centre[3];
    public fixed double Normal[3];
    public double Radius;
}

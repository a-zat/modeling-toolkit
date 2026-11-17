namespace ModelingToolkit.Core.Meshes;

/// <summary>How the normals of a simplified mesh are made.</summary>
public enum NormalsMode
{
    /// <summary>Taken from the mesh as it was, at the nearest of its points: its shading is kept.</summary>
    Keep,

    /// <summary>Made again from the triangles, smooth across edges gentler than the crease angle.</summary>
    Recompute,
}

/// <summary>
/// What simplifying a mesh does, in order: weld its vertices, clean it, take out its small pieces, smooth it, reduce its
/// triangles and make its normals. Lengths are in millimetres, angles in degrees.
/// </summary>
public sealed record SimplifyOptions
{
    /// <summary>Vertices closer than this are one; 0 welds those at the same position only.</summary>
    public double WeldTolerance { get; init; } = 0.01;

    /// <summary>Takes out the triangles without area and those repeated, the same corners in the same order.</summary>
    public bool RemoveDegenerate { get; init; } = true;

    /// <summary>Takes out the pieces, the parts not joined to the rest, whose box is smaller across; 0 keeps them all.</summary>
    public double MinPieceSize { get; init; }

    /// <summary>The passes of a smoothing that keeps the volume (Taubin's); 0 for none.</summary>
    public int SmoothIterations { get; init; }

    /// <summary>How far smoothing may move a vertex from where it was.</summary>
    public double SmoothMaxMove { get; init; } = 5;

    /// <summary>Smoothing leaves the vertices of the edges sharper than this where they are; 0 to smooth them too.</summary>
    public double SmoothKeepAngle { get; init; }

    /// <summary>The share of the triangles to keep, in percent; 100 reduces none.</summary>
    public double KeepPercent { get; init; } = 100;

    /// <summary>The reduction stops before a point would move further from the surface than this; 0 for no limit.</summary>
    public double MaxError { get; init; } = 2;

    /// <summary>The reduction keeps the borders of the surface, the edges of one triangle, where they are.</summary>
    public bool KeepBorders { get; init; } = true;

    /// <summary>
    /// The reduction keeps the seams of the texture, where its coordinates jump, where they are; without, it reduces
    /// more, but surfaces that nearly touch, often joined at seams, may pierce each other, and the texture may stretch.
    /// </summary>
    public bool KeepTextureSeams { get; init; } = true;

    public NormalsMode Normals { get; init; } = NormalsMode.Keep;

    /// <summary>
    /// For <see cref="NormalsMode.Keep"/>: where the normals of a vertex differ by less than this, they become one, and
    /// the edge there smooth, no longer drawn; 0 keeps them as they are.
    /// </summary>
    public double SoftenAngle { get; init; }

    /// <summary>For <see cref="NormalsMode.Recompute"/>: edges sharper than this stay sharp, the others are smooth.</summary>
    public double CreaseAngle { get; init; } = 40;

    /// <summary>Puts the meshes of a block with the same material together, as one.</summary>
    public bool MergeByMaterial { get; init; }

    public static SimplifyOptions Default { get; } = new();
}

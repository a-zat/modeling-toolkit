using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A block reference that follows an anchor of another one, at a fixed offset from it: a chair that goes with the leaf
/// of a table. The two stay separate components, in the same block; the anchor is reached from a reference of that
/// block through the references of <see cref="Chain"/>.
/// </summary>
/// <remarks>
/// Only where the anchor is and how it turns are followed, not how it is scaled: an anchor in a part scaled down to
/// nothing, such as a leaf of a table hidden until it is extended, places the follower at the origin of that part.
/// </remarks>
public sealed class Attachment
{
    /// <param name="follower">The reference that follows.</param>
    /// <param name="chain">
    /// From a reference in the block of the follower, each in the block of the one before, to the reference whose block
    /// holds the anchor; empty when the anchor is in the block of the follower.
    /// </param>
    /// <param name="anchor">What is followed.</param>
    /// <param name="offset">From the frame of the anchor to the transform of the follower.</param>
    /// <exception cref="ArgumentException">The references and the anchor are not so linked, or the follower is among them.</exception>
    public Attachment(BlockReference follower, IReadOnlyList<BlockReference> chain, AnchorEntity anchor, Transform offset)
    {
        ArgumentNullException.ThrowIfNull(follower);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(offset);
        Block? holder = follower.Owner ?? throw new ArgumentException("The follower is in no block.", nameof(follower));
        foreach (BlockReference reference in chain)
        {
            if (ReferenceEquals(reference, follower))
            {
                throw new ArgumentException("A reference cannot follow an anchor inside itself.", nameof(chain));
            }
            if (!ReferenceEquals(reference.Owner, holder))
            {
                throw new ArgumentException("Each reference of the chain must be in the block of the one before.", nameof(chain));
            }
            holder = reference.Block;
        }
        if (!ReferenceEquals(anchor.Owner, holder))
        {
            throw new ArgumentException("The anchor must be in the block of the last reference of the chain.", nameof(anchor));
        }
        Follower = follower;
        Chain = [.. chain];
        Anchor = anchor;
        Offset = offset;
    }

    public BlockReference Follower { get; }

    public IReadOnlyList<BlockReference> Chain { get; }

    public AnchorEntity Anchor { get; }

    /// <summary>From the frame of the anchor to the transform of the follower; kept when the follower is moved alone.</summary>
    public Transform Offset { get; internal set; }

    /// <summary>Whether the follower, the references of the chain and the anchor are all in the document still.</summary>
    public bool IsActive
    {
        get
        {
            Block? holder = Follower.Owner;
            if (holder is null)
            {
                return false;
            }
            // As made: a reference moved into another block no longer reaches the anchor.
            foreach (BlockReference reference in Chain)
            {
                if (!ReferenceEquals(reference.Owner, holder))
                {
                    return false;
                }
                holder = reference.Block;
            }
            return ReferenceEquals(Anchor.Owner, holder);
        }
    }

    /// <summary>
    /// The frame of the anchor in the coordinates of the block of the follower, without scaling, given the transform of
    /// each reference of the chain: that of the document, or of an animation.
    /// </summary>
    public Transform AnchorFrame(Func<BlockReference, Transform> transformOf)
    {
        ArgumentNullException.ThrowIfNull(transformOf);
        Transform frame = Chain.Aggregate(Transform.Identity, (all, reference) => all * transformOf(reference));
        return Rigid(frame * Anchor.Placement);
    }

    /// <summary>Where the follower goes, given the transform of each reference of the chain.</summary>
    public Transform PlacementOf(Func<BlockReference, Transform> transformOf) => AnchorFrame(transformOf) * Offset;

    /// <summary>The offset that keeps a follower where it is, from where the anchor is now.</summary>
    public static Transform OffsetOf(BlockReference follower, IReadOnlyList<BlockReference> chain, AnchorEntity anchor)
    {
        ArgumentNullException.ThrowIfNull(follower);
        Transform frame = Rigid(chain.Aggregate(Transform.Identity, (all, r) => all * r.Transform) * anchor.Placement);
        return frame.Inverse() * follower.Transform;
    }

    /// <summary>
    /// The attachment, not yet made, of a reference to the anchor for attaching nearest to its origin, within a reach in
    /// millimetres: among the anchors of the block of the reference and of the other references there, at any depth;
    /// null for none.
    /// </summary>
    public static Attachment? Nearest(BlockReference follower, double reach)
    {
        ArgumentNullException.ThrowIfNull(follower);
        if (follower.Owner is not { } owner)
        {
            return null;
        }
        Point3d origin = follower.Transform.Apply(Point3d.Origin);
        (List<BlockReference> Chain, AnchorEntity Anchor)? nearest = null;
        double best = reach;
        var chain = new List<BlockReference>();
        void Look(Block block, Transform placement)
        {
            foreach (Entity entity in block.Entities)
            {
                if (entity is AnchorEntity { Kind: AnchorKind.Attach } anchor)
                {
                    double distance = (Rigid(placement * anchor.Placement).Apply(Point3d.Origin) - origin).Length;
                    if (distance <= best)
                    {
                        best = distance;
                        nearest = ([.. chain], anchor);
                    }
                }
                else if (entity is BlockReference reference && !ReferenceEquals(reference, follower) && chain.Count < 32)
                {
                    chain.Add(reference);
                    Look(reference.Block, placement * reference.Transform);
                    chain.RemoveAt(chain.Count - 1);
                }
            }
        }
        Look(owner, Transform.Identity);
        return nearest is { } found
            ? new Attachment(follower, found.Chain, found.Anchor, OffsetOf(follower, found.Chain, found.Anchor))
            : null;
    }

    // The rotation and the translation of a transform, its axes made of unit length and square to each other.
    internal static Transform Rigid(Transform t)
    {
        var x = new Vector3d(t.M00, t.M10, t.M20);
        var y = new Vector3d(t.M01, t.M11, t.M21);
        if (x.Length < 1e-300 || y.Length < 1e-300)
        {
            return Transform.Translation(t.M03, t.M13, t.M23);
        }
        x = x.Normalized();
        y -= x * y.Dot(x);
        if (y.Length < 1e-300)
        {
            return Transform.Translation(t.M03, t.M13, t.M23);
        }
        y = y.Normalized();
        Vector3d z = x.Cross(y);
        return new Transform(
            x.X, y.X, z.X, t.M03,
            x.Y, y.Y, z.Y, t.M13,
            x.Z, y.Z, z.Z, t.M23);
    }
}

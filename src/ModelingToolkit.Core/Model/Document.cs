using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>
/// A 3D model: the entities of <see cref="Root"/>, the blocks they reference, and the materials and textures they use.
/// Lengths are in millimetres and the Z axis points up.
/// </summary>
public sealed class Document
{
    public Document()
    {
        Root = new Block("Model") { Document = this };
        Blocks = new BlockCollection(this);
    }

    /// <summary>The model space: the block holding the top-level entities, referenced by no other block.</summary>
    public Block Root { get; }

    /// <summary>
    /// The blocks referenced by <see cref="BlockReference"/>s. Only the changes of the blocks listed here, and of
    /// <see cref="Root"/>, are raised by <see cref="Changed"/>.
    /// </summary>
    public BlockCollection Blocks { get; }

    /// <summary>
    /// Raised after every change of the structure of the document or of the properties of its blocks and entities,
    /// on the thread that made it. Materials and textures do not raise it.
    /// </summary>
    public event EventHandler<DocumentChange>? Changed;

    public List<Material> Materials { get; } = [];

    public List<Texture> Textures { get; } = [];

    /// <summary>
    /// The motions of parts of the model, such as doors that open. Like materials, they do not raise
    /// <see cref="Changed"/>; those whose block references are no longer in the document move nothing.
    /// </summary>
    public List<Animation> Animations { get; } = [];

    /// <summary>
    /// The references that follow anchors of others: when a reference of the chain of one is moved, its follower moves
    /// with it; when the follower is moved alone, it keeps its new place from the anchor.
    /// </summary>
    public IReadOnlyList<Attachment> Attachments => _attachments;

    private readonly List<Attachment> _attachments = [];
    private readonly HashSet<BlockReference> _placing = new(ReferenceEqualityComparer.Instance);

    public BoundingBox GetBoundingBox() => Root.GetBoundingBox();

    /// <summary>Makes a reference follow an anchor, from where it is; raises <see cref="AttachmentsChanged"/>.</summary>
    /// <exception cref="ArgumentException">
    /// The follower already follows an anchor, or the anchor moves with the follower, through other attachments.
    /// </exception>
    public void Attach(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (_attachments.Exists(other => ReferenceEquals(other.Follower, attachment.Follower)))
        {
            throw new ArgumentException("The reference already follows an anchor.", nameof(attachment));
        }
        if (DependsOn(attachment.Chain, attachment.Follower, []))
        {
            throw new ArgumentException("The anchor moves with the reference that would follow it.", nameof(attachment));
        }
        _attachments.Add(attachment);
        Raise(new AttachmentsChanged(attachment, true));
    }

    /// <summary>Stops a reference from following its anchor; false when it did not.</summary>
    public bool Detach(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (!_attachments.Remove(attachment))
        {
            return false;
        }
        Raise(new AttachmentsChanged(attachment, false));
        return true;
    }

    /// <summary>The attachment by which a reference follows an anchor; null for none.</summary>
    public Attachment? AttachmentOf(BlockReference follower) =>
        _attachments.Find(attachment => ReferenceEquals(attachment.Follower, follower));

    // Whether a reference of the chain is, or follows through other attachments, the reference given.
    private bool DependsOn(IReadOnlyList<BlockReference> chain, BlockReference follower, HashSet<Attachment> seen)
    {
        foreach (BlockReference reference in chain)
        {
            if (ReferenceEquals(reference, follower))
            {
                return true;
            }
            if (AttachmentOf(reference) is { } attachment && seen.Add(attachment) &&
                DependsOn(attachment.Chain, follower, seen))
            {
                return true;
            }
        }
        return false;
    }

    internal void Raise(DocumentChange change)
    {
        Changed?.Invoke(this, change);
        if (_attachments.Count == 0)
        {
            return;
        }
        if (change is PropertyChanged { Target: BlockReference moved, Property: nameof(BlockReference.Transform) })
        {
            Follow(moved);
        }
        else if (change is PropertyChanged { Target: AnchorEntity anchor, Property: nameof(AnchorEntity.Placement) })
        {
            // What follows the anchor goes where it is now.
            foreach (Attachment attachment in _attachments.ToArray())
            {
                if (ReferenceEquals(attachment.Anchor, anchor) && attachment.IsActive)
                {
                    Place(attachment);
                }
            }
        }
    }

    private void Place(Attachment attachment)
    {
        _placing.Add(attachment.Follower);
        try
        {
            attachment.Follower.Transform = attachment.PlacementOf(r => r.Transform);
        }
        finally
        {
            _placing.Remove(attachment.Follower);
        }
        Follow(attachment.Follower); // and what follows it
    }

    // A follower moved alone keeps its new place from the anchor; the followers of a reference moved go with it.
    private void Follow(BlockReference moved)
    {
        if (_placing.Contains(moved))
        {
            return;
        }
        foreach (Attachment attachment in _attachments)
        {
            if (ReferenceEquals(attachment.Follower, moved) && attachment.IsActive)
            {
                attachment.Offset = attachment.AnchorFrame(r => r.Transform).Inverse() * moved.Transform;
            }
        }
        foreach (Attachment attachment in _attachments.ToArray())
        {
            if (attachment.IsActive && attachment.Chain.Contains(moved, ReferenceEqualityComparer.Instance))
            {
                Place(attachment);
            }
        }
    }
}

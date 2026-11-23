using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;

namespace ModelingToolkit.Core.Editing.Tools;

/// <summary>What dragging a handle of the selection does.</summary>
public enum HandleKind
{
    /// <summary>A corner: moves the selection anywhere, snapping as the move tool does.</summary>
    Move,

    /// <summary>The middle of an edge, or a square at the middle of a face: moves the selection along one of the axes only.</summary>
    Slide,

    /// <summary>The middle of a face: turns the selection around the normal of the face, snapping to right angles.</summary>
    Rotate,
}

/// <summary>A handle of the box of the selection, a point in the model space to drag.</summary>
/// <param name="Grip">The point to take the selection by, as the move tool snaps to it.</param>
public readonly record struct SelectionHandle(HandleKind Kind, Point3d Point, Inference Grip);

/// <summary>
/// The box of an entity of the block being edited, turned with it, in the model space, and the points to take it by:
/// its corners, which move it freely, the middles of its edges, which move it along the axes, and the middles of its
/// faces, which turn it around their normal.
/// </summary>
public sealed class SelectionHandles
{
    private static readonly int[][] Edges =
        [[0, 1], [2, 3], [4, 5], [6, 7], [0, 2], [1, 3], [4, 6], [5, 7], [0, 4], [1, 5], [2, 6], [3, 7]];

    private static readonly int[][] Faces =
        [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 4, 5], [2, 3, 6, 7], [0, 2, 4, 6], [1, 3, 5, 7]];

    private readonly Point3d[] _corners; // of the box, the bit 1 for X, 2 for Y, 4 for Z
    private readonly List<int> _faceMasks = []; // for each handle, the faces it is on, a bit for each of Faces
    private readonly (Point3d Centre, Vector3d Normal)?[] _faces = new (Point3d, Vector3d)?[6];

    /// <summary>How far from edge-on, as the cosine of the angle to the line of sight, a face shows its handle to turn.</summary>
    public const double EdgeOn = 0.15;

    /// <summary>The largest radius of the arrows to turn, as a fraction of their distance from the eye.</summary>
    public const double ArrowSight = 0.05;

    /// <summary>
    /// The distance of a point from the eye in half steps of a power of two, by which the arrows to turn are sized:
    /// they change size, and are drawn again, only when it changes.
    /// </summary>
    public static int SightStep(Point3d point, Point3d eye) =>
        (int)Math.Round(2 * Math.Log2(Math.Max(point.DistanceTo(eye), 1)));

    /// <param name="placement">The placement in the model of the block that has the entity.</param>
    public SelectionHandles(Entity entity, Transform placement)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(placement);
        Entity = entity;
        Placement = placement;
        BoundingBox box;
        Transform frame = placement;
        if (entity is BlockReference reference)
        {
            box = reference.Block.GetBoundingBox();
            frame = placement * reference.Transform;
        }
        else
        {
            box = entity.GetBoundingBox();
        }
        IsEmpty = box.IsEmpty;
        _corners = IsEmpty ? [] : [.. Enumerable.Range(0, 8).Select(i => frame.Apply(new Point3d(
            (i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z)))];
        Handles = IsEmpty ? [] : [.. Make()];
    }

    public Entity Entity { get; }

    /// <summary>The placement in the model of the block that has the entity.</summary>
    public Transform Placement { get; }

    public bool IsEmpty { get; }

    /// <summary>
    /// The corners, then the middles of the edges, then the middles of the faces to turn, then the same middles to move
    /// along the axes.
    /// </summary>
    public IReadOnlyList<SelectionHandle> Handles { get; }

    /// <summary>The points to take the selection by, as the move and rotate tools snap to them.</summary>
    public IEnumerable<Inference> Grips => Handles.Select(handle => handle.Grip);

    /// <summary>The corners of the box, in the model space: bit 1 of the index for X, 2 for Y, 4 for Z.</summary>
    public IReadOnlyList<Point3d> Corners => _corners;

    /// <summary>The twelve edges of the box, transformed in the model space.</summary>
    public IEnumerable<PreviewLine> Box(Transform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        foreach (int[] edge in Edges)
        {
            yield return new PreviewLine(transform.Apply(_corners[edge[0]]), transform.Apply(_corners[edge[1]]));
        }
    }

    private IEnumerable<SelectionHandle> Make()
    {
        Point3d centre = Middle(0, 1, 2, 3, 4, 5, 6, 7);
        for (int f = 0; f < Faces.Length; f++)
        {
            Point3d middle = Middle(Faces[f]);
            Vector3d normal = middle - centre;
            _faces[f] = normal.Length < 1e-9 ? null : (middle, normal.Normalized()); // a flat box has no thickness
        }
        int MaskOf(params int[] corners)
        {
            int mask = 0;
            for (int f = 0; f < Faces.Length; f++)
            {
                if (corners.All(corner => Faces[f].Contains(corner)))
                {
                    mask |= 1 << f;
                }
            }
            return mask;
        }
        for (int i = 0; i < _corners.Length; i++)
        {
            _faceMasks.Add(MaskOf(i));
            yield return new SelectionHandle(HandleKind.Move, _corners[i], new Inference(InferenceKind.Endpoint, _corners[i]));
        }
        foreach (int[] edge in Edges)
        {
            Point3d a = _corners[edge[0]], b = _corners[edge[1]];
            if (a.DistanceTo(b) < 1e-9)
            {
                continue; // a flat box: the edge is a corner
            }
            Point3d middle = Middle(edge);
            _faceMasks.Add(MaskOf(edge));
            yield return new SelectionHandle(HandleKind.Slide, middle,
                new Inference(InferenceKind.Midpoint, middle, Edges: Inference.Segment(a, b)));
        }
        for (int f = 0; f < Faces.Length; f++)
        {
            if (_faces[f] is not ({ } middle, { } normal))
            {
                continue;
            }
            _faceMasks.Add(1 << f);
            yield return new SelectionHandle(HandleKind.Rotate, middle, new Inference(InferenceKind.Centre, middle,
                new Plane(normal, normal.Dot(middle - Point3d.Origin)), normal));
        }
        for (int f = 0; f < Faces.Length; f++)
        {
            if (_faces[f] is not ({ } middle, { } normal))
            {
                continue;
            }
            _faceMasks.Add(1 << f);
            yield return new SelectionHandle(HandleKind.Slide, middle, new Inference(InferenceKind.Centre, middle,
                new Plane(normal, normal.Dot(middle - Point3d.Origin)), normal));
        }
    }

    /// <summary>Whether the box is flat, without thickness along one of its sides: it has no faces to point at.</summary>
    public bool IsFlat => _faces.Any(face => face is null);

    /// <summary>
    /// The face of the box the ray meets first, turned to it; null when it misses the box, starts inside it, or the box
    /// is flat.
    /// </summary>
    public int? FaceUnder(PointerRay ray)
    {
        ArgumentNullException.ThrowIfNull(ray);
        int? nearest = null;
        double nearestDistance = double.MaxValue;
        for (int f = 0; f < _faces.Length; f++)
        {
            if (_faces[f] is not ({ } middle, { } normal) || normal.Dot(ray.Direction) >= 0 ||
                InferenceEngine.Intersect(ray, new Plane(normal, normal.Dot(middle - Point3d.Origin))) is not { } hit)
            {
                continue;
            }
            // Within the face: along each of its sides, no further from its middle than half of it.
            int[] corners = Faces[f];
            Vector3d u = (_corners[corners[1]] - _corners[corners[0]]) / 2, v = (_corners[corners[2]] - _corners[corners[0]]) / 2;
            Vector3d offset = hit - middle;
            if (Math.Abs(offset.Dot(u)) > u.Dot(u) * (1 + 1e-9) || Math.Abs(offset.Dot(v)) > v.Dot(v) * (1 + 1e-9))
            {
                continue;
            }
            double distance = (hit - ray.Origin).Length;
            if (distance < nearestDistance)
            {
                nearest = f;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    /// <summary>
    /// The handles the select tool shows on a face pointed at: its corners, which move the selection anywhere, the
    /// square at its middle, which moves it along the axes, and the arrow that turns it around its normal.
    /// </summary>
    public IReadOnlyList<int> OnFace(int face)
    {
        var shown = new List<int>();
        for (int i = 0; i < Handles.Count; i++)
        {
            SelectionHandle handle = Handles[i];
            bool corner = handle.Kind == HandleKind.Move && (_faceMasks[i] & 1 << face) != 0;
            bool middle = handle.Grip.Kind == InferenceKind.Centre && _faceMasks[i] == 1 << face;
            if (corner || middle)
            {
                shown.Add(i);
            }
        }
        return shown;
    }

    /// <summary>
    /// The indices of the handles not hidden by the box itself, seen from an eye: those on a face turned to it, and of
    /// the handles to turn, only on faces not seen nearly edge-on. A flat box shows them all, an eye inside the box none,
    /// as from inside a room the handles of the room would only be in the way.
    /// </summary>
    public IReadOnlyList<int> VisibleFrom(Point3d eye)
    {
        int front = 0, open = 0;
        for (int f = 0; f < _faces.Length; f++)
        {
            if (_faces[f] is not ({ } middle, { } normal))
            {
                continue;
            }
            Vector3d sight = eye - middle;
            double cosine = sight.Length < 1e-9 ? 1 : normal.Dot(sight) / sight.Length;
            front |= cosine > 0 ? 1 << f : 0;
            open |= cosine > EdgeOn ? 1 << f : 0;
        }
        bool all = _faces.Any(face => face is null);
        var visible = new List<int>();
        if (front == 0 && !all)
        {
            return visible;
        }
        for (int i = 0; i < Handles.Count; i++)
        {
            int mask = _faceMasks[i];
            if (Handles[i] is { Kind: HandleKind.Slide, Grip.Kind: InferenceKind.Centre })
            {
                continue; // shown only on the face pointed at
            }
            if (all || (Handles[i].Kind == HandleKind.Rotate ? (mask & open) != 0 : (mask & front) != 0))
            {
                visible.Add(i);
            }
        }
        return visible;
    }

    /// <summary>
    /// The arc that shows a handle to turn, lying on its face around its middle: three quarters of a circle a fifth as
    /// wide as the face is narrow, and the two strokes of its arrowhead; empty for the other handles. Seen from an eye,
    /// the arc is no larger than <see cref="ArrowSight"/> of its distance, by steps (<see cref="SightStep"/>), so
    /// that the face of a large box seen from near does not fill the view with it.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Point3d>> TurnArrow(SelectionHandle handle, Point3d? eye = null)
    {
        if (handle.Kind != HandleKind.Rotate || handle.Grip.Direction is not { } normal)
        {
            return [];
        }
        // The two directions along the face, from its middle to the middles of two of its edges.
        Vector3d? u = null, v = null;
        foreach (SelectionHandle other in Handles)
        {
            if (other.Kind != HandleKind.Slide)
            {
                continue;
            }
            Vector3d to = other.Point - handle.Point;
            if (Math.Abs(to.Dot(normal)) > 1e-6 || to.Length < 1e-9)
            {
                continue;
            }
            if (u is null)
            {
                u = to;
            }
            else if (Math.Abs(u.Value.Normalized().Dot(to.Normalized())) < 0.5)
            {
                v = to;
                break;
            }
        }
        if (u is not { } a || v is not { } b)
        {
            return [];
        }
        double radius = 0.4 * Math.Min(a.Length, b.Length);
        if (eye is { } from)
        {
            radius = Math.Min(radius, ArrowSight * Math.Pow(2, SightStep(handle.Point, from) / 2.0));
        }
        Vector3d x = a.Normalized(), y = normal.Cross(x).Normalized();
        Point3d At(double angle) => handle.Point + (x * Math.Cos(angle) + y * Math.Sin(angle)) * radius;
        const int Pieces = 24;
        double start = Math.PI / 4, end = start + 1.5 * Math.PI;
        Point3d[] arc = [.. Enumerable.Range(0, Pieces + 1).Select(i => At(start + (end - start) * i / Pieces))];
        Point3d tip = arc[^1];
        Vector3d along = (-x * Math.Sin(end) + y * Math.Cos(end)).Normalized(); // the way it turns, at the tip
        Vector3d outward = (tip - handle.Point).Normalized();
        double head = 0.35 * radius;
        return
        [
            arc,
            [tip - along * head + outward * head * 0.6, tip, tip - along * head - outward * head * 0.6],
        ];
    }

    /// <summary>
    /// The turn of the selection by a handle of the middle of a face, dragged to the ray of the pointer: around the
    /// normal of the face, from the direction the pointer first went off its middle, snapping to right angles; null
    /// when the ray does not meet the plane of the face, or is still at its middle.
    /// </summary>
    /// <param name="from">The direction the turn starts from, set by the first drag far enough from the middle.</param>
    /// <param name="heading">How far the selection is turned from the axes of the model, to snap square with them too.</param>
    public static HandleDrag? Turn(SelectionHandle handle, PointerRay ray, ref Vector3d? from, double? heading = null)
    {
        ArgumentNullException.ThrowIfNull(ray);
        if (handle.Grip is not { Plane: { } plane, Direction: { } axis } ||
            Math.Abs(ray.Direction.Dot(axis)) < 0.05 || InferenceEngine.Intersect(ray, plane) is not { } hit)
        {
            return null;
        }
        Vector3d to = hit - handle.Point;
        if (to.Length < 8 * ray.UnitSizeAt(handle.Point))
        {
            return from is null ? null : new HandleDrag(Transform.Identity, InferenceKind.None, [], 0, true);
        }
        from ??= to.Normalized();
        double degrees = RotateTool.Snap(Math.Atan2(from.Value.Cross(to).Dot(axis), from.Value.Dot(to)) * 180 / Math.PI,
            heading);
        Transform rotation = Transform.Rotation(handle.Point, axis, degrees * Math.PI / 180);
        double radius = to.Length;
        return new HandleDrag(rotation, InferenceKind.None,
            [new PreviewLine(handle.Point, handle.Point + from.Value * radius),
             new PreviewLine(handle.Point, handle.Point + rotation.ApplyVector(from.Value) * radius)],
            degrees, true);
    }

    private Point3d Middle(params int[] corners) =>
        Point3d.Origin + corners.Aggregate(default(Vector3d), (sum, i) => sum + (_corners[i] - Point3d.Origin)) / corners.Length;
}

/// <summary>What a drag of a handle makes.</summary>
/// <param name="Transform">The transform of the selection, in the model space.</param>
/// <param name="Along">The axis it snapped along, or none.</param>
/// <param name="Lines">The lines that show the drag.</param>
/// <param name="Measure">The distance in millimetres, or the angle in degrees when <paramref name="IsAngle"/>.</param>
public sealed record HandleDrag(
    Transform Transform, InferenceKind Along, IReadOnlyList<PreviewLine> Lines, double Measure, bool IsAngle);

using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.ViewModels;

/// <summary>
/// An animation of the document, as the inspector shows it: how far it has gone, from 0 (as modelled) to 1 (its end),
/// which the slider sets, and whether it is open, which plays it to its end or back to its start.
/// </summary>
public sealed partial class AnimationItemViewModel : ObservableObject
{
    private readonly AnimationsViewModel _owner;
    private bool _following; // set from another animation that holds all of its parts

    internal AnimationItemViewModel(Animation animation, AnimationsViewModel owner)
    {
        Animation = animation;
        _owner = owner;
        Targets = new HashSet<BlockReference>(animation.Targets, ReferenceEqualityComparer.Instance);
    }

    public Animation Animation { get; }

    /// <summary>
    /// The name of the component it moves, when it moves one; of the animation, which a file names as it likes, when it
    /// moves several parts.
    /// </summary>
    public string Name =>
        (Targets.Count == 1 ? Targets.First().Name ?? Targets.First().Block.Name : null) is { Length: > 0 } component
            ? component.Replace('_', ' ')
            : Animation.Name.Replace('_', ' ');

    internal HashSet<BlockReference> Targets { get; }

    /// <summary>Where the animation is shown, from 0 to 1, along its duration.</summary>
    [ObservableProperty]
    public partial double Position { get; set; }

    /// <summary>Whether it is open, or opening: it plays to its end when set, back to its start when cleared.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Where it plays to, while it plays; null when it stays where it is.</summary>
    internal double? Goal { get; set; }

    /// <summary>When it was last moved, against the others: the latest moved places the parts they share.</summary>
    internal long Moved { get; set; }

    /// <summary>Whether it has been moved at all; until then, its parts are where they are modelled.</summary>
    internal bool IsMoved => Moved > 0;

    /// <summary>Plays it from where it is to its end, or back to its start.</summary>
    public void Toggle() => IsOpen = !IsOpen;

    /// <summary>Plays it to its end, or back to its start, even if it is open, or closed, already, but not all the way.</summary>
    internal void Play(bool open)
    {
        if (IsOpen != open)
        {
            IsOpen = open;
            return;
        }
        Goal = open ? 1 : 0;
        _owner.OnPlayed(this);
    }

    // A drag of the slider shows the animation there and stops it playing.
    partial void OnPositionChanged(double value)
    {
        if (!_following)
        {
            // Anything but closed is open: the toggle closes it.
            _following = true;
            IsOpen = value > 0;
            _following = false;
            _owner.OnMoved(this);
        }
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (!_following)
        {
            Goal = value ? 1 : 0;
            _owner.OnPlayed(this);
        }
    }

    /// <summary>Back at its start, stopped: another animation took a part of it.</summary>
    internal void Reset()
    {
        _following = true;
        try
        {
            Position = 0;
            IsOpen = false;
            Goal = null;
            Moved = 0;
        }
        finally
        {
            _following = false;
        }
    }

    /// <summary>Moves it as another animation that holds all its parts, without making it the latest moved itself.</summary>
    internal void Follow(AnimationItemViewModel leader)
    {
        _following = true;
        try
        {
            Position = leader.Position;
            IsOpen = leader.IsOpen;
            Goal = null;
            Moved = leader.Moved;
        }
        finally
        {
            _following = false;
        }
    }

    /// <summary>Moves it as it plays, by a part of its way.</summary>
    internal void Step(double fraction)
    {
        if (Goal is not { } goal)
        {
            return;
        }

        double next = goal > Position ? Math.Min(goal, Position + fraction) : Math.Max(goal, Position - fraction);
        _following = true;
        try
        {
            Position = next;
        }
        finally
        {
            _following = false;
        }
        if (next == goal)
        {
            Goal = null;
        }
    }
}

/// <summary>
/// The animations of the open document, those of the selection shown in the inspector, and the transformations of the
/// parts they move, which the 3D view shows (<see cref="Pose"/>). Playing them changes nothing in the document: they
/// start again from the model as it is when another document is opened.
/// </summary>
/// <remarks>
/// When animations move the same part, the one moved last places it. An animation that moves all the parts of
/// another, as one that opens everything, moves that one along with it.
/// </remarks>
public sealed partial class AnimationsViewModel : ObservableObject
{
    private readonly List<AnimationItemViewModel> _all = [];
    private Document? _document;
    private IReadOnlyList<Entity>? _selection;
    private long _moves;
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();
    private readonly bool _useTimer;
    private List<HiddenPart>? _hidden; // see FindHidden
    private List<BeaconPart>? _beaconParts; // see Beacons

    public AnimationsViewModel()
        : this(useTimer: true)
    {
    }

    /// <param name="useTimer">
    /// Whether animations play by themselves, on a timer of the user interface thread; without it, as in tests, only
    /// <see cref="Advance"/> moves them.
    /// </param>
    internal AnimationsViewModel(bool useTimer)
    {
        _useTimer = useTimer;
        All = new AnimationGroupViewModel("All", [], isExpanded: true);
        Pose = new Dictionary<BlockReference, Transform>(ReferenceEqualityComparer.Instance);
    }

    /// <summary>The animations of the selection: those that move it, a part of it or a part holding it.</summary>
    public ObservableCollection<AnimationItemViewModel> Items { get; } = [];

    /// <summary>All the animations shown together, as one.</summary>
    public AnimationGroupViewModel All { get; }

    /// <summary>The transformation of each part moved by the animations, in place of the one of the document.</summary>
    [ObservableProperty]
    public partial IReadOnlyDictionary<BlockReference, Transform> Pose { get; private set; }

    /// <summary>Whether the document has animations at all.</summary>
    [ObservableProperty]
    public partial bool HasAnimations { get; private set; }

    /// <summary>Whether something in the selection moves.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeveral))]
    public partial bool HasItems { get; private set; }

    /// <summary>Whether several animations are shown, so that one switch and slider move them all.</summary>
    public bool HasSeveral => Items.Count > 1 && !_oneMovesAll;

    // Whether one of the movements shown moves all the parts of the others: a movement that opens everything, which
    // does what the row for all of them would.
    private bool _oneMovesAll;

    /// <summary>Whether an animation is playing, so that the view asks for frames.</summary>
    public bool IsPlaying => _all.Any(item => item.Goal is not null);

    /// <summary>Shows the animations of another document, all at their start.</summary>
    public void Show(Document? document)
    {
        _document = document;
        _all.Clear();
        _moves = 0;
        StopTimer();
        Refresh();
    }

    /// <summary>Follows the animations added to or removed from the document, as by an import or its undoing.</summary>
    public void Refresh()
    {
        _hidden = null;
        _beaconParts = null;
        IReadOnlyList<Animation> animations = _document?.Animations ?? [];
        if (!_all.Select(item => item.Animation).SequenceEqual(animations))
        {
            var known = new Dictionary<Animation, AnimationItemViewModel>(ReferenceEqualityComparer.Instance);
            foreach (AnimationItemViewModel item in _all)
            {
                known[item.Animation] = item;
            }
            _all.Clear();
            _all.AddRange(animations.Select(animation =>
                known.TryGetValue(animation, out AnimationItemViewModel? item) ? item : new AnimationItemViewModel(animation, this)));
            UpdatePose();
        }
        HasAnimations = _all.Count > 0;
        Filter(_selection);
    }

    /// <summary>Shows the animations of a node of the model tree, given its path; all of them for the model or null.</summary>
    public void Filter(IReadOnlyList<Entity>? path)
    {
        _selection = path;
        List<AnimationItemViewModel> shown = [.. _all.Where(item => InScene(item) && Concerns(item, path))];
        if (shown.SequenceEqual(Items))
        {
            return;
        }
        Items.Clear();
        foreach (AnimationItemViewModel item in shown)
        {
            Items.Add(item);
        }
        _oneMovesAll = shown.Count > 1 && shown.Any(all => shown.All(other => ReferenceEquals(other, all) ||
            other.Targets.All(target => all.Targets.Contains(target))));
        HasItems = Items.Count > 0;
        OnPropertyChanged(nameof(HasSeveral));

        All.Set([.. shown]);
    }

    /// <summary>
    /// Opens or closes what was clicked, given the path of the mesh: the animation of the innermost part of the path
    /// that one moves, the one moving fewest parts if several do. When the path moves nothing but holds parts that are
    /// hidden until an animation shows them, such as the leaves of a table that extends, and the point clicked is near
    /// where one shows, that one. Returns whether there was one.
    /// </summary>
    public bool ToggleAt(IReadOnlyList<Entity>? path, Point3d? point = null)
    {
        if (At(path, point) is not { } item)
        {
            return false;
        }
        item.Toggle();
        return true;
    }

    /// <summary>Whether something at the path of a mesh opens or closes: see <see cref="ToggleAt"/>.</summary>
    public bool CanToggleAt(IReadOnlyList<Entity>? path, Point3d? point = null) => At(path, point) is not null;

    private AnimationItemViewModel? At(IReadOnlyList<Entity>? path, Point3d? point)
    {
        if (path is null)
        {
            return null;
        }
        foreach (BlockReference reference in path.OfType<BlockReference>().Reverse())
        {
            AnimationItemViewModel? item = _all
                .Where(candidate => candidate.Targets.Contains(reference))
                .OrderBy(candidate => candidate.Targets.Count)
                .FirstOrDefault();
            if (item is not null)
            {
                return item;
            }
        }
        return point is { } clicked ? HiddenNear(path, clicked) : null;
    }

    // A part scaled down to nothing has no surface to click: the animation that shows it, of the part that would be
    // nearest to the click, within its own size, among those held by the parts of the path, the innermost first.
    private AnimationItemViewModel? HiddenNear(IReadOnlyList<Entity> path, Point3d point)
    {
        IReadOnlyList<HiddenPart> hidden = _hidden ??= FindHidden();
        if (hidden.Count == 0)
        {
            return null; // most models: nothing to look for
        }
        List<BlockReference> references = [.. path.OfType<BlockReference>()];
        AnimationItemViewModel? nearest = null;
        int nearestDepth = 0;
        double nearestDistance = double.MaxValue;
        foreach (HiddenPart part in hidden)
        {
            // How deep the path and the chain to the part go together: the part is in the reference clicked there.
            int depth = 0;
            while (depth < references.Count && depth < part.Chain.Length &&
                   ReferenceEquals(references[depth], part.Chain[depth]))
            {
                depth++;
            }
            if (depth < Owning(part.Chain) || depth < nearestDepth ||
                !part.Item.Animation.PoseAt(part.Item.Animation.Duration).TryGetValue(part.Target, out Transform? shown))
            {
                continue;
            }
            BoundingBox box = part.Target.Block.GetBoundingBox();
            if (box.IsEmpty)
            {
                continue;
            }
            Transform holder = part.Chain.Aggregate(Transform.Identity, (all, r) => all * r.Transform);
            BoundingBox where = box.Transform(holder * shown);
            double distance = DistanceTo(where, point);
            double size = Math.Max(500, Math.Max(where.Max.X - where.Min.X,
                Math.Max(where.Max.Y - where.Min.Y, where.Max.Z - where.Min.Z)));
            if (distance <= size && (depth > nearestDepth || distance < nearestDistance))
            {
                nearest = part.Item;
                nearestDepth = depth;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    /// <summary>
    /// How many references of a chain, from the model down, a click must be inside for what the chain holds to be the
    /// one clicked: all of them, so that a click on the chair beside a table, gathered with it in one component, is not
    /// taken for a click on the table.
    /// </summary>
    internal static int Owning(IReadOnlyList<BlockReference> chain) => Math.Max(1, chain.Count);

    // The parts hidden at rest that an animation shows, each with the references from the model down to the one holding
    // it, once for every place it is in: found again after a change of the document, and only when a part is hidden.
    private List<HiddenPart> FindHidden()
    {
        var targets = new Dictionary<BlockReference, AnimationItemViewModel>(ReferenceEqualityComparer.Instance);
        foreach (AnimationItemViewModel item in _all.OrderByDescending(item => item.Targets.Count))
        {
            foreach (BlockReference target in item.Targets.Where(IsHidden))
            {
                targets[target] = item; // the one moving fewest parts comes last, and wins
            }
        }
        return [.. ChainsTo(targets.Keys).Select(found => new HiddenPart(targets[found.Target], found.Target, found.Chain))];
    }

    // Each place of the references given in the document, with the references from the model down to the one holding
    // it; what a reference given holds is not looked into.
    private List<(BlockReference Target, BlockReference[] Chain)> ChainsTo(IEnumerable<BlockReference> wanted)
    {
        var found = new List<(BlockReference, BlockReference[])>();
        var targets = new HashSet<BlockReference>(wanted, ReferenceEqualityComparer.Instance);
        if (targets.Count == 0 || _document is null)
        {
            return found;
        }
        var chain = new List<BlockReference>();
        void Walk(Block block)
        {
            foreach (BlockReference reference in block.Entities.OfType<BlockReference>())
            {
                if (targets.Contains(reference))
                {
                    found.Add((reference, [.. chain]));
                    continue;
                }
                chain.Add(reference);
                Walk(reference.Block);
                chain.RemoveAt(chain.Count - 1);
            }
        }
        Walk(_document.Root);
        return found;
    }

    /// <summary>
    /// A beacon for each part that an animation of its own moves, while that animation is at its start: in the middle
    /// of the part, or, for a part hidden until the animation shows it, in the middle of where it shows.
    /// </summary>
    public IReadOnlyList<WalkBeacon> Beacons()
    {
        _beaconParts ??= [.. ChainsTo(_all.Where(item => item.Targets.Count == 1).Select(item => item.Targets.First()))
            .Select(found => new BeaconPart(
                _all.First(item => item.Targets.Count == 1 && item.Targets.Contains(found.Target)),
                found.Target, found.Chain, found.Target.Block.GetBoundingBox()))];
        var beacons = new List<WalkBeacon>();
        foreach (BeaconPart part in _beaconParts)
        {
            AnimationItemViewModel item = part.Item;
            if (item.IsOpen || item.Position > 0 || item.Goal is not null || part.Box.IsEmpty || !InScene(item))
            {
                continue;
            }
            Transform holder = part.Chain.Aggregate(Transform.Identity, (all, reference) => all * Shown(reference));
            Transform at = IsHidden(part.Target) &&
                item.Animation.PoseAt(item.Animation.Duration).TryGetValue(part.Target, out Transform? open)
                ? open
                : Shown(part.Target);
            BoundingBox where = part.Box.Transform(holder * at);
            var centre = new Point3d((where.Min.X + where.Max.X) / 2, (where.Min.Y + where.Max.Y) / 2,
                (where.Min.Z + where.Max.Z) / 2);
            beacons.Add(new WalkBeacon(centre, Math.Clamp((where.Max - where.Min).Length / 2, 50, 400)));
        }
        return beacons;
    }

    private Transform Shown(BlockReference reference) =>
        Pose.TryGetValue(reference, out Transform? posed) ? posed : reference.Transform;

    private sealed record BeaconPart(
        AnimationItemViewModel Item, BlockReference Target, BlockReference[] Chain, BoundingBox Box);

    private sealed record HiddenPart(AnimationItemViewModel Item, BlockReference Target, BlockReference[] Chain);

    // Scaled to a point, a line or a plane, which is how a file hides a part until an animation grows it.
    private static bool IsHidden(BlockReference part)
    {
        Transform t = part.Transform;
        double determinant = t.M00 * (t.M11 * t.M22 - t.M12 * t.M21) - t.M01 * (t.M10 * t.M22 - t.M12 * t.M20) +
            t.M02 * (t.M10 * t.M21 - t.M11 * t.M20);
        return Math.Abs(determinant) < 1e-9;
    }

    private static double DistanceTo(BoundingBox box, Point3d point)
    {
        double dx = Math.Max(0, Math.Max(box.Min.X - point.X, point.X - box.Max.X));
        double dy = Math.Max(0, Math.Max(box.Min.Y - point.Y, point.Y - box.Max.Y));
        double dz = Math.Max(0, Math.Max(box.Min.Z - point.Z, point.Z - box.Max.Z));
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Moves the animations that play by the time passed, in seconds.</summary>
    internal void Advance(double seconds)
    {
        foreach (AnimationItemViewModel item in _all.Where(item => item.Goal is not null).ToList())
        {
            item.Step(seconds / Math.Max(item.Animation.Duration, 0.05));
            Lead(item);
        }
        UpdatePose();
        if (!IsPlaying)
        {
            StopTimer();
        }
    }

    internal void OnMoved(AnimationItemViewModel item)
    {
        item.Moved = ++_moves;
        item.Goal = null;
        StopOthers(item);
        Lead(item);
        UpdatePose();
    }

    internal void OnPlayed(AnimationItemViewModel item)
    {
        item.Moved = ++_moves;
        StopOthers(item);
        Lead(item);
        // Without an application, as in tests that make no window, touching the dispatcher would bind it to the thread
        // of the caller and keep the application from taking it.
        if (item.Goal is not null && _useTimer && Avalonia.Application.Current is not null)
        {
            StartTimer();
        }
    }

    // A part has one movement at a time: those that share parts with the animation moved, but neither hold all of the
    // parts of the other nor are held by it, go back to their start. An animation of one door, in one of every door, stays
    // over it, as the last moved places a part.
    private void StopOthers(AnimationItemViewModel item)
    {
        foreach (AnimationItemViewModel other in _all)
        {
            if (other != item && other.IsMoved && other.Targets.Overlaps(item.Targets) &&
                !other.Targets.IsSubsetOf(item.Targets) && !item.Targets.IsSubsetOf(other.Targets))
            {
                other.Reset();
            }
        }
    }

    // The animations whose parts are all moved by this one follow it.
    private void Lead(AnimationItemViewModel leader)
    {
        foreach (AnimationItemViewModel other in _all)
        {
            if (other != leader && other.Targets.Count < leader.Targets.Count && other.Targets.IsSubsetOf(leader.Targets))
            {
                other.Follow(leader);
            }
        }
    }

    private void UpdatePose()
    {
        var pose = new Dictionary<BlockReference, Transform>(ReferenceEqualityComparer.Instance);
        foreach (AnimationItemViewModel item in _all.Where(item => item.IsMoved).OrderBy(item => item.Moved))
        {
            foreach ((BlockReference target, Transform transform) in
                     item.Animation.PoseAt(item.Position * item.Animation.Duration))
            {
                pose[target] = transform;
            }
        }
        // What follows an anchor moved by the animations goes with it, and what follows that.
        if (_document is { Attachments.Count: > 0 } document && pose.Count > 0)
        {
            Transform TransformOf(BlockReference reference) =>
                pose.TryGetValue(reference, out Transform? posed) ? posed : reference.Transform;
            for (int pass = 0; pass < document.Attachments.Count; pass++)
            {
                bool moved = false;
                foreach (Attachment attachment in document.Attachments)
                {
                    if (attachment.IsActive && !pose.ContainsKey(attachment.Follower) &&
                        attachment.Chain.Any(pose.ContainsKey))
                    {
                        pose[attachment.Follower] = attachment.PlacementOf(TransformOf);
                        moved = true;
                    }
                }
                if (!moved)
                {
                    break;
                }
            }
        }
        Pose = pose;
    }

    private void StartTimer()
    {
        if (_timer is { IsEnabled: true })
        {
            return;
        }

        _timer ??= CreateTimer();
        _clock.Restart();
        _timer.Start();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) =>
        {
            double elapsed = _clock.Elapsed.TotalSeconds;
            _clock.Restart();
            Advance(elapsed);
        });
        return timer;
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _clock.Reset();
    }

    // The parts erased from the document move nothing; their animation comes back with them, by an undo.
    private static bool InScene(AnimationItemViewModel item) => item.Targets.Any(target => target.Owner is not null);

    // Whether an animation moves the node of the path, a part of it or a part holding it.
    private static bool Concerns(AnimationItemViewModel item, IReadOnlyList<Entity>? path)
    {
        if (path is null || path.Count == 0)
        {
            return true;
        }
        if (path.OfType<BlockReference>().Any(item.Targets.Contains))
        {
            return true;
        }
        if (path[^1] is not BlockReference selected)
        {
            return false;
        }

        var blocks = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Block>([selected.Block]);
        while (pending.TryPop(out Block? block))
        {
            if (!blocks.Add(block))
            {
                continue;
            }
            foreach (BlockReference reference in block.Entities.OfType<BlockReference>())
            {
                pending.Push(reference.Block);
            }
        }
        return item.Targets.Any(target => target.Owner is { } owner && blocks.Contains(owner));
    }
}

/// <summary>
/// Animations shown together, as those of a layer, with a switch and a slider that open, close or move them all at
/// once, and whether the group is expanded in the panel.
/// </summary>
public sealed partial class AnimationGroupViewModel : ObservableObject
{
    private List<AnimationItemViewModel> _items;
    private bool _setting;

    internal AnimationGroupViewModel(string name, List<AnimationItemViewModel> items, bool isExpanded)
    {
        Name = name;
        _items = items;
        IsExpanded = isExpanded;
    }

    public string Name { get; }

    public IReadOnlyList<AnimationItemViewModel> Items => _items;

    public string Title => $"{Name.Replace('_', ' ')} ({_items.Count})";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Where all the animations of the group are shown, from 0 to 1, as the slider last set them.</summary>
    [ObservableProperty]
    public partial double Position { get; set; }

    /// <summary>Opens, or closes, all the animations of the group.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    internal void Set(List<AnimationItemViewModel> items)
    {
        _items = items;
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(Title));
    }

    partial void OnPositionChanged(double value)
    {
        if (_setting)
        {
            return;
        }
        foreach (AnimationItemViewModel item in _items)
        {
            item.Position = value;
        }
        _setting = true;
        IsOpen = value > 0;
        _setting = false;
    }

    partial void OnIsOpenChanged(bool value)
    {
        if (_setting)
        {
            return;
        }
        foreach (AnimationItemViewModel item in _items)
        {
            item.Play(value);
        }
    }
}

using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>Where the camera of the 3D view is and where it looks, in millimetres; the field of view is vertical.</summary>
public sealed record ViewCamera(Point3d Eye, Point3d Target, Vector3d Up, double FieldOfView);

/// <summary>
/// A camera at the height of the eyes of a person standing on a floor, who walks on it and looks around: forward and
/// sideways move on the floor, whatever the camera looks at, and looking up or down stops short of the vertical.
/// </summary>
public sealed class WalkCamera
{
    // The defaults of the <see cref="WalkSettings"/>.

    /// <summary>Above the floor, in millimetres.</summary>
    public const double EyeHeight = 1600;

    /// <summary>A walk, in millimetres per second; running is <see cref="RunFactor"/> times faster.</summary>
    public const double Speed = 1400;

    public const double RunFactor = 2.5;

    /// <summary>Vertical, in degrees: wider than the one of the orbit camera, as the eyes of a person in a room.</summary>
    public const double FieldOfView = 65;

    /// <summary>The turn of a drag, in radians per point of the screen.</summary>
    public const double LookSensitivity = 0.004;

    /// <summary>The highest step that is climbed, in millimetres: more than the knees, a stair riser.</summary>
    public const double StepHeight = 400;

    // A fall speeds up by this, in millimetres per second squared, to a speed that does not grow past the maximum.
    private const double Gravity = 9800;
    private const double MaxFall = 20000;

    private (Point3d Eye, double Yaw, double Pitch)? _standing; // where it was before sitting

    private double _fall; // millimetres per second, downwards
    private bool _grounded;

    private const double MaxPitch = 85 * Math.PI / 180;

    private WalkCamera(Point3d eye, double yaw, double pitch, WalkSettings settings)
    {
        Settings = settings;
        Eye = eye;
        Yaw = yaw;
        Pitch = pitch;
    }

    public Point3d Eye { get; private set; }

    /// <summary>How the person walks; changed while walking, it applies from the next move.</summary>
    public WalkSettings Settings { get; set; }

    /// <summary>
    /// What of a move of the eyes on the floor goes through, given where they are, as a <see cref="WalkCollider"/>
    /// says; null for all of it.
    /// </summary>
    public Func<Point3d, Vector3d, Vector3d>? Limit { get; set; }

    /// <summary>Where the eyes may stand, given where they are: out of what they would stand in; null for where they are.</summary>
    public Func<Point3d, Point3d>? Clear { get; set; }

    /// <summary>
    /// The height of the ground under a place, given it and the height of the feet plus the step that is climbed, as a
    /// <see cref="WalkCollider"/> says; null for no ground, in which case the eyes keep their height.
    /// </summary>
    public Func<double, double, double, double?>? Ground { get; set; }

    /// <summary>Whether the eyes are not yet on the ground, so that <see cref="Gravitate"/> is to be called again.</summary>
    public bool IsFalling => !_grounded && Settings.Gravity && Ground is not null;

    /// <summary>Where it looks around the vertical, in radians, counterclockwise from the X axis.</summary>
    public double Yaw { get; private set; }

    /// <summary>How far it looks up, in radians, negative down.</summary>
    public double Pitch { get; private set; }

    /// <summary>
    /// Stands on a floor where the orbit camera looked, at the height of the eyes, facing the way it faced: entering a
    /// model seen from outside puts the person in the middle of it; or on a point of the floor, <paramref name="at"/>.
    /// </summary>
    public static WalkCamera Enter(ViewCamera camera, double floor, WalkSettings? settings = null, Point3d? at = null)
    {
        settings ??= WalkSettings.Default;
        ArgumentNullException.ThrowIfNull(camera);
        Vector3d direction = camera.Target - camera.Eye;
        // Looking straight down, the top of the screen is the way ahead.
        Vector3d ahead = Math.Abs(direction.X) + Math.Abs(direction.Y) > 1e-6 * direction.Length ? direction : camera.Up;
        double yaw = Math.Atan2(ahead.Y, ahead.X);
        Point3d stand = at ?? new Point3d(camera.Target.X, camera.Target.Y, floor);
        return new WalkCamera(new Point3d(stand.X, stand.Y, stand.Z + settings.EyeHeight), yaw, 0, settings);
    }

    /// <summary>Turns by a drag of the pointer, in points: to the right turns right, up looks up.</summary>
    public void Look(double right, double up)
    {
        Yaw = Normalized(Yaw - right * Settings.LookSensitivity);
        Pitch = Math.Clamp(Pitch + up * Settings.LookSensitivity, -MaxPitch, MaxPitch);
    }

    /// <summary>Whether it looks from a point of view taken by <see cref="Sit"/>, where it does not walk.</summary>
    public bool IsSeated => _standing is not null;

    /// <summary>Takes a point of view, as sitting on a chair: the eyes there, looking that way; it does not walk from it.</summary>
    public void Sit(Point3d eye, double yaw)
    {
        _standing ??= (Eye, Yaw, Pitch);
        Eye = eye;
        Yaw = Normalized(yaw);
        Pitch = 0;
    }

    /// <summary>Back where it was before sitting, looking as it did.</summary>
    public void Stand()
    {
        if (_standing is { } standing)
        {
            (Eye, Yaw, Pitch) = standing;
            _standing = null;
            _grounded = false;
        }
    }

    /// <summary>Turns around the vertical by an angle, in radians, counterclockwise seen from above: left for a positive one.</summary>
    public void Turn(double angle) => Yaw = Normalized(Yaw + angle);

    /// <summary>Moves the eyes out of what they stand in, where <see cref="Clear"/> knows it, as after being put on a point.</summary>
    public void Settle()
    {
        if (Clear is { } clear)
        {
            Eye = clear(Eye);
        }
    }

    /// <summary>
    /// Walks for a time, in seconds: forward and right between -1 and 1, as given by the keys held, on the floor.
    /// Both together go no faster than one.
    /// </summary>
    public void Walk(double forward, double right, double seconds, bool run)
    {
        double length = Math.Sqrt(forward * forward + right * right);
        if (length < 1e-9 || seconds <= 0)
        {
            return;
        }
        double step = Settings.Speed * (run ? Settings.RunFactor : 1) * seconds / Math.Max(length, 1);
        Vector3d ahead = new(Math.Cos(Yaw), Math.Sin(Yaw), 0), side = new(Math.Sin(Yaw), -Math.Cos(Yaw), 0);
        Move((ahead * forward + side * right) * step);
    }

    private void Move(Vector3d move)
    {
        if (IsSeated)
        {
            return;
        }
        Eye += Limit is { } limit ? limit(Eye, move) : move;
        _grounded = false; // the ground may end there
    }

    /// <summary>
    /// Follows the ground for a time, in seconds: up a low step, and down, falling faster as it goes, to what is under
    /// the feet. The eyes stay where they are with no ground under them. Returns whether they are still falling.
    /// </summary>
    public bool Gravitate(double seconds)
    {
        if (!Settings.Gravity || Ground is not { } ground || IsSeated)
        {
            _grounded = true;
            return false;
        }
        double feet = Eye.Z - Settings.EyeHeight;
        if (ground(Eye.X, Eye.Y, feet + Settings.StepHeight) is not { } floor)
        {
            _grounded = true;
            return false;
        }
        double next;
        if (floor >= feet)
        {
            // Up a step, smoothly, or level.
            next = feet + (floor - feet) * Math.Min(1, 15 * seconds);
            if (floor - next < 1)
            {
                next = floor;
            }
            _fall = 0;
        }
        else
        {
            _fall = Math.Min(MaxFall, _fall + Gravity * seconds);
            next = Math.Max(floor, feet - _fall * seconds);
            if (next <= floor)
            {
                _fall = 0;
            }
        }
        Eye = new Point3d(Eye.X, Eye.Y, next + Settings.EyeHeight);
        _grounded = next == floor;
        return !_grounded;
    }

    /// <summary>The camera of the view: looking a metre ahead, the vertical up.</summary>
    public ViewCamera ToCamera()
    {
        var direction = new Vector3d(
            Math.Cos(Pitch) * Math.Cos(Yaw), Math.Cos(Pitch) * Math.Sin(Yaw), Math.Sin(Pitch));
        return new ViewCamera(Eye, Eye + direction * 1000, Vector3d.UnitZ, Settings.FieldOfView);
    }

    private static double Normalized(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
}

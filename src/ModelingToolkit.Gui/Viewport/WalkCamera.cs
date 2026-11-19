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

    /// <summary>Steps forward, or back for a negative distance, in millimetres, as the wheel does.</summary>
    public void Step(double distance) => Move(new Vector3d(Math.Cos(Yaw), Math.Sin(Yaw), 0) * distance);

    private void Move(Vector3d move) => Eye += Limit is { } limit ? limit(Eye, move) : move;

    /// <summary>The camera of the view: looking a metre ahead, the vertical up.</summary>
    public ViewCamera ToCamera()
    {
        var direction = new Vector3d(
            Math.Cos(Pitch) * Math.Cos(Yaw), Math.Cos(Pitch) * Math.Sin(Yaw), Math.Sin(Pitch));
        return new ViewCamera(Eye, Eye + direction * 1000, Vector3d.UnitZ, Settings.FieldOfView);
    }

    private static double Normalized(double angle) => Math.IEEERemainder(angle, 2 * Math.PI);
}

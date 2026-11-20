using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

public sealed class WalkCameraTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void Walking_starts_on_the_floor_where_the_orbit_camera_looked_facing_its_way()
    {
        // Looking down at (1000, 2000, 300) from the south-west, above.
        var orbit = new ViewCamera(new Point3d(-4000, -3000, 5000), new Point3d(1000, 2000, 300), Vector3d.UnitZ, 35);

        WalkCamera walk = WalkCamera.Enter(orbit, floor: -50);

        AssertPoint(new Point3d(1000, 2000, -50 + WalkCamera.EyeHeight), walk.Eye);
        Assert.Equal(Math.PI / 4, walk.Yaw, Tolerance);
        Assert.Equal(0, walk.Pitch);
        ViewCamera camera = walk.ToCamera();
        Assert.Equal(WalkCamera.FieldOfView, camera.FieldOfView);
        Assert.Equal(Vector3d.UnitZ, camera.Up);
    }

    [Fact]
    public void A_person_of_other_settings_stands_taller_walks_faster_and_sees_wider()
    {
        var settings = new WalkSettings(
            EyeHeight: 1200, Speed: 700, RunFactor: 3, FieldOfView: 90, LookSensitivity: 0.01, CollisionRadius: 100);
        var orbit = new ViewCamera(new Point3d(0, -3000, 3000), default, Vector3d.UnitZ, 35);

        WalkCamera walk = WalkCamera.Enter(orbit, floor: 0, settings);
        Assert.Equal(1200, walk.Eye.Z);
        Assert.Equal(90, walk.ToCamera().FieldOfView);
        double yaw = walk.Yaw;
        walk.Walk(forward: 1, right: 0, seconds: 1, run: true);
        Assert.Equal(2100, (walk.Eye - new Point3d(0, 0, 1200)).Length, Tolerance);
        walk.Look(right: 10, up: 0);
        Assert.Equal(yaw - 0.1, walk.Yaw, Tolerance);
    }

    [Fact]
    public void Turning_by_the_keys_goes_counterclockwise_for_a_positive_angle_and_leaves_the_place()
    {
        WalkCamera walk = Facing(0);
        Point3d eye = walk.Eye;

        walk.Turn(Math.PI / 2);

        Assert.Equal(Math.PI / 2, walk.Yaw, Tolerance);
        AssertPoint(eye, walk.Eye);
    }

    [Fact]
    public void With_gravity_the_eyes_fall_to_the_ground_rise_over_a_low_step_and_keep_their_height_over_nothing()
    {
        // A ground at 0 up to X = 1000, then at -2000, with a step of 200 at X = 2000.
        double? Ground(double x, double y, double from) => x switch
        {
            < 1000 => from >= 0 ? 0 : null,
            < 2000 => from >= -2000 ? -2000 : null,
            _ => from >= 200 ? 200 : null,
        };
        // Put at 600 above the ground.
        WalkCamera walk = At(0, 600);
        walk.Ground = Ground;
        Assert.True(walk.IsFalling);

        Fall(walk);
        Assert.Equal(WalkCamera.EyeHeight, walk.Eye.Z, 1e-6);
        Assert.False(walk.IsFalling);

        // Over the edge: down, and within a second or so.
        walk = At(950, 0);
        walk.Ground = Ground;
        Fall(walk);
        walk.Walk(forward: 1, right: 0, seconds: 0.2, run: false); // over the edge at 1000
        Assert.True(walk.IsFalling);
        int frames = Fall(walk);
        Assert.Equal(-2000 + WalkCamera.EyeHeight, walk.Eye.Z, 1e-6);
        Assert.InRange(frames, 20, 120);

        // Up a step of 200, which is within the 400 that are climbed.
        walk = At(2100, 0);
        walk.Ground = Ground;
        Fall(walk);
        Assert.Equal(200 + WalkCamera.EyeHeight, walk.Eye.Z, 1e-6);

        // Without gravity the eyes stay, in the air.
        walk = At(0, 3000);
        walk.Ground = Ground;
        walk.Settings = walk.Settings with { Gravity = false };
        Assert.False(walk.Gravitate(1));
        Assert.Equal(3000 + WalkCamera.EyeHeight, walk.Eye.Z, 1e-6);
    }

    // Gravitates by frames until on the ground; how many it took.
    private static int Fall(WalkCamera walk)
    {
        int frames = 0;
        while (walk.Gravitate(1 / 60.0) && frames < 600)
        {
            frames++;
        }
        return frames;
    }

    // Eyes at their height above a point at X and a height, facing along X.
    private static WalkCamera At(double x, double z) => WalkCamera.Enter(
        new ViewCamera(new Point3d(-1000, 0, 1000), new Point3d(0, 0, 1000), Vector3d.UnitZ, 35), 0, at: new Point3d(x, 0, z));

    [Fact]
    public void Looking_straight_down_faces_the_top_of_the_screen()
    {
        var top = new ViewCamera(new Point3d(0, 0, 10000), default, Vector3d.UnitY, 35);

        Assert.Equal(Math.PI / 2, WalkCamera.Enter(top, 0).Yaw, Tolerance);
    }

    [Fact]
    public void Walking_stays_on_the_floor_whatever_the_camera_looks_at()
    {
        WalkCamera walk = Facing(0); // along X
        walk.Look(0, 200); // up

        walk.Walk(forward: 1, right: 0, seconds: 1, run: false);
        AssertPoint(new Point3d(WalkCamera.Speed, 0, WalkCamera.EyeHeight), walk.Eye);

        walk.Walk(forward: 0, right: 1, seconds: 1, run: true);
        AssertPoint(new Point3d(WalkCamera.Speed, -WalkCamera.Speed * WalkCamera.RunFactor, WalkCamera.EyeHeight), walk.Eye);
    }

    [Fact]
    public void Diagonals_are_no_faster_and_the_wheel_steps_ahead()
    {
        WalkCamera walk = Facing(0);
        walk.Walk(1, 1, 1, false);
        double moved = Math.Sqrt(walk.Eye.X * walk.Eye.X + walk.Eye.Y * walk.Eye.Y);
        Assert.Equal(WalkCamera.Speed, moved, Tolerance);

        WalkCamera stepping = Facing(Math.PI / 2);
        stepping.Step(-500);
        AssertPoint(new Point3d(0, -500, WalkCamera.EyeHeight), stepping.Eye);
    }

    [Fact]
    public void A_drag_to_the_right_turns_right_and_looking_up_stops_short_of_the_vertical()
    {
        WalkCamera walk = Facing(0);

        walk.Look(right: 100, up: 0);
        Assert.True(walk.Yaw < 0);

        walk.Look(0, 100000);
        Assert.True(walk.Pitch < Math.PI / 2);
        ViewCamera camera = walk.ToCamera();
        Assert.True(camera.Target.Z > camera.Eye.Z);
    }

    private static WalkCamera Facing(double yaw) => WalkCamera.Enter(
        new ViewCamera(new Point3d(-Math.Cos(yaw), -Math.Sin(yaw), 0), default, Vector3d.UnitZ, 35), floor: 0);

    private static void AssertPoint(Point3d expected, Point3d actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }
}

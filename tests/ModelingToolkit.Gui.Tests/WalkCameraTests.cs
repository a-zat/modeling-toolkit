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

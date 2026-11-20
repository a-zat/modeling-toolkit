using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Core.Model;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

public sealed class WalkColliderTests
{
    private const double Tolerance = 1e-6;

    // The eyes at their height above a floor at 0, at the origin.
    private static readonly Point3d Eye = new(0, 0, WalkCamera.EyeHeight);

    [Fact]
    public void A_walk_into_a_wall_stops_short_of_it()
    {
        WalkCollider collider = Of(Wall(x: 1000));

        Vector3d move = collider.Limit(Eye, new Vector3d(2000, 0, 0));

        Assert.Equal(1000 - WalkCollider.Radius, move.X, Tolerance);
        Assert.Equal(0, move.Y, Tolerance);
    }

    [Fact]
    public void A_walk_aslant_into_a_wall_slides_along_it()
    {
        WalkCollider collider = Of(Wall(x: 1000));

        Vector3d move = collider.Limit(Eye, new Vector3d(2000, 1000, 0));

        Assert.Equal(1000 - WalkCollider.Radius, move.X, Tolerance);
        Assert.Equal(1000, move.Y, Tolerance);
    }

    [Fact]
    public void Walks_away_from_a_wall_or_over_a_threshold_go_through()
    {
        WalkCollider collider = Of(Wall(x: 1000), Wall(x: 500, height: 200), Wall(x: 400, bottom: 2500));

        Assert.Equal(new Vector3d(-2000, 0, 0), collider.Limit(Eye, new Vector3d(-2000, 0, 0)));
        // A low threshold is stepped over, and what is above the head passed under: the wall beyond stops the walk.
        Assert.Equal(1000 - WalkCollider.Radius, collider.Limit(Eye, new Vector3d(1500, 0, 0)).X, Tolerance);
    }

    [Fact]
    public void The_distance_kept_follows_the_radius_given()
    {
        var collider = new WalkCollider(
            [(Wall(x: 1000), Transform.Identity)], radius: 500, eyeHeight: WalkCamera.EyeHeight);

        Assert.Equal(500, collider.Limit(Eye, new Vector3d(2000, 0, 0)).X, Tolerance);
    }

    [Fact]
    public void Walking_with_a_collider_stops_the_camera_at_the_wall()
    {
        WalkCollider collider = Of(Wall(x: 1000));
        WalkCamera walk = WalkCamera.Enter(new ViewCamera(new Point3d(-1000, 0, 1000), default, Vector3d.UnitZ, 35), 0);
        walk.Limit = collider.Limit;

        walk.Walk(forward: 1, right: 0, seconds: 5, run: true);

        Assert.Equal(1000 - WalkCollider.Radius, walk.Eye.X, Tolerance);
    }

    [Fact]
    public void Put_on_a_point_too_near_a_wall_the_eyes_move_out_to_the_radius_and_can_walk_away_or_along()
    {
        WalkCollider collider = Of(Wall(x: 1000));

        Point3d cleared = collider.Clear(new Point3d(900, 0, WalkCamera.EyeHeight));

        Assert.True(cleared.X <= 1000 - WalkCollider.Radius + 20, $"stands at {cleared.X}");
        Assert.True(cleared.X >= 1000 - WalkCollider.Radius - 120, $"stands at {cleared.X}");
        Assert.Equal(0, cleared.Y, 1);
        // Far from everything, it stays.
        Assert.Equal(Eye, collider.Clear(Eye));
    }

    [Fact]
    public void The_ground_is_the_highest_surface_under_the_feet_and_a_step_they_can_climb()
    {
        // A floor at 0 and a ledge at 1000, which is above the feet and the step of 400.
        WalkCollider collider = Of(Floor(0, 0, 10000), Floor(1000, 2000, 3000));

        Assert.Equal(0, collider.Ground(0, 0, 400));
        Assert.Equal(0, collider.Ground(2500, 0, 400)); // the ledge is higher than the step
        Assert.Equal(1000, collider.Ground(2500, 0, 1400));
        Assert.Null(collider.Ground(0, 0, -100)); // nothing under what is below the floor
    }

    private static MeshEntity Floor(double z, double from, double to) => new(
        [new(from, -5000, z), new(to, -5000, z), new(to, 5000, z), new(from, 5000, z)], [new(0, 1, 2), new(0, 2, 3)]);

    private static WalkCollider Of(params MeshEntity[] meshes) =>
        new(meshes.Select(mesh => (mesh, Transform.Identity)));

    // A wall across the X axis, square to it, 10 m wide, from a height to another.
    private static MeshEntity Wall(double x, double bottom = 0, double height = 3000) => new(
        [new(x, -5000, bottom), new(x, 5000, bottom), new(x, 5000, height), new(x, -5000, height)],
        [new(0, 1, 2), new(0, 2, 3)]);
}

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
    public void Walking_with_a_collider_stops_the_camera_at_the_wall()
    {
        WalkCollider collider = Of(Wall(x: 1000));
        WalkCamera walk = WalkCamera.Enter(new ViewCamera(new Point3d(-1000, 0, 1000), default, Vector3d.UnitZ, 35), 0);
        walk.Limit = collider.Limit;

        walk.Walk(forward: 1, right: 0, seconds: 5, run: true);

        Assert.Equal(1000 - WalkCollider.Radius, walk.Eye.X, Tolerance);
    }

    private static WalkCollider Of(params MeshEntity[] meshes) =>
        new(meshes.Select(mesh => (mesh, Transform.Identity)));

    // A wall across the X axis, square to it, 10 m wide, from a height to another.
    private static MeshEntity Wall(double x, double bottom = 0, double height = 3000) => new(
        [new(x, -5000, bottom), new(x, 5000, bottom), new(x, 5000, height), new(x, -5000, height)],
        [new(0, 1, 2), new(0, 2, 3)]);
}

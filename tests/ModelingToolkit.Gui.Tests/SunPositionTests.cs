using ModelingToolkit.Core.Geometry;
using ModelingToolkit.Gui.Viewport;

namespace ModelingToolkit.Gui.Tests;

public sealed class SunPositionTests
{
    private static readonly DateOnly Equinox = new(2026, 3, 20);
    private static readonly DateOnly Midsummer = new(2026, 6, 21);

    [Fact]
    public void At_noon_the_sun_is_south_as_high_as_the_complement_of_the_latitude_at_the_equinox()
    {
        Vector3d sun = SunPosition.Direction(12, Equinox, latitude: 45, northAngle: 0);

        Assert.Equal(0, sun.X, 0.02);
        Assert.True(sun.Y < 0); // south, the Y axis being north
        Assert.Equal(45, Degrees(Math.Asin(sun.Z)), 1.5);
    }

    [Fact]
    public void The_sun_rises_in_the_east_and_is_below_the_horizon_at_night()
    {
        Vector3d morning = SunPosition.Direction(8, Equinox, 45, 0);
        Vector3d evening = SunPosition.Direction(16, Equinox, 45, 0);
        Vector3d night = SunPosition.Direction(0, Equinox, 45, 0);

        Assert.True(morning.X > 0 && morning.Z > 0);
        Assert.True(evening.X < 0 && evening.Z > 0);
        Assert.True(night.Z < 0);
        Assert.Equal(1, morning.Length, 1e-9);
    }

    [Fact]
    public void North_turns_the_sun_with_it_and_summer_raises_it()
    {
        // North along X: the noon sun, to the south, is along -X.
        Vector3d turned = SunPosition.Direction(12, Equinox, 45, northAngle: -90);
        Assert.True(turned.X < -0.5);
        Assert.Equal(0, turned.Y, 0.02);

        double summer = Degrees(Math.Asin(SunPosition.Direction(12, Midsummer, 45, 0).Z));
        Assert.Equal(45 + 23.44, summer, 1.5);
    }

    private static double Degrees(double radians) => radians * 180 / Math.PI;
}

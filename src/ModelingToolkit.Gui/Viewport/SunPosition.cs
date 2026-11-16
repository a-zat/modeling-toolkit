using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// Where the sun is in the sky of a place, at an hour of a day, by the usual approximations of the solar declination
/// and of the hour angle, the hour being the solar time: precise to a degree or two, enough to see the light of a room.
/// </summary>
public static class SunPosition
{
    /// <summary>
    /// The direction towards the sun in the coordinates of the model, Z up, north the Y axis turned counterclockwise by
    /// <paramref name="northAngle"/> degrees; below the horizon at night.
    /// </summary>
    /// <param name="hour">From 0 to 24.</param>
    /// <param name="latitude">In degrees, north positive.</param>
    public static Vector3d Direction(double hour, DateOnly day, double latitude, double northAngle)
    {
        double declination = Radians(23.44) * Math.Sin(2 * Math.PI * (284 + day.DayOfYear) / 365.0);
        double hourAngle = Radians(15 * (hour - 12));
        double phi = Radians(latitude);

        // East, north and up, from the hour angle (west of the meridian when positive) and the declination.
        double east = -Math.Cos(declination) * Math.Sin(hourAngle);
        double north = Math.Cos(phi) * Math.Sin(declination) - Math.Sin(phi) * Math.Cos(declination) * Math.Cos(hourAngle);
        double up = Math.Sin(phi) * Math.Sin(declination) + Math.Cos(phi) * Math.Cos(declination) * Math.Cos(hourAngle);

        // North is the Y axis turned by the north angle, east a quarter turn clockwise from it.
        double angle = Radians(northAngle);
        var northAxis = new Vector3d(-Math.Sin(angle), Math.Cos(angle), 0);
        var eastAxis = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
        return (eastAxis * east + northAxis * north + Vector3d.UnitZ * up).Normalized();
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

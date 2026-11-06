namespace ModelingToolkit.Core.Geometry;

/// <summary>
/// Polylines stored one after the other: polyline i is made of the points from <c>FirstPoints[i]</c> to
/// <c>FirstPoints[i + 1] - 1</c>, at least two.
/// </summary>
public sealed class Polylines
{
    private readonly Point3d[] _points;
    private readonly int[] _firstPoints;

    /// <param name="firstPoints">
    /// The first point of each polyline, then the number of points: one more value than there are polylines.
    /// </param>
    /// <exception cref="ArgumentException">A polyline has fewer than two points, or the indices are out of range.</exception>
    public Polylines(Point3d[] points, int[] firstPoints)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(firstPoints);
        if (firstPoints.Length == 0 || firstPoints[0] != 0 || firstPoints[^1] != points.Length)
        {
            throw new ArgumentException(
                "The first points must start at 0 and end with the number of points.", nameof(firstPoints));
        }
        for (int index = 1; index < firstPoints.Length; index++)
        {
            if (firstPoints[index] - firstPoints[index - 1] < 2)
            {
                throw new ArgumentException("Each polyline needs at least two points.", nameof(firstPoints));
            }
        }

        _points = points;
        _firstPoints = firstPoints;
    }

    public static Polylines Empty { get; } = new([], [0]);

    public int Count => _firstPoints.Length - 1;

    /// <summary>The points of all the polylines, one polyline after the other.</summary>
    public ReadOnlySpan<Point3d> Points => _points;

    /// <summary>The first point of each polyline, then the number of points.</summary>
    public ReadOnlySpan<int> FirstPoints => _firstPoints;

    public ReadOnlySpan<Point3d> this[int index] =>
        _points.AsSpan(_firstPoints[index], _firstPoints[index + 1] - _firstPoints[index]);
}

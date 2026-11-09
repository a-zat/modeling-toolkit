namespace ModelingToolkit.Gui.Viewport;

/// <summary>
/// The rectangle of a window selection in the view, in device-independent units. Points at NaN, behind the camera, are
/// never in it.
/// </summary>
internal readonly record struct WindowArea(double Left, double Top, double Right, double Bottom)
{
    public static WindowArea Between(Avalonia.Point a, Avalonia.Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public bool Holds((double X, double Y) point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public bool Holds(((double X, double Y) A, (double X, double Y) B, (double X, double Y) C) triangle) =>
        Holds(triangle.A) && Holds(triangle.B) && Holds(triangle.C);

    /// <summary>Whether a segment has a point in the rectangle.</summary>
    public bool Touches((double X, double Y) a, (double X, double Y) b)
    {
        if (double.IsNaN(a.X + a.Y + b.X + b.Y))
        {
            return false;
        }
        // Liang–Barsky: the part of the segment within each side of the rectangle.
        double dx = b.X - a.X, dy = b.Y - a.Y, enter = 0, leave = 1;
        foreach ((double p, double q) in new[] { (-dx, a.X - Left), (dx, Right - a.X), (-dy, a.Y - Top), (dy, Bottom - a.Y) })
        {
            if (p == 0)
            {
                if (q < 0)
                {
                    return false;
                }
                continue;
            }
            double r = q / p;
            if (p < 0)
            {
                enter = Math.Max(enter, r);
            }
            else
            {
                leave = Math.Min(leave, r);
            }
        }
        return enter <= leave;
    }

    /// <summary>Whether a triangle and the rectangle overlap.</summary>
    public bool Touches(((double X, double Y) A, (double X, double Y) B, (double X, double Y) C) triangle)
    {
        (var a, var b, var c) = triangle;
        if (double.IsNaN(a.X + a.Y + b.X + b.Y + c.X + c.Y))
        {
            return false;
        }
        if (Touches(a, b) || Touches(b, c) || Touches(c, a))
        {
            return true;
        }
        // The rectangle within the triangle.
        (double X, double Y) centre = ((Left + Right) / 2, (Top + Bottom) / 2);
        double Side((double X, double Y) p, (double X, double Y) q) => (q.X - p.X) * (centre.Y - p.Y) - (q.Y - p.Y) * (centre.X - p.X);
        double ab = Side(a, b), bc = Side(b, c), ca = Side(c, a);
        return (ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0);
    }
}

/// <summary>The faces and edges a window selected, to select in place of the others or, with Shift, after them.</summary>
public sealed record WindowSelection(IReadOnlyList<Core.Model.ElementReference> Elements, bool Add);

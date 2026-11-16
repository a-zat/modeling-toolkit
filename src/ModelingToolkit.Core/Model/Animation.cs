using ModelingToolkit.Core.Geometry;

namespace ModelingToolkit.Core.Model;

/// <summary>The part of the transformation of a block reference that an <see cref="AnimationChannel"/> moves.</summary>
public enum AnimatedProperty
{
    /// <summary>Offsets, in millimetres, along the axes of the referenced block.</summary>
    Translation,

    /// <summary>Unit quaternions (x, y, z, w) turning the referenced block around its origin.</summary>
    Rotation,

    /// <summary>Factors along the axes of the referenced block, from its origin.</summary>
    Scale,
}

/// <summary>How the values of an <see cref="AnimationChannel"/> go from one key to the next, as in glTF.</summary>
public enum AnimationInterpolation
{
    /// <summary>Each value holds until the next key.</summary>
    Step,

    /// <summary>In a straight line; rotations along the shortest arc.</summary>
    Linear,

    /// <summary>A cubic Hermite spline: each key has an in-tangent, a value and an out-tangent, in that order.</summary>
    CubicSpline,
}

/// <summary>
/// How one property of a block reference changes in time: values at keys, relative to the reference's own
/// <see cref="BlockReference.Transform"/>, so that the motion follows the reference wherever it is moved.
/// </summary>
/// <remarks>
/// The motion is in the coordinates of the referenced block: the reference is placed by its transformation applied to
/// the motion, <c>Transform * motion</c>. Before the first key and after the last, the values of those keys hold.
/// </remarks>
public sealed class AnimationChannel
{
    private readonly double[] _times;
    private readonly double[] _values;

    /// <param name="times">The times of the keys, in seconds, increasing.</param>
    /// <param name="values">
    /// For each key, the components of its value (4 for rotations, 3 otherwise), three times for cubic splines.
    /// </param>
    public AnimationChannel(BlockReference target, AnimatedProperty property, AnimationInterpolation interpolation,
        IReadOnlyList<double> times, IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(values);
        if (!Enum.IsDefined(property))
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }
        if (!Enum.IsDefined(interpolation))
        {
            throw new ArgumentOutOfRangeException(nameof(interpolation));
        }
        if (times.Count == 0)
        {
            throw new ArgumentException("A channel needs at least one key.", nameof(times));
        }
        for (int key = 0; key < times.Count; key++)
        {
            if (!double.IsFinite(times[key]) || (key > 0 && times[key] <= times[key - 1]))
            {
                throw new ArgumentException("The times of the keys must be finite and increasing.", nameof(times));
            }
        }

        int components = property == AnimatedProperty.Rotation ? 4 : 3;
        int perKey = interpolation == AnimationInterpolation.CubicSpline ? 3 * components : components;
        if (values.Count != times.Count * perKey)
        {
            throw new ArgumentException(
                $"{times.Count} keys need {times.Count * perKey} values, not {values.Count}.", nameof(values));
        }
        if (values.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("The values must be finite.", nameof(values));
        }

        Target = target;
        Property = property;
        Interpolation = interpolation;
        _times = [.. times];
        _values = [.. values];
    }

    public BlockReference Target { get; }

    public AnimatedProperty Property { get; }

    public AnimationInterpolation Interpolation { get; }

    public IReadOnlyList<double> Times => _times;

    public IReadOnlyList<double> Values => _values;

    /// <summary>The time of the last key, in seconds.</summary>
    public double EndTime => _times[^1];

    private int Components => Property == AnimatedProperty.Rotation ? 4 : 3;

    /// <summary>The motion at a time, in the coordinates of the referenced block.</summary>
    public Transform MotionAt(double time)
    {
        double[] value = Sample(time);
        return Property switch
        {
            AnimatedProperty.Translation => Transform.Translation(value[0], value[1], value[2]),
            AnimatedProperty.Rotation => Quaternions.ToTransform(value),
            _ => new Transform(value[0], 0, 0, 0, 0, value[1], 0, 0, 0, 0, value[2], 0),
        };
    }

    private double[] Sample(double time)
    {
        int components = Components;
        if (double.IsNaN(time) || time <= _times[0] || _times.Length == 1)
        {
            return ValueOf(0);
        }
        if (time >= _times[^1])
        {
            return ValueOf(_times.Length - 1);
        }

        int next = Array.BinarySearch(_times, time);
        if (next >= 0)
        {
            return ValueOf(next);
        }
        next = ~next;
        int key = next - 1;
        double span = _times[next] - _times[key];
        double s = (time - _times[key]) / span;

        double[] result = new double[components];
        switch (Interpolation)
        {
            case AnimationInterpolation.Step:
                return ValueOf(key);
            case AnimationInterpolation.Linear when Property == AnimatedProperty.Rotation:
                return Quaternions.Slerp(ValueOf(key), ValueOf(next), s);
            case AnimationInterpolation.Linear:
                double[] a = ValueOf(key), b = ValueOf(next);
                for (int c = 0; c < components; c++)
                {
                    result[c] = a[c] + (b[c] - a[c]) * s;
                }
                return result;
            default:
                // glTF: p(s) = (2s³ - 3s² + 1) v₀ + t (s³ - 2s² + s) b₀ + (-2s³ + 3s²) v₁ + t (s³ - s²) a₁, where b₀ is
                // the out-tangent of the first key and a₁ the in-tangent of the second.
                double s2 = s * s, s3 = s2 * s;
                for (int c = 0; c < components; c++)
                {
                    double v0 = _values[(key * 3 + 1) * components + c];
                    double b0 = _values[(key * 3 + 2) * components + c];
                    double v1 = _values[(next * 3 + 1) * components + c];
                    double a1 = _values[(next * 3) * components + c];
                    result[c] = (2 * s3 - 3 * s2 + 1) * v0 + span * (s3 - 2 * s2 + s) * b0 + (-2 * s3 + 3 * s2) * v1 +
                                span * (s3 - s2) * a1;
                }
                return Property == AnimatedProperty.Rotation ? Quaternions.Normalized(result) : result;
        }
    }

    // The value of a key, without the tangents of cubic splines.
    private double[] ValueOf(int key)
    {
        int components = Components;
        int start = Interpolation == AnimationInterpolation.CubicSpline ? (key * 3 + 1) * components : key * components;
        double[] value = _values.AsSpan(start, components).ToArray();
        return Property == AnimatedProperty.Rotation ? Quaternions.Normalized(value) : value;
    }
}

/// <summary>
/// A named motion of parts of a document, such as a door that opens: channels that move block references in time.
/// Playing it changes nothing in the document: <see cref="PoseAt"/> gives the transformations to show.
/// </summary>
public sealed class Animation
{
    public Animation(string name, IEnumerable<AnimationChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(channels);
        Name = name;
        Channels = [.. channels];
        if (Channels.Count == 0)
        {
            throw new ArgumentException("An animation needs at least one channel.", nameof(channels));
        }
    }

    public string Name { get; set; }

    public IReadOnlyList<AnimationChannel> Channels { get; }

    /// <summary>The time of the last key of its channels, in seconds.</summary>
    public double Duration => Channels.Max(channel => channel.EndTime);

    /// <summary>The block references it moves, once each.</summary>
    public IEnumerable<BlockReference> Targets => Channels.Select(channel => channel.Target).Distinct();

    /// <summary>
    /// The transformation of each block reference it moves, at a time in seconds: the reference's transformation
    /// followed by the motion of its channels, translation, rotation and scale applied in the reverse order.
    /// </summary>
    public IReadOnlyDictionary<BlockReference, Transform> PoseAt(double time)
    {
        var pose = new Dictionary<BlockReference, Transform>(ReferenceEqualityComparer.Instance);
        foreach (IGrouping<BlockReference, AnimationChannel> target in Channels.GroupBy(channel => channel.Target))
        {
            Transform translation = Transform.Identity, rotation = Transform.Identity, scale = Transform.Identity;
            foreach (AnimationChannel channel in target)
            {
                Transform motion = channel.MotionAt(time);
                switch (channel.Property)
                {
                    case AnimatedProperty.Translation:
                        translation = motion;
                        break;
                    case AnimatedProperty.Rotation:
                        rotation = motion;
                        break;
                    default:
                        scale = motion;
                        break;
                }
            }
            pose[target.Key] = target.Key.Transform * translation * rotation * scale;
        }
        return pose;
    }
}

/// <summary>Unit quaternions as arrays (x, y, z, w), for the rotations of animations.</summary>
public static class Quaternions
{
    public static double[] Normalized(double[] q)
    {
        double length = Math.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
        return length > 0 ? [q[0] / length, q[1] / length, q[2] / length, q[3] / length] : [0, 0, 0, 1];
    }

    /// <summary>The product a·b: the rotation b followed by a.</summary>
    public static double[] Multiply(double[] a, double[] b) =>
    [
        a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
        a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
        a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
        a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2],
    ];

    /// <summary>The inverse of a unit quaternion.</summary>
    public static double[] Conjugate(double[] q) => [-q[0], -q[1], -q[2], q[3]];

    /// <summary>A vector turned by a unit quaternion.</summary>
    public static Vector3d Rotate(double[] q, Vector3d vector) =>
        ToTransform(q).ApplyVector(vector);

    /// <summary>Along the shortest arc from a to b, at a fraction of the way.</summary>
    public static double[] Slerp(double[] a, double[] b, double fraction)
    {
        double dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
        double sign = 1;
        if (dot < 0)
        {
            dot = -dot;
            sign = -1;
        }

        double wa, wb;
        if (dot > 0.9995)
        {
            wa = 1 - fraction;
            wb = fraction;
        }
        else
        {
            double angle = Math.Acos(dot);
            double sine = Math.Sin(angle);
            wa = Math.Sin((1 - fraction) * angle) / sine;
            wb = Math.Sin(fraction * angle) / sine;
        }
        wb *= sign;
        return Normalized([wa * a[0] + wb * b[0], wa * a[1] + wb * b[1], wa * a[2] + wb * b[2], wa * a[3] + wb * b[3]]);
    }

    public static Transform ToTransform(double[] q)
    {
        double x = q[0], y = q[1], z = q[2], w = q[3];
        return new Transform(
            1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 0,
            2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), 0,
            2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), 0);
    }

    /// <summary>The rotation of a transformation without scale or shear, as a unit quaternion.</summary>
    public static double[] FromTransform(Transform t)
    {
        double trace = t.M00 + t.M11 + t.M22;
        double[] q;
        if (trace > 0)
        {
            double s = 0.5 / Math.Sqrt(trace + 1);
            q = [(t.M21 - t.M12) * s, (t.M02 - t.M20) * s, (t.M10 - t.M01) * s, 0.25 / s];
        }
        else if (t.M00 > t.M11 && t.M00 > t.M22)
        {
            double s = 2 * Math.Sqrt(1 + t.M00 - t.M11 - t.M22);
            q = [0.25 * s, (t.M01 + t.M10) / s, (t.M02 + t.M20) / s, (t.M21 - t.M12) / s];
        }
        else if (t.M11 > t.M22)
        {
            double s = 2 * Math.Sqrt(1 + t.M11 - t.M00 - t.M22);
            q = [(t.M01 + t.M10) / s, 0.25 * s, (t.M12 + t.M21) / s, (t.M02 - t.M20) / s];
        }
        else
        {
            double s = 2 * Math.Sqrt(1 + t.M22 - t.M00 - t.M11);
            q = [(t.M02 + t.M20) / s, (t.M12 + t.M21) / s, 0.25 * s, (t.M10 - t.M01) / s];
        }
        return Normalized(q);
    }
}

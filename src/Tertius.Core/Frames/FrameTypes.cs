using System.Numerics;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>What moves a reference frame.</summary>
public enum MotionDriver : byte
{
    /// <summary>The pose never changes.</summary>
    Static,
    /// <summary>The pose is a function of time: a Kepler orbit around the parent, plus a spin.</summary>
    Kepler,
    /// <summary>The frame's hull is a dynamic body in the parent frame's physics system.</summary>
    Physics,
    /// <summary>
    /// The frame stays at its place in its parent and turns in place, as the ground of a planet does: its rotation is a
    /// function of time, set by its <see cref="FrameSpin"/>.
    /// </summary>
    Spin,
}

/// <summary>
/// The bubble of a frame: an ellipsoid with semi-axes <see cref="Radii"/> along the frame's axes.
/// A sphere has three equal radii. The bubble decides which frame owns an object.
/// </summary>
public record struct FrameBounds(Vector3 Radii)
{
    public static FrameBounds Sphere(float radius) => new(new Vector3(radius));

    public readonly float MaxRadius => MathF.Max(Radii.X, MathF.Max(Radii.Y, Radii.Z));
    public readonly float MinRadius => MathF.Min(Radii.X, MathF.Min(Radii.Y, Radii.Z));
    public readonly bool IsSphere => Radii.X == Radii.Y && Radii.Y == Radii.Z;

    /// <summary>
    /// The distance from the bubble surface to a point in frame coordinates: negative inside. For a sphere it
    /// is exact. For an ellipsoid it is the distance along the ray from the center, which is close enough for
    /// hand-off decisions.
    /// </summary>
    public readonly double SignedDistance(Double3 point)
    {
        double length = point.Length;
        if (IsSphere)
            return length - Radii.X;
        var q = point / (Double3)Radii;
        double k = q.Length;
        if (k == 0)
            return -MinRadius;
        return length * (k - 1) / k;
    }

    /// <summary>True when a sphere at <paramref name="center"/> touches or overlaps the bubble.</summary>
    public readonly bool Touches(Double3 center, double radius) => SignedDistance(center) <= radius;

    /// <summary>True when a sphere is fully outside the bubble by more than <paramref name="margin"/>.</summary>
    public readonly bool FullyOutside(Double3 center, double radius, double margin) => SignedDistance(center) > radius + margin;

    /// <summary>The hand-off margin for an object of the given radius: the larger of 2% of the bubble and the radius.</summary>
    public readonly double ExitMargin(double objectRadius) => Math.Max(0.02 * MaxRadius, objectRadius);

    /// <summary>True when <paramref name="inner"/> fits inside <paramref name="outer"/>, so that it can enter it as a child.</summary>
    public static bool Fits(in FrameBounds inner, in FrameBounds outer) => inner.MaxRadius < 0.5f * outer.MinRadius;

    /// <summary>
    /// Where the line <paramref name="origin"/> + t · <paramref name="direction"/>, in frame coordinates, crosses the
    /// bubble: it is inside for every t from <paramref name="entry"/> to <paramref name="exit"/>, and outside for every
    /// other t. The parameter t counts in lengths of <paramref name="direction"/>, so for a unit direction it is the
    /// distance from <paramref name="origin"/> in meters, and it is negative behind the origin. False, with both values 0,
    /// when the line passes beside the bubble or <paramref name="direction"/> is zero.
    /// </summary>
    public readonly bool TryIntersectLine(Double3 origin, Double3 direction, out double entry, out double exit)
    {
        // Scaled by the radii, the ellipsoid is the unit sphere: |o + t·d|² = 1, or a·t² + b·t + c = 0.
        var radii = (Double3)Radii;
        Double3 o = origin / radii, d = direction / radii;
        double a = Double3.Dot(d, d), b = 2 * Double3.Dot(o, d), c = Double3.Dot(o, o) - 1;
        double discriminant = b * b - 4 * a * c;
        if (!(a > 0) || discriminant < 0)
        {
            entry = exit = 0;
            return false;
        }
        // The form that never subtracts two nearly equal numbers: q has the sign of b, and the roots are q/a and c/q.
        double q = -0.5 * (b + (b >= 0 ? Math.Sqrt(discriminant) : -Math.Sqrt(discriminant)));
        double t0 = q / a, t1 = q != 0 ? c / q : -t0;
        entry = Math.Min(t0, t1);
        exit = Math.Max(t0, t1);
        return true;
    }
}

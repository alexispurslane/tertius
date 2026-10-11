using System.Numerics;

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
}

public enum GravityKind : byte
{
    None,
    /// <summary>Artificial gravity: one acceleration vector, optionally only inside a region box.</summary>
    Uniform,
    /// <summary>A point mass at the frame origin, such as a planet.</summary>
    Point,
}

/// <summary>The gravity field of one frame. An object feels only the field of the frame that it is in.</summary>
public record struct GravityField(GravityKind Kind, Vector3 Acceleration, BoundingBox? Region, double Mu)
{
    public static GravityField None => default;

    public static GravityField Uniform(Vector3 acceleration, BoundingBox? region = null) =>
        new(GravityKind.Uniform, acceleration, region, 0);

    /// <param name="mu">The standard gravitational parameter G·M, in m³/s².</param>
    public static GravityField Point(double mu) => new(GravityKind.Point, Vector3.Zero, null, mu);

    /// <summary>
    /// True for a field that physics can apply: a kind that <see cref="GravityKind"/> defines; for a uniform field, a
    /// finite acceleration, and finite corners of the region when it has one; for a point mass, a finite G·M that is not
    /// negative. Any other field would give the bodies of the frame a force that is not finite.
    /// </summary>
    public readonly bool IsValid => Kind switch
    {
        GravityKind.None => true,
        GravityKind.Uniform => IsFinite(Acceleration) && (Region is not { } region || (IsFinite(region.Min) && IsFinite(region.Max))),
        GravityKind.Point => double.IsFinite(Mu) && Mu >= 0,
        _ => false,
    };

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>The acceleration at a point in frame coordinates.</summary>
    public readonly Vector3 At(Double3 position)
    {
        switch (Kind)
        {
            case GravityKind.Uniform:
                return Region is { } region && !region.Contains(position.ToVector3()) ? Vector3.Zero : Acceleration;
            case GravityKind.Point:
                double r2 = position.LengthSquared;
                if (r2 < 1e-6)
                    return Vector3.Zero;
                double r = Math.Sqrt(r2);
                return (position * (-Mu / (r2 * r))).ToVector3();
            default:
                return Vector3.Zero;
        }
    }
}

/// <summary>
/// Kepler elements of a frame around its parent, plus the frame's spin. Angles are radians, distances meters,
/// times seconds. The reference plane is the parent's XZ plane, with +Y as the orbit normal at zero inclination.
/// </summary>
public record struct KeplerOrbit(
    double SemiMajorAxis,
    double Eccentricity,
    double Inclination,
    double LongitudeOfAscendingNode,
    double ArgumentOfPeriapsis,
    double MeanAnomalyAtEpoch,
    double Epoch,
    double ParentMu,
    DQuaternion AxialTilt,
    double SpinRate,
    double SpinPhaseAtEpoch)
{
    /// <summary>A circular orbit in the reference plane.</summary>
    public static KeplerOrbit Circular(double radius, double parentMu, double phase = 0, double spinRate = 0) =>
        new(radius, 0, 0, 0, 0, phase, 0, parentMu, DQuaternion.Identity, spinRate, 0);

    /// <summary>
    /// True for an orbit that the Kepler driver can follow: a closed orbit, with an eccentricity from 0 up to but not
    /// including 1, a positive semi-major axis and a parent with a positive G·M, and every element, the tilt and the spin
    /// finite. The default orbit is not valid: it would leave the frame at its parent's origin.
    /// </summary>
    public readonly bool IsValid =>
        SemiMajorAxis > 0 && double.IsFinite(SemiMajorAxis) && ParentMu > 0 && double.IsFinite(ParentMu) &&
        Eccentricity >= 0 && Eccentricity < 1 &&
        double.IsFinite(Inclination) && double.IsFinite(LongitudeOfAscendingNode) && double.IsFinite(ArgumentOfPeriapsis) &&
        double.IsFinite(MeanAnomalyAtEpoch) && double.IsFinite(Epoch) && double.IsFinite(SpinRate) && double.IsFinite(SpinPhaseAtEpoch) &&
        double.IsFinite(AxialTilt.X) && double.IsFinite(AxialTilt.Y) && double.IsFinite(AxialTilt.Z) && double.IsFinite(AxialTilt.W);

    public readonly double MeanMotion => SemiMajorAxis > 0 ? Math.Sqrt(ParentMu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis)) : 0;

    public readonly double Period => MeanMotion > 0 ? 2 * Math.PI / MeanMotion : double.PositiveInfinity;

    /// <summary>Position and velocity relative to the parent, in parent axes, at <paramref name="time"/>.</summary>
    public readonly (Double3 Position, Double3 Velocity) StateAt(double time)
    {
        if (SemiMajorAxis <= 0 || ParentMu <= 0)
            return (Double3.Zero, Double3.Zero);

        double e = Eccentricity;
        double n = MeanMotion;
        double m = MeanAnomalyAtEpoch + n * (time - Epoch);
        m = Math.IEEERemainder(m, 2 * Math.PI);

        // Solve Kepler's equation M = E - e sin E with Newton iterations.
        double eccentric = e < 0.8 ? m : Math.PI;
        for (int i = 0; i < 30; i++)
        {
            double f = eccentric - e * Math.Sin(eccentric) - m;
            double step = f / (1 - e * Math.Cos(eccentric));
            eccentric -= step;
            if (Math.Abs(step) < 1e-15)
                break;
        }

        double cosE = Math.Cos(eccentric), sinE = Math.Sin(eccentric);
        double a = SemiMajorAxis;
        double b = a * Math.Sqrt(1 - e * e);

        // Perifocal frame: P toward periapsis, Q 90 degrees ahead in the direction of motion.
        double px = a * (cosE - e), qy = b * sinE;
        double edot = n / (1 - e * cosE);
        double vpx = -a * sinE * edot, vqy = b * cosE * edot;

        DQuaternion orient = PerifocalToParent();
        // Perifocal axes in a Y-up frame: P = +X, Q = -Z, so that +Y is the orbit normal (counterclockwise seen from +Y).
        Double3 position = orient.Rotate(new Double3(px, 0, -qy));
        Double3 velocity = orient.Rotate(new Double3(vpx, 0, -vqy));
        return (position, velocity);
    }

    /// <summary>The frame's orientation relative to the parent at <paramref name="time"/>: axial tilt, then spin about local +Y.</summary>
    public readonly DQuaternion RotationAt(double time)
    {
        double angle = SpinPhaseAtEpoch + SpinRate * (time - Epoch);
        DQuaternion tilt = AxialTilt.LengthSquared == 0 ? DQuaternion.Identity : AxialTilt;
        return tilt * DQuaternion.FromAxisAngle(Double3.UnitY, angle);
    }

    /// <summary>The frame's angular velocity in parent axes.</summary>
    public readonly Vector3 AngularVelocity()
    {
        DQuaternion tilt = AxialTilt.LengthSquared == 0 ? DQuaternion.Identity : AxialTilt;
        return (tilt.Rotate(Double3.UnitY) * SpinRate).ToVector3();
    }

    private readonly DQuaternion PerifocalToParent()
    {
        // Rotations about +Y (the reference normal) and +X (the line of nodes), in the order Ω, i, ω.
        DQuaternion node = DQuaternion.FromAxisAngle(Double3.UnitY, LongitudeOfAscendingNode);
        DQuaternion tilt = DQuaternion.FromAxisAngle(Double3.UnitX, Inclination);
        DQuaternion periapsis = DQuaternion.FromAxisAngle(Double3.UnitY, ArgumentOfPeriapsis);
        return node * tilt * periapsis;
    }
}

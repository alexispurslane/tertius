using System.Numerics;

namespace Tertius;

/// <summary>Artificial gravity: one acceleration, everywhere in the frame or only inside a box.</summary>
/// <param name="Acceleration">The acceleration, in m/s², in the axes of the frame.</param>
/// <param name="Region">The box in frame coordinates that the field holds in, or null when it holds everywhere in the frame.</param>
public readonly record struct UniformGravity(Vector3 Acceleration, BoundingBox? Region = null);

/// <summary>A point mass at the frame origin, such as a planet.</summary>
/// <param name="Mu">The standard gravitational parameter G·M, in m³/s².</param>
public readonly record struct PointGravity(double Mu)
{
    /// <summary>
    /// True for a finite G·M above 0. Only such a point mass gives a Kepler frame in its frame a G·M to orbit.
    /// </summary>
    public bool CanBeOrbited => Mu > 0 && double.IsFinite(Mu);
}

/// <summary>
/// The gravity field of one frame: <see cref="UniformGravity"/>, <see cref="PointGravity"/> or neither, which is no
/// gravity. An object feels only the field of the frame that it is in.
/// The union generator writes its union members (docs/plans/union-tooling.md). The empty union is the all-zero default,
/// <see cref="None"/>, and its <c>HasValue</c> is false. A field holds the values of its own case and nothing else, so no
/// field is a uniform field that also has a G·M.
/// </summary>
[GeneratedUnion(typeof(UniformGravity), typeof(PointGravity))]
public readonly partial struct GravityField
{
    /// <summary>No gravity.</summary>
    public static GravityField None => default;

    /// <summary>
    /// True for a field that physics can apply. No gravity is valid. A uniform field is valid with a finite acceleration,
    /// and with finite corners of its region when it has one. A point mass is valid with a finite G·M that is not
    /// negative. Any other field would give the bodies of the frame a force that is not finite.
    /// </summary>
    public bool IsValid => this switch
    {
        UniformGravity { Acceleration: var acceleration, Region: var region } =>
            IsFinite(acceleration) && (region is not { } box || (IsFinite(box.Min) && IsFinite(box.Max))),
        PointGravity { Mu: var mu } => double.IsFinite(mu) && mu >= 0,
        null => true
    };

    /// <summary>
    /// True for a point mass with a finite G·M above 0 (see <see cref="PointGravity.CanBeOrbited"/>). Only such a field
    /// gives a Kepler frame in this frame a G·M to orbit, so the World refuses a Kepler frame under any other field.
    /// </summary>
    public bool CanBeOrbited => this is PointGravity { CanBeOrbited: true };

    /// <summary>A uniform field. Without a <paramref name="region"/>, it holds everywhere in the frame.</summary>
    public static GravityField Uniform(Vector3 acceleration, BoundingBox? region = null) =>
        new(new UniformGravity(acceleration, region));

    /// <param name="mu">The standard gravitational parameter G·M, in m³/s².</param>
    public static GravityField Point(double mu) => new(new PointGravity(mu));

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    /// <summary>The acceleration at a point in frame coordinates.</summary>
    public Vector3 At(Double3 position) => AtPrecise(position).ToVector3();

    /// <summary>
    /// Like <see cref="At"/>, in doubles, for a caller that subtracts another acceleration before it narrows the result.
    /// </summary>
    public Double3 AtPrecise(Double3 position) => this switch
    {
        UniformGravity { Acceleration: var acceleration, Region: var region } =>
            region is { } box && !box.Contains(position.ToVector3()) ? Double3.Zero : acceleration,
        PointGravity { Mu: var mu } => PullAt(position, mu),
        null => Double3.Zero
    };

    /// <summary>The pull of a point mass of gravitational parameter <paramref name="mu"/>, in m³/s², at the origin.</summary>
    private static Double3 PullAt(Double3 position, double mu)
    {
        double r2 = position.LengthSquared;
        if (r2 < 1e-6)
            return Double3.Zero;
        double r = Math.Sqrt(r2);
        return position * (-mu / (r2 * r));
    }
}

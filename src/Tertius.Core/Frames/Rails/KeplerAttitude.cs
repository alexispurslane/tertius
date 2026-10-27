namespace Tertius.Frames.Rails;

/// <summary>
/// The attitude of a Kepler frame that turns as a planet does: it leans by <paramref name="AxialTilt"/>, then it turns
/// about its own +Y, its pole, at <paramref name="Rate"/>. Angles are radians, rates radians per second. The spin angle is
/// <paramref name="PhaseAtEpoch"/> at the orbit's epoch.
/// </summary>
/// <param name="AxialTilt">
/// The rotation from the parent's axes to the frame's axes at a spin angle of 0. Only its direction counts, so it need not be
/// unit length. A zero quaternion means none, as for <see cref="FrameSpin.Tilt"/>.
/// </param>
/// <param name="Rate">How fast the frame turns about its pole, counterclockwise seen from the pole's tip.</param>
/// <param name="PhaseAtEpoch">The spin angle at the orbit's epoch.</param>
public readonly record struct Spin(DQuaternion AxialTilt, double Rate, double PhaseAtEpoch);

/// <summary>
/// The attitude of a Kepler frame that faces its parent, as a station in low orbit does: the belly (-Y) points at the
/// parent, and the nose (-Z) points along the motion across the horizon.
/// </summary>
public readonly record struct FacesParent;

/// <summary>
/// How a Kepler frame turns as it orbits: <see cref="Spin"/> or <see cref="FacesParent"/>. Each case holds only its own
/// values, so a frame that faces its parent has no tilt and no spin rate.
/// The union generator writes its union members (docs/plans/union-tooling.md). The empty union is the all-zero default,
/// and its <c>HasValue</c> is false. It is no attitude, so an orbit with it is not valid (see
/// <see cref="KeplerOrbit.IsValid"/>): every producer of an orbit names the attitude that it means.
/// </summary>
[GeneratedUnion(typeof(Spin), typeof(FacesParent))]
public readonly partial struct KeplerAttitude
{
    /// <summary>True for an attitude that a Kepler frame can follow: a spin whose values are all finite, or a facing.</summary>
    public bool IsValid =>
        this switch
        {
            Spin { AxialTilt: var tilt, Rate: var rate, PhaseAtEpoch: var phase } => double.IsFinite(rate)
                && double.IsFinite(phase)
                && double.IsFinite(tilt.X)
                && double.IsFinite(tilt.Y)
                && double.IsFinite(tilt.Z)
                && double.IsFinite(tilt.W),
            FacesParent => true,
            null => false,
        };
}

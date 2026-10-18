namespace Tertius.Frames.Rails;

/// <summary>How a Kepler frame turns as it orbits.</summary>
public enum KeplerAttitude : byte
{
    /// <summary>The axial tilt, then a steady spin about the frame's own +Y, as a planet turns.</summary>
    Spin,

    /// <summary>
    /// Faces the parent, as a station in low orbit does: the belly (-Y) points at the parent, and the nose (-Z) points
    /// along the motion across the horizon. The tilt and the spin are not used.
    /// </summary>
    FacesParent,
}

/// <summary>
/// Kepler elements of a frame around its parent, plus the frame's attitude. Angles are radians, distances meters,
/// times seconds. The reference plane is the parent's XZ plane, with +Y as the orbit normal at zero inclination.
/// <para>
/// The orbit has no G·M of its own. A <see cref="KeplerRails"/> function that needs the G·M of the parent, in m³/s², takes
/// it as a parameter, called <c>mu</c>. The World and physics take it from the point gravity of the parent frame (see
/// <see cref="FrameTree.KeplerMu"/> and <see cref="KeplerRails.OrbitMu"/>), so a Kepler frame and the loose bodies in its
/// parent fall on the same G·M.
/// </para>
/// </summary>
public record struct KeplerOrbit(
    double SemiMajorAxis,
    double Eccentricity,
    double Inclination,
    double LongitudeOfAscendingNode,
    double ArgumentOfPeriapsis,
    double MeanAnomalyAtEpoch,
    double Epoch,
    DQuaternion AxialTilt,
    double SpinRate,
    double SpinPhaseAtEpoch,
    KeplerAttitude Attitude = KeplerAttitude.Spin)
{
    /// <summary>A circular orbit in the reference plane.</summary>
    public static KeplerOrbit Circular(double radius, double phase = 0, double spinRate = 0) =>
        new(radius, 0, 0, 0, 0, phase, 0, DQuaternion.Identity, spinRate, 0);

    /// <summary>
    /// True for an orbit whose own elements the Kepler driver can follow: a closed orbit, with an eccentricity from 0 up to
    /// but not including 1 and a positive semi-major axis, and every element, the tilt and the spin finite. The default
    /// orbit is not valid: it would leave the frame at its parent's origin. The orbit holds no G·M, so this does not check
    /// the parent: the World does (see <see cref="GravityField.CanBeOrbited"/>).
    /// </summary>
    public readonly bool IsValid =>
        SemiMajorAxis > 0 && double.IsFinite(SemiMajorAxis) &&
        Eccentricity >= 0 && Eccentricity < 1 &&
        double.IsFinite(Inclination) && double.IsFinite(LongitudeOfAscendingNode) && double.IsFinite(ArgumentOfPeriapsis) &&
        double.IsFinite(MeanAnomalyAtEpoch) && double.IsFinite(Epoch) && double.IsFinite(SpinRate) && double.IsFinite(SpinPhaseAtEpoch) &&
        double.IsFinite(AxialTilt.X) && double.IsFinite(AxialTilt.Y) && double.IsFinite(AxialTilt.Z) && double.IsFinite(AxialTilt.W) &&
        Attitude is KeplerAttitude.Spin or KeplerAttitude.FacesParent;
}

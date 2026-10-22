namespace Tertius.Demo;

/// <summary>
/// The steering law of a climb to a circular orbit in a plane through the planet's center, in a frame whose origin is the
/// planet's center, the center of its point gravity, and which does not turn. It tells which acceleration the engine
/// should give, from the ship's position and velocity alone, so it corrects every error of the steps before.
/// </summary>
/// <remarks>
/// <para>The law splits the motion into three parts: a vertical part, along the line from the planet's center, a part
/// across the plane, along its normal, and a part along the track, the direction in the plane across the up. The ship
/// must reach the circle's speed along the track, the circle's height with no vertical speed, and the plane with no speed
/// across it, at the same moment. The time to go is the time that the whole thrust needs for the speed along the track
/// that is left, and at least <see cref="ShortestTimeToGo"/>. In that time, the acceleration that starts a straight-line
/// change of acceleration and ends at a place with no speed toward it is 6 Δr / t² - 4 v / t. The vertical part is that,
/// plus the gravity, less the lift of the speed across the up, v² / r, up to full throttle either way. The part across the
/// plane is that toward the plane, up to the thrust that the vertical part leaves. The rest of the thrust goes along the
/// track, but never more than takes the speed that is left in <see cref="TrimTime"/>. So the throttle falls in the last
/// seconds, and the climb ends without a jolt.</para>
/// <para>From a ramp at 30 degrees, at 3 g, this pitches the thrust up to about 60 degrees, then over to the horizontal
/// as the ship nears the circle's height, and below it at the end, to stop the climb.</para>
/// <para>The part across the plane takes out what pushes the ship across it. In the demo, the hull yaws by about 0.15
/// degrees when it pitches onto its ramp. Without the part across the plane, the thrust along the whole speed across the
/// up kept that yaw, so the speed across the plane grew with the speed, to 22.9 m/s at the end of the climb.</para>
/// </remarks>
/// <param name="Mu">The planet's G·M, in m³/s².</param>
/// <param name="OrbitRadius">The radius of the circle, in meters from the planet's center.</param>
/// <param name="MaxAcceleration">The acceleration of the engine at full throttle, in m/s².</param>
/// <param name="PlaneNormal">
/// The normal of the circle's plane, a unit vector in the frame's axes. It is the plane's prograde normal: the normal
/// across the up of a ship in the plane points the way that the ship flies.
/// </param>
public readonly record struct AscentGuidance(double Mu, double OrbitRadius, double MaxAcceleration, Double3 PlaneNormal)
{
    /// <summary>The shortest time to go that the vertical part and the part across the plane plan with, in seconds, so that they do not swing at the end.</summary>
    public const double ShortestTimeToGo = 8;

    /// <summary>The time in which the part along the track takes the speed that is left along it, in seconds.</summary>
    public const double TrimTime = 4;

    /// <summary>
    /// How near the speed along the track must come to the circle's speed for the climb to end, in m/s: 0.2, the speed
    /// that the trim takes in <see cref="TrimTime"/> with an acceleration of <see cref="FrameTree.ZeroGravityThreshold"/>.
    /// So the climb ends as soon as the trim's thrust would leave the cabin weightless, and a passenger who waits for a
    /// weightless cabin never feels the engine run on.
    /// </summary>
    public const double SpeedTolerance = FrameTree.ZeroGravityThreshold * TrimTime;

    /// <summary>How near the vertical speed must come to zero for the climb to end, in m/s.</summary>
    public const double ClimbTolerance = 0.5;

    /// <summary>The speed of the circle, in m/s.</summary>
    public double CircleSpeed => Math.Sqrt(Mu / OrbitRadius);

    /// <summary>
    /// The acceleration that the engine should give, in m/s², in the frame's axes, at most <see cref="MaxAcceleration"/>
    /// long, for a ship at <paramref name="position"/> that moves at <paramref name="velocity"/>. The vertical part comes
    /// first, then the part across the plane, then the part along the track.
    /// </summary>
    public Double3 Acceleration(Double3 position, Double3 velocity)
    {
        double r = position.Length;
        // The up, the track and the side, the normal across the up: three orthogonal directions, so that the three parts
        // share the thrust exactly. Beside the plane, the up leans from it by the distance over the radius.
        Double3 up = position / r, track = Double3.Cross(PlaneNormal, up).Normalized(), side = Double3.Cross(up, track);
        double climb = Double3.Dot(velocity, up),
            across = Double3.Dot(position, PlaneNormal),
            acrossSpeed = Double3.Dot(velocity, PlaneNormal);
        double alongTrack = Double3.Dot(velocity, track);
        double speedLeft = CircleSpeed - alongTrack;
        double timeToGo = Math.Max(speedLeft / MaxAcceleration, ShortestTimeToGo);
        double vertical = 6 * (OrbitRadius - r) / (timeToGo * timeToGo) - 4 * climb / timeToGo + Mu / (r * r)
                          - (alongTrack * alongTrack + acrossSpeed * acrossSpeed) / r;
        vertical = Math.Clamp(vertical, -MaxAcceleration, MaxAcceleration);
        double acrossLeft = Math.Sqrt(MaxAcceleration * MaxAcceleration - vertical * vertical);
        double acrossPlane = Math.Clamp(-6 * across / (timeToGo * timeToGo) - 4 * acrossSpeed / timeToGo, -acrossLeft,
            acrossLeft);
        double horizontal = Math.Min(Math.Sqrt(Math.Max(0, acrossLeft * acrossLeft - acrossPlane * acrossPlane)),
            Math.Max(0, speedLeft / TrimTime));
        return up * vertical + side * acrossPlane + track * horizontal;
    }

    /// <summary>
    /// True when the climb is done: the speed along the track is within <see cref="SpeedTolerance"/> of the circle's
    /// speed, or above it, and the vertical speed within <see cref="ClimbTolerance"/> of zero.
    /// </summary>
    public bool Reached(Double3 position, Double3 velocity)
    {
        Double3 up = position.Normalized();
        double climb = Double3.Dot(velocity, up);
        double alongTrack = (velocity - up * climb).Length;
        return CircleSpeed - alongTrack < SpeedTolerance && Math.Abs(climb) < ClimbTolerance;
    }
}

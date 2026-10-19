namespace Tertius.Demo;

/// <summary>
/// The steering law of a climb to a circular orbit, in a frame whose origin is the planet's center, the center of its
/// point gravity, and which does not turn. It tells which acceleration the engine should give, from the ship's position
/// and velocity alone, so it corrects every error of the steps before.
/// </summary>
/// <remarks>
/// <para>The law splits the motion into a vertical part, along the line from the planet's center, and a horizontal part,
/// along the ground track. The ship must reach the circle's speed along the track, and the circle's height with no
/// vertical speed, at the same moment. The time to go is the time that the whole thrust needs for the speed along the
/// track that is left, and at least <see cref="ShortestTimeToGo"/>. In that time, the vertical acceleration that starts
/// a straight-line change of acceleration and ends at the circle's height with no vertical speed is 6 Δr / t² - 4 v / t.
/// The engine must give that, plus the gravity, less the lift of the speed along the track, v² / r. The rest of the
/// thrust goes along the track, but never more than takes the speed that is left in <see cref="TrimTime"/>. So the
/// throttle falls in the last seconds, and the climb ends without a jolt.</para>
/// <para>From a ramp at 30 degrees, at 3 g, this pitches the thrust up to about 60 degrees, then over to the horizontal
/// as the ship nears the circle's height, and below it at the end, to stop the climb.</para>
/// </remarks>
/// <param name="Mu">The planet's G·M, in m³/s².</param>
/// <param name="OrbitRadius">The radius of the circle, in meters from the planet's center.</param>
/// <param name="MaxAcceleration">The acceleration of the engine at full throttle, in m/s².</param>
public readonly record struct AscentGuidance(double Mu, double OrbitRadius, double MaxAcceleration)
{
    /// <summary>The shortest time to go that the vertical part plans with, in seconds, so that it does not swing at the end.</summary>
    public const double ShortestTimeToGo = 8;

    /// <summary>The time in which the horizontal part takes the speed that is left along the track, in seconds.</summary>
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

    /// <summary>Below this speed along the ground, in m/s, a ship has no track, and the law tells it to climb straight up.</summary>
    public const double TrackSpeed = 1e-3;

    /// <summary>The speed of the circle, in m/s.</summary>
    public double CircleSpeed => Math.Sqrt(Mu / OrbitRadius);

    /// <summary>
    /// The acceleration that the engine should give, in m/s², in the frame's axes, at most <see cref="MaxAcceleration"/>
    /// long, for a ship at <paramref name="position"/> that moves at <paramref name="velocity"/>. A ship with no speed along
    /// the ground has no track, so it is told to climb straight up.
    /// </summary>
    public Double3 Acceleration(Double3 position, Double3 velocity)
    {
        double r = position.Length;
        Double3 up = position / r;
        double climb = Double3.Dot(velocity, up);
        Double3 across = velocity - up * climb;
        double alongTrack = across.Length;
        if (alongTrack < TrackSpeed)
            return up * MaxAcceleration;
        double speedLeft = CircleSpeed - alongTrack;
        double timeToGo = Math.Max(speedLeft / MaxAcceleration, ShortestTimeToGo);
        double vertical = 6 * (OrbitRadius - r) / (timeToGo * timeToGo) - 4 * climb / timeToGo + Mu / (r * r) - alongTrack * alongTrack / r;
        vertical = Math.Clamp(vertical, -MaxAcceleration, MaxAcceleration);
        double horizontal = Math.Min(Math.Sqrt(MaxAcceleration * MaxAcceleration - vertical * vertical), Math.Max(0, speedLeft / TrimTime));
        return up * vertical + across * (horizontal / alongTrack);
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

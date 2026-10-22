namespace Tertius;

/// <summary>
/// The thresholds of the membership rules of numeric frames (docs/plans/numeric-frames.md, "Membership"). Each threshold to
/// leave is below the one to join, so that a body near a threshold does not go in and out. Speeds are in m/s relative to
/// the semantic frame, distances in meters, accelerations in m/s² and times in seconds.
/// <para>
/// A group has two pairs of speeds. A group of free bodies only, which coast, collide, push each other and fall together,
/// gets a falling frame from <see cref="CreateSpeed"/>. A group with a body that thrusts (see <see cref="ThrustOnset"/>) gets
/// one from <see cref="ThrustCreateSpeed"/>. The precision measurements behind the numbers (tests/Tertius.Physics.Tests/Precision)
/// found that the error of bodies that move together stays small: at 1 km/s, 1.3 mm of gap and 0.024 mm/s of relative
/// velocity per minute. A body that thrusts drifts from its neighbors much faster: 2.1 mm/s per minute at 50 m/s and
/// 85 mm/s per minute at 1 km/s.
/// </para>
/// </summary>
internal sealed record NumericFrameOptions
{
    /// <summary>
    /// A group of free bodies only whose common speed reaches this gets a falling frame. It is just below a power of two, so
    /// that one float spacing covers the whole range below it: 1,000 m/s is below 1,024 m/s.
    /// </summary>
    public double CreateSpeed { get; init; } = 1_000;

    /// <summary>A falling frame of free bodies only whose common speed drops below this stops falling: 0.4 of the create speed.</summary>
    public double LeaveSpeed { get; init; } = 400;

    /// <summary>
    /// A group with a body that thrusts (see <see cref="ThrustOnset"/>) whose common speed reaches this gets a falling frame.
    /// At 50 m/s, a body that thrusts drifts by about 2 mm/s per minute of thrust (measured 2.1 mm/s), which the user
    /// accepted against the doc's target of 1 mm/s.
    /// </summary>
    public double ThrustCreateSpeed { get; init; } = 50;

    /// <summary>A falling frame with a body that thrusts whose common speed drops below this stops falling.</summary>
    public double ThrustLeaveSpeed { get; init; } = 20;

    /// <summary>
    /// The acceleration that the force and impulse intents of a tick must ask of a body for the tick to count as a tick of
    /// thrust: 1 mm/s per minute. A thrust that small can be lost in the float rounding entirely and still stay under the
    /// design's target of 1 mm/s of drift over a minute.
    /// </summary>
    public double MinThrustAcceleration { get; init; } = 1e-3 / 60;

    /// <summary>
    /// The time of thrust ticks within the thrust window (see <see cref="ThrustWindow"/>) after which a body thrusts. Every
    /// character thrusts, always and at once. By the float model, a body that thrusts at a common speed just below 1,000 m/s
    /// drifts by at most about 1.8 mm/s per second of thrust (measured 1.4 mm/s per second). Half a second of thrust before
    /// the body counts keeps that drift under 1 mm/s. So a single impulse, or a single tick of force, never counts.
    /// </summary>
    public double ThrustOnset { get; init; } = 0.5;

    /// <summary>
    /// The time without a thrust tick after which a body's thrust time starts again at zero. Thrust in pulses less than this
    /// apart adds up, and a body that thrusts keeps thrusting for this long after its last thrust tick. With at most half a
    /// second of thrust that does not count in every 20.5 s, the drift stays near 2 mm/s per minute at most, the rate that
    /// the user accepted for groups that thrust at 50 m/s (measured 2.1 mm/s per minute).
    /// </summary>
    public double ThrustWindow { get; init; } = 20;

    /// <summary>
    /// A slow group of bodies whose center is this far from its semantic frame's origin gets a resting frame: 2^23 m, about
    /// 8,389 km. The precision measurements found no error from the distance up to 9,000 km.
    /// </summary>
    public double CreateDistance { get; init; } = 1 << 23;

    /// <summary>A numeric frame whose slow members' center comes nearer than this to the origin goes away: 2^22 m, half the create distance.</summary>
    public double LeaveDistance { get; init; } = 1 << 22;

    /// <summary>How far ahead in time the merge test looks: each pair's bounds grow by their relative speed times this.</summary>
    public double LookaheadTime { get; init; } = 2;

    /// <summary>How much farther than the merge test reaches the members of one frame must be apart before they split.</summary>
    public double SplitMargin { get; init; } = 10;

    /// <summary>How long the members of one frame must stay apart before they split.</summary>
    public double SplitCooldown { get; init; } = 1;

    /// <summary>The common speed of the members relative to their falling frame at which the frame takes it over.</summary>
    public double DrainSpeed { get; init; } = 1;

    /// <summary>The distance of the members' center of mass from their frame's origin at which the origin moves to it.</summary>
    public double RecenterDistance { get; init; } = 1_000;

    /// <summary>
    /// The distance of a numeric frame from its tree parent's origin at which it moves under a nesting frame near it
    /// (docs/plans/numeric-frames.md, "Nesting for vast distances"). It joins a nesting frame within half this distance.
    /// </summary>
    public double NestDistance { get; init; } = 1e9;

    /// <summary>
    /// The grid of nesting frames, in meters: a power of two, so that every multiple of it is exact in a double up to about
    /// a million light-years. The start is 2^20 m, about 1,049 km.
    /// </summary>
    public double NestGrid { get; init; } = 1 << 20;

    internal void Validate()
    {
        Check(CreateSpeed, nameof(CreateSpeed));
        Check(LeaveSpeed, nameof(LeaveSpeed));
        Check(ThrustCreateSpeed, nameof(ThrustCreateSpeed));
        Check(ThrustLeaveSpeed, nameof(ThrustLeaveSpeed));
        Check(CreateDistance, nameof(CreateDistance));
        Check(LeaveDistance, nameof(LeaveDistance));
        Check(LookaheadTime, nameof(LookaheadTime));
        Check(SplitMargin, nameof(SplitMargin));
        Check(SplitCooldown, nameof(SplitCooldown));
        Check(DrainSpeed, nameof(DrainSpeed));
        Check(RecenterDistance, nameof(RecenterDistance));
        Check(NestDistance, nameof(NestDistance));
        if (!(ThrustWindow > 0))
            throw new ArgumentOutOfRangeException(nameof(ThrustWindow), ThrustWindow,
                "The thrust window must be longer than zero.");
        // At zero, the thrust clock counts every body as one that thrusts, also a body without intents.
        if (!(MinThrustAcceleration > 0))
            throw new ArgumentOutOfRangeException(nameof(MinThrustAcceleration), MinThrustAcceleration,
                "The minimum thrust acceleration must be more than zero, or every tick is a thrust tick.");
        if (!(ThrustOnset > 0))
            throw new ArgumentOutOfRangeException(nameof(ThrustOnset), ThrustOnset,
                "The thrust onset must be longer than zero, or every body thrusts.");
        if (!(NestGrid > 0) || double.IsInfinity(NestGrid) || Math.ScaleB(1, Math.ILogB(NestGrid)) != NestGrid)
            throw new ArgumentOutOfRangeException(nameof(NestGrid), NestGrid,
                "The grid of nesting frames must be a power of two meters.");
        // A frame lands within 0.87 grid steps of its nest, which must be inside half the nest distance.
        if (NestDistance < 2 * NestGrid)
            throw new ArgumentOutOfRangeException(nameof(NestDistance), NestDistance,
                "The nest distance must be at least two grid steps.");
        if (LeaveSpeed > CreateSpeed)
            throw new ArgumentOutOfRangeException(nameof(LeaveSpeed), LeaveSpeed,
                "The speed to leave a numeric frame cannot pass the speed to get one.");
        if (ThrustLeaveSpeed > ThrustCreateSpeed)
            throw new ArgumentOutOfRangeException(nameof(ThrustLeaveSpeed), ThrustLeaveSpeed,
                "The speed to leave a numeric frame cannot pass the speed to get one.");
        // A group that thrusts gets a falling frame from a lower speed than a group of free bodies, and keeps it down to a
        // lower speed. The update's first test of a semantic frame relies on it (see FrameTree.NeedsWork).
        if (ThrustCreateSpeed > CreateSpeed)
            throw new ArgumentOutOfRangeException(nameof(ThrustCreateSpeed), ThrustCreateSpeed,
                "A group that thrusts cannot need more speed to get a numeric frame than a group of free bodies.");
        if (ThrustLeaveSpeed > LeaveSpeed)
            throw new ArgumentOutOfRangeException(nameof(ThrustLeaveSpeed), ThrustLeaveSpeed,
                "A group that thrusts cannot need more speed to keep a numeric frame than a group of free bodies.");
        if (LeaveDistance > CreateDistance)
            throw new ArgumentOutOfRangeException(nameof(LeaveDistance), LeaveDistance,
                "The distance to leave a numeric frame cannot pass the distance to get one.");
    }

    private static void Check(double value, string name)
    {
        if (double.IsNaN(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "A numeric frame threshold cannot be negative.");
    }
}

namespace Tertius.Simulation;

/// <summary>
/// The thresholds of the membership rules of numeric frames (docs/plans/numeric-frames.md, "Membership"), with the
/// doc's starting values. Each threshold to leave is below the one to join, so that a body near a threshold does not
/// go in and out. Speeds are in m/s relative to the semantic frame, distances in meters and times in seconds.
/// </summary>
internal sealed record NumericFrameOptions
{
    /// <summary>A group of bodies whose common speed reaches this gets a falling frame.</summary>
    public double CreateSpeed { get; init; } = 50;

    /// <summary>A falling frame whose members' common speed drops below this stops falling.</summary>
    public double LeaveSpeed { get; init; } = 20;

    /// <summary>A slow group of bodies whose center is this far from its semantic frame's origin gets a resting frame.</summary>
    public double CreateDistance { get; init; } = 10_000;

    /// <summary>A numeric frame whose slow members' center comes nearer than this to the origin goes away.</summary>
    public double LeaveDistance { get; init; } = 5_000;

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
        Check(CreateDistance, nameof(CreateDistance));
        Check(LeaveDistance, nameof(LeaveDistance));
        Check(LookaheadTime, nameof(LookaheadTime));
        Check(SplitMargin, nameof(SplitMargin));
        Check(SplitCooldown, nameof(SplitCooldown));
        Check(DrainSpeed, nameof(DrainSpeed));
        Check(RecenterDistance, nameof(RecenterDistance));
        Check(NestDistance, nameof(NestDistance));
        if (!(NestGrid > 0) || double.IsInfinity(NestGrid) || Math.ScaleB(1, Math.ILogB(NestGrid)) != NestGrid)
            throw new ArgumentOutOfRangeException(nameof(NestGrid), NestGrid, "The grid of nesting frames must be a power of two meters.");
        // A frame lands within 0.87 grid steps of its nest, which must be inside half the nest distance.
        if (NestDistance < 2 * NestGrid)
            throw new ArgumentOutOfRangeException(nameof(NestDistance), NestDistance, "The nest distance must be at least two grid steps.");
        if (LeaveSpeed > CreateSpeed)
            throw new ArgumentOutOfRangeException(nameof(LeaveSpeed), LeaveSpeed, "The speed to leave a numeric frame cannot pass the speed to get one.");
        if (LeaveDistance > CreateDistance)
            throw new ArgumentOutOfRangeException(nameof(LeaveDistance), LeaveDistance, "The distance to leave a numeric frame cannot pass the distance to get one.");
    }

    private static void Check(double value, string name)
    {
        if (double.IsNaN(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "A numeric frame threshold cannot be negative.");
    }
}

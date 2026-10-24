namespace Tertius.Testing;

/// <summary>
/// Membership rules of numeric frames (<see cref="NumericFrameOptions"/>) for tests that need other rules than the
/// defaults on purpose. A test that only needs a fast or far scene uses the defaults and builds the scene for them. Each
/// value here that changes a speed changes both speed pairs, the one for groups of free bodies and the one for groups
/// with a body that thrusts, so that no character and no body that thrusts slips through the rule.
/// </summary>
internal static class NumericFrameTesting
{
    /// <summary>
    /// Membership rules under which no group of bodies and no hull ever gets a numeric frame: every speed and distance
    /// threshold is infinite. A test uses them for a scene that must run in the system of its semantic frame alone, for
    /// example to check which system holds a body, or to run a scene with numeric frames beside the same scene without
    /// them.
    /// </summary>
    internal static readonly NumericFrameOptions WithoutNumericFrames = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        ThrustCreateSpeed = double.PositiveInfinity,
        ThrustLeaveSpeed = double.PositiveInfinity,
        CreateDistance = double.PositiveInfinity,
        LeaveDistance = double.PositiveInfinity,
    };

    /// <summary>
    /// Membership rules under which every group that is not anchored falls in a numeric frame, also a slow one or one at
    /// rest, and never stops falling for being slow: both speed pairs are 0. A test uses them to put bodies in falling
    /// frames without the speeds that the defaults need, for example to test a falling frame near other bodies.
    /// </summary>
    internal static readonly NumericFrameOptions EveryGroupFalls = new()
    {
        CreateSpeed = 0,
        LeaveSpeed = 0,
        ThrustCreateSpeed = 0,
        ThrustLeaveSpeed = 0,
    };

    /// <summary>
    /// Membership rules under which every slow group gets a resting numeric frame and never loses it for being near its
    /// semantic frame's origin: both distances are 0. A test uses them to put slow bodies in resting frames without the
    /// distance of 8,389 km that the defaults need.
    /// </summary>
    internal static readonly NumericFrameOptions EverySlowGroupRests = new() { CreateDistance = 0, LeaveDistance = 0 };
}

using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Tolerance checks for poses and vectors, for the transform, frame, physics, attachment and render snapshot tests.
/// AwesomeAssertions has no check for "a position within so many metres of another", and a pose that a frame tree
/// composed carries rounding error in its last bits, so a test states how much error it allows. The checks are plain
/// static methods, not extensions, so that they cannot clash with the helpers of another test file.
/// </summary>
internal static class PoseChecks
{
    /// <summary>The unit quarter turn about +Y: it maps +X to -Z and +Z to +X.</summary>
    public static readonly DQuaternion QuarterTurnAboutY = DQuaternion.FromAxisAngle(Double3.UnitY, Math.PI / 2);

    /// <summary>Checks that two positions are at most <paramref name="metres"/> apart.</summary>
    public static void AssertNear(Double3 actual, Double3 expected, double metres = 1e-9) =>
        (actual - expected)
            .Length.Should()
            .BeLessThanOrEqualTo(metres, "{0} should be within {1} m of {2}", actual, metres, expected);

    /// <summary>Checks that two vectors are at most <paramref name="tolerance"/> apart.</summary>
    public static void AssertNear(Vector3 actual, Vector3 expected, float tolerance = 1e-5f) =>
        Vector3
            .Distance(actual, expected)
            .Should()
            .BeLessThanOrEqualTo(tolerance, "{0} should be within {1} of {2}", actual, tolerance, expected);

    /// <summary>
    /// Checks that two quaternions turn a vector the same way. A quaternion and its negation are the same rotation, so
    /// comparing the components would be wrong. The check turns the three axes and compares the results.
    /// </summary>
    public static void AssertSameRotation(DQuaternion actual, DQuaternion expected, double tolerance = 1e-9)
    {
        foreach (Double3 axis in new[] { Double3.UnitX, Double3.UnitY, Double3.UnitZ })
            AssertNear(actual.Rotate(axis), expected.Rotate(axis), tolerance);
    }

    /// <summary>The same check for single-precision rotations, as the render snapshot holds them.</summary>
    public static void AssertSameRotation(Quaternion actual, DQuaternion expected, float tolerance = 1e-5f)
    {
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            AssertNear(Vector3.Transform(axis, actual), expected.Rotate(axis), tolerance);
    }
}

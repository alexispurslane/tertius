using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The motion of a frame through another frame, and the motion of a parent in its frame (docs/plans/spin-gravity.md, "V7,
/// the relative motion of two frames"): <see cref="FrameMotion.MotionToParent"/> composes the motion of an inner frame in a
/// middle frame with the motion of the middle frame in its parent, and <see cref="FrameMotion.Inverse"/> turns the motion of
/// a frame in its parent into the motion of the parent in the frame. <see cref="FrameTree.MotionRelativeInStep"/> composes
/// the levels of a path with them. The expected values differentiate the inner frame's path and orientation in the parent
/// by central differences.
/// </summary>
public class FrameMotionCompositionTests
{
    // The middle frame in the parent, and the inner frame in the middle frame: every vector off the axes, and each frame's
    // spin and spin-up not parallel.
    private static readonly Double3 MiddlePlace = new(40, -12, 3),
        MiddleVelocity = new(2, -7, 0.5),
        MiddleAcceleration = new(-0.3, 0.8, 1.1);

    private static readonly Double3 MiddleSpin = new(0.1, 0.31321, -0.05), MiddleSpinUp = new(0.02, -0.01, 0.2);
    private static readonly DQuaternion MiddleAxes = DQuaternion.FromAxisAngle(new(1, -2, 0.5), 0.9);

    private static readonly Double3 InnerPlace = new(30, 4, -9),
        InnerVelocity = new(1.5, -0.5, 4),
        InnerAcceleration = new(0.25, 2, -1);

    private static readonly Double3 InnerSpin = new(-0.6, 1.2, 0.4), InnerSpinUp = new(0.3, 0.1, -0.5);
    private static readonly DQuaternion InnerAxes = DQuaternion.FromAxisAngle(new(2, 1, -1), 0.3);

    private static readonly FrameMotion Middle = new(new(MiddlePlace, MiddleAxes), MiddleVelocity, MiddleSpin,
        MiddleAcceleration, MiddleSpinUp);

    private static readonly FrameMotion Inner = new(new(InnerPlace, InnerAxes), InnerVelocity, InnerSpin,
        InnerAcceleration, InnerSpinUp);

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance, string what) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{what}: {actual} is not {expected}");

    private static void ExpectNear(DQuaternion actual, DQuaternion expected, double tolerance, string what) =>
        (2 * DQuaternion.Identity.AngularVelocityTo(actual * expected.Conjugate(), 2).Length).Should()
        .BeLessThan(tolerance, $"{what}: {actual} is not {expected}");

    [Fact]
    public void AMotionThroughAMiddleFrameIsTheMotionOfTheInnerFrameInTheParent()
    {
        Double3 PlaceInParent(double t) =>
            MiddlePlace + MiddleVelocity * t + MiddleAcceleration * (0.5 * t * t) +
            AxesAt(MiddleSpin, MiddleSpinUp, MiddleAxes, t)
                .Rotate(InnerPlace + InnerVelocity * t + InnerAcceleration * (0.5 * t * t));

        DQuaternion AxesInParent(double t) => AxesAt(MiddleSpin, MiddleSpinUp, MiddleAxes, t) *
                                              AxesAt(InnerSpin, InnerSpinUp, InnerAxes, t);

        const double h = 1e-3, k = 1e-4;
        Double3 AngularVelocityAt(double t) => AxesInParent(t - k).AngularVelocityTo(AxesInParent(t + k), 2 * k);

        FrameMotion composed = Middle.MotionToParent(Inner);

        ExpectNear(composed.Pose.Position, PlaceInParent(0), 1e-12, "the place");
        ExpectNear(composed.Pose.Rotation, AxesInParent(0), 1e-12, "the rotation");
        ExpectNear(composed.Velocity, (PlaceInParent(h) - PlaceInParent(-h)) / (2 * h), 1e-4, "the velocity");
        ExpectNear(composed.Acceleration, (PlaceInParent(h) - PlaceInParent(0) * 2 + PlaceInParent(-h)) / (h * h), 1e-4,
            "the acceleration");
        ExpectNear(composed.AngularVelocity, AngularVelocityAt(0), 1e-6, "the angular velocity");
        ExpectNear(composed.AngularAcceleration, (AngularVelocityAt(h) - AngularVelocityAt(-h)) / (2 * h), 1e-5,
            "the angular acceleration");
    }

    [Fact]
    public void TheMotionOfAParentInItsFrameUndoesTheMotionOfTheFrameInTheParent()
    {
        // The parent seen from the frame, then the frame seen from the parent: the frame in itself, which neither moves nor turns.
        FrameMotion itself = Middle.Inverse().MotionToParent(Middle);
        FrameMotion parentInItself = Middle.MotionToParent(Middle.Inverse());

        foreach ((FrameMotion motion, string what) in new[] { (itself, "the frame"), (parentInItself, "the parent") })
        {
            ExpectNear(motion.Pose.Position, Double3.Zero, 1e-12, $"{what}'s place");
            ExpectNear(motion.Pose.Rotation, DQuaternion.Identity, 1e-12, $"{what}'s rotation");
            ExpectNear(motion.Velocity, Double3.Zero, 1e-12, $"{what}'s velocity");
            ExpectNear(motion.Acceleration, Double3.Zero, 1e-12, $"{what}'s acceleration");
            ExpectNear(motion.AngularVelocity, Double3.Zero, 1e-12, $"{what}'s angular velocity");
            ExpectNear(motion.AngularAcceleration, Double3.Zero, 1e-12, $"{what}'s angular acceleration");
        }
    }

    /// <summary>
    /// Axes that start at <paramref name="start"/> and turn about <paramref name="spin"/> + <paramref name="spinUp"/> t,
    /// in the outer axes: the turn by the rotation vector spin t + spinUp t² / 2, whose angular velocity at t = 0 is
    /// <paramref name="spin"/> and whose angular acceleration there is <paramref name="spinUp"/>.
    /// </summary>
    private static DQuaternion AxesAt(Double3 spin, Double3 spinUp, DQuaternion start, double t)
    {
        Double3 rotation = spin * t + spinUp * (0.5 * t * t);
        return DQuaternion.FromAxisAngle(rotation, rotation.Length) * start;
    }
}

using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The accelerations of a point and of a body between a frame and its parent, with the frame's own acceleration, its
/// angular acceleration and its rotation terms (docs/plans/spin-gravity.md, "V7, the relative motion of two frames").
/// The tests with literal results use a frame at (10, 0, 0) that stands a quarter turn about +Z from its parent, so that
/// the frame's +X is the parent's +Y and its +Y is the parent's -X.
/// </summary>
public class FrameMotionAccelerationTests
{
    // The frame moves at (0, 5, 0) and accelerates at (1, 0, 0). It turns at 2 rad/s about +Z and spins up at 3 rad/s².
    private static readonly FrameMotion QuarterTurned = new(
        Pose: new(new(10, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)),
        Velocity: new(0, 5, 0),
        AngularVelocity: new(0, 0, 2),
        Acceleration: new(1, 0, 0),
        AngularAcceleration: new(0, 0, 3));

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance = 1e-12) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    // ---- The acceleration of a point --------------------------------------------------------------------------

    [Fact]
    public void APointThatMovesInATurningFrameThatSpinsUpGainsTheFramesAccelerationAndItsRotationTermsInTheParent()
    {
        // The point is at (1, 0, 0) in the frame, so r = (0, 1, 0) from the frame's origin in the parent's axes. It moves at
        // (0, 1, 0) in the frame, which is (-1, 0, 0) in the parent's axes, and accelerates at (2, 0, 0), which is (0, 2, 0).
        // The sum: (0, 2, 0) turned, (1, 0, 0) of the frame, α × r = (-3, 0, 0), ω × (ω × r) = (0, -4, 0) and
        // 2 ω × (R v) = (0, -4, 0).
        Double3 acceleration = QuarterTurned.AccelerationToParent(new(1, 0, 0), new(0, 1, 0), new(2, 0, 0));

        ExpectNear(acceleration, new(-2, -6, 0));
    }

    [Fact]
    public void APointsAccelerationInTheFrameIsItsAccelerationInTheParentLessTheFramesAccelerationAndRotationTerms()
    {
        // The same point, seen in the parent: at (10, 1, 0), moving at (-1, 0, 0) + (0, 5, 0) + ω × r = (-3, 5, 0).
        Double3 acceleration = QuarterTurned.AccelerationFromParent(new(10, 1, 0), new(-3, 5, 0), new(-2, -6, 0));

        ExpectNear(acceleration, new(2, 0, 0));
    }

    [Fact]
    public void ThePointsAccelerationInTheParentIsTheSecondDerivativeOfItsPathThroughTheMovingFrame()
    {
        // A general case: every vector off the axes, the frame's spin and spin-up not parallel. The expected value is the
        // second difference of the point's place in the parent, from the frame's pose and the point's path in it.
        Double3 framePlace = new(40, -12, 3), frameVelocity = new(2, -7, 0.5), frameAcceleration = new(-0.3, 0.8, 1.1);
        Double3 spin = new(0.1, 0.31321, -0.05), spinUp = new(0.02, -0.01, 0.2);
        DQuaternion startAxes = DQuaternion.FromAxisAngle(new(1, -2, 0.5), 0.9);
        Double3 place = new(30, 4, -9), velocity = new(1.5, -0.5, 4), acceleration = new(0.25, 2, -1);

        Double3 InParent(double t) =>
            framePlace + frameVelocity * t + frameAcceleration * (0.5 * t * t) +
            AxesAt(spin: spin, spinUp: spinUp, start: startAxes, t: t)
                .Rotate(place + velocity * t + acceleration * (0.5 * t * t));

        const double h = 1e-3;
        Double3 expected = (InParent(h) - InParent(0) * 2 + InParent(-h)) / (h * h);
        var motion = new FrameMotion(Pose: new(framePlace, startAxes), Velocity: frameVelocity, AngularVelocity: spin,
            Acceleration: frameAcceleration, AngularAcceleration: spinUp);

        ExpectNear(motion.AccelerationToParent(place, velocity, acceleration), expected, 1e-5);
    }

    [Fact]
    public void APointsAccelerationConvertsIntoTheParentAndBackToItself()
    {
        FrameMotion motion = GeneralMotion();
        Double3 place = new(30, 4, -9), velocity = new(1.5, -0.5, 4), acceleration = new(0.25, 2, -1);

        Double3 back = motion.AccelerationFromParent(motion.Pose.TransformPoint(place),
            motion.VelocityToParent(place, velocity),
            motion.AccelerationToParent(place, velocity, acceleration));

        ExpectNear(back, acceleration, 1e-12);
    }

    // ---- The angular acceleration of a body -------------------------------------------------------------------

    [Fact]
    public void ABodyThatTurnsInATurningFrameThatSpinsUpGainsTheFramesSpinUpAndTheCrossTermInTheParent()
    {
        // The body turns at (1, 0, 0) in the frame, (0, 1, 0) in the parent's axes, and spins up at (0, 1, 0), which is
        // (-1, 0, 0). The sum: (-1, 0, 0) turned, (0, 0, 3) of the frame, and ω × (R ω_b) = (-2, 0, 0).
        Double3 alpha = QuarterTurned.AngularAccelerationToParent(new(0, 1, 0), new(1, 0, 0));

        ExpectNear(alpha, new(-3, 0, 3));
    }

    [Fact]
    public void
        ABodysAngularAccelerationInTheFrameIsItsAngularAccelerationInTheParentLessTheFramesSpinUpAndTheCrossTerm()
    {
        // The same body turns at (0, 1, 0) + (0, 0, 2) = (0, 1, 2) in the parent, so ω × Ω' = (-2, 0, 0).
        Double3 alpha = QuarterTurned.AngularAccelerationFromParent(new(-3, 0, 3), new(0, 1, 2));

        ExpectNear(alpha, new(0, 1, 0));
    }

    [Fact]
    public void TheBodysAngularAccelerationInTheParentIsTheChangeOfItsAngularVelocityThroughTheMovingFrame()
    {
        // A general case. The expected value differentiates twice: the body's angular velocity in the parent from its
        // orientation there, and then its change, both by central differences.
        Double3 spin = new(0.1, 0.31321, -0.05), spinUp = new(0.02, -0.01, 0.2);
        DQuaternion startAxes = DQuaternion.FromAxisAngle(new(1, -2, 0.5), 0.9);
        Double3 bodySpin = new(-0.6, 1.2, 0.4), bodySpinUp = new(0.3, 0.1, -0.5);
        DQuaternion startOrientation = DQuaternion.FromAxisAngle(new(2, 1, -1), 0.3);

        DQuaternion OrientationInParent(double t) =>
            AxesAt(spin: spin, spinUp: spinUp, start: startAxes, t: t) *
            AxesAt(spin: bodySpin, spinUp: bodySpinUp, start: startOrientation, t: t);

        const double k = 1e-4, h = 1e-3;

        Double3 AngularVelocityAt(double t) =>
            OrientationInParent(t - k).AngularVelocityTo(OrientationInParent(t + k), 2 * k);

        Double3 expected = (AngularVelocityAt(h) - AngularVelocityAt(-h)) / (2 * h);
        var motion = new FrameMotion(Pose: new(new(40, -12, 3), startAxes), Velocity: new(2, -7, 0.5),
            AngularVelocity: spin, Acceleration: Double3.Zero, AngularAcceleration: spinUp);

        ExpectNear(motion.AngularAccelerationToParent(bodySpinUp, bodySpin), expected, 1e-5);
    }

    [Fact]
    public void ABodysAngularAccelerationConvertsIntoTheParentAndBackToItself()
    {
        FrameMotion motion = GeneralMotion();
        Double3 bodySpin = new(-0.6, 1.2, 0.4), bodySpinUp = new(0.3, 0.1, -0.5);

        Double3 back = motion.AngularAccelerationFromParent(motion.AngularAccelerationToParent(bodySpinUp, bodySpin),
            motion.AngularToParent(bodySpin));

        ExpectNear(back, bodySpinUp, 1e-12);
    }

    /// <summary>
    /// Axes that start at <paramref name="start"/> and turn about <paramref name="spin"/> + <paramref name="spinUp"/> t,
    /// in the outer axes: the turn by the rotation vector spin t + spinUp t² / 2. Its angular velocity at t = 0 is
    /// <paramref name="spin"/>, and its angular acceleration there is <paramref name="spinUp"/>, because the turn's angular
    /// velocity differs from spin + spinUp t only by terms of order t².
    /// </summary>
    private static DQuaternion AxesAt(Double3 spin, Double3 spinUp, DQuaternion start, double t)
    {
        Double3 rotation = spin * t + spinUp * (0.5 * t * t);
        return DQuaternion.FromAxisAngle(rotation, rotation.Length) * start;
    }

    private static FrameMotion GeneralMotion() => new(
        Pose: new(new(40, -12, 3), DQuaternion.FromAxisAngle(new(1, -2, 0.5), 0.9)),
        Velocity: new(2, -7, 0.5),
        AngularVelocity: new(0.1, 0.31321, -0.05),
        Acceleration: new(-0.3, 0.8, 1.1),
        AngularAcceleration: new(0.02, -0.01, 0.2));
}

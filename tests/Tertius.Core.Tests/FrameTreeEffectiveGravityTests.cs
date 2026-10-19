using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The gravity that a body feels in a frame (docs/plans/spin-gravity.md, "V5, the effective gravity"):
/// <see cref="FrameTree.LinearGravity"/>, the field minus the proper acceleration and the home's acceleration, and
/// <see cref="FrameTree.EffectiveGravity"/> and <see cref="FrameTree.SemanticGravity"/>, which add the centrifugal and the
/// Euler term of a turning frame at the body's semantic place, from the turns that <see cref="FrameTree.BeginStep"/> fixed.
/// Each value is worked out by hand.
/// <para>
/// The station of the tests holds a uniform field of 2 m/s² along its −Y, so that the sum of the field and the rotation
/// terms shows, and it spins about its own +Y at 0.31321 rad/s, the rate that gives 9.81 m/s² at a radius of 100 m, as the
/// station of the spike on the branch spike-spin-gravity. A rate ω about +Y gives a body at (r, 0, 0) the centrifugal
/// acceleration −ω × (ω × r) = (ω² r, 0, 0), outward, and an angular acceleration α about +Y gives it the Euler acceleration
/// −α × r = (0, 0, α r), against the turn: a frame that turns about +Y carries +X toward −Z.
/// </para>
/// </summary>
public class FrameTreeEffectiveGravityTests
{
    private const double Step = 1.0 / 60;
    private const double StationRate = 0.31321;

    /// <summary>The angular acceleration of the station that spins up, in rad/s².</summary>
    private const double SpinUpRate = 0.5;

    private static readonly FrameSpin StationSpin = new(DQuaternion.Identity, StationRate);
    private static readonly GravityField StationField = GravityField.Uniform(new Vector3(0, -2, 0));
    private static readonly Double3 Rim = new(100, 0, 0);

    private readonly FrameTree frames = new();
    private readonly FrameHandle space;
    private uint nextOwner = 2;

    public FrameTreeEffectiveGravityTests() =>
        space = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));

    private FrameHandle Station(GravityField? field = null) =>
        frames.Create(new Entity(nextOwner++, 1), space, DPose.Identity, FrameBounds.Sphere(1e5f), field ?? StationField);

    /// <summary>The intents of a step in which the owner of <paramref name="frame"/> wrote <paramref name="move"/>, relative to the frame's parent.</summary>
    private PhysicsIntent[] Intents(FrameHandle frame, MotionIntent move)
    {
        var intents = new PhysicsIntent[nextOwner];
        intents[frame.Entity.Index] = new PhysicsIntent { Frame = frames.Parent(frame), Move = move };
        return intents;
    }

    /// <summary>One step of <see cref="Step"/> seconds: the tree updates, fixes the turns of the step, and takes its step records.</summary>
    private void RunStep(PhysicsIntent[] intents)
    {
        frames.Update();
        frames.BeginStep(intents, Step, lastStepFailed: false);
        frames.TakeStepRecords(intents, Step, null);
    }

    /// <summary>The time at the end of step <paramref name="k"/>, the first step being 1, from a start at time 0.</summary>
    private static double End(int k) => k * Step;

    private static MotionIntent Spin(double stepEnd) => new(StationSpin.Sample(Double3.Zero, stepEnd, Step));

    /// <summary>The full sample of the step that ends at <paramref name="stepEnd"/> of a frame that spins up about +Y from rest at time 0, at <see cref="SpinUpRate"/>.</summary>
    private static MotionIntent SpinningUp(double stepEnd) => new(new MotionSample(
        new DPose(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * SpinUpRate * stepEnd * stepEnd)),
        Double3.Zero, Double3.Zero, Double3.Zero, new Double3(0, SpinUpRate * stepEnd, 0), new Double3(0, SpinUpRate, 0)));

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    /// <summary>A station that has spun steadily for three steps.</summary>
    private FrameHandle SpinningStation(GravityField? field = null)
    {
        FrameHandle station = Station(field);
        for (int k = 1; k <= 3; k++)
            RunStep(Intents(station, Spin(End(k))));
        return station;
    }

    [Fact]
    public void ABodyInASteadilySpinningStationFeelsTheFieldPlusTheCentrifugalTermOutwardFromTheAxis()
    {
        FrameHandle station = SpinningStation();

        // ω² r = 0.31321² · 100 m = 9.81005041 m/s², outward along +X, and the field of 2 m/s² along −Y.
        ExpectNear(frames.EffectiveGravity(station, Rim), new Double3(StationRate * StationRate * 100, -2, 0), 1e-12);
    }

    [Fact]
    public void ABodyInAStationThatSpinsUpAlsoFeelsTheEulerTermAgainstTheTurn()
    {
        FrameHandle station = Station();
        for (int k = 1; k <= 2; k++)
            RunStep(Intents(station, SpinningUp(End(k))));

        // The step in flight ends at End(2), where the station turns at α · End(2) = 1/60 rad/s, and the angular velocity
        // grew by α over the step: the Euler term is α r = 50 m/s² along +Z.
        double rate = SpinUpRate * End(2);
        ExpectNear(frames.EffectiveGravity(station, Rim), new Double3(rate * rate * 100, -2, SpinUpRate * 100), 1e-9);
    }

    [Fact]
    public void TheLinearGravityOfATurningFrameLeavesOutTheRotationTerms()
    {
        FrameHandle station = SpinningStation();

        frames.LinearGravity(station, Rim).Should().Be(new Double3(0, -2, 0));
    }

    [Fact]
    public void ANewFrameFeelsOnlyItsLinearGravityBeforeItsFirstStepAlsoInTheSlotOfADestroyedFrameThatTurned()
    {
        FrameHandle fresh = Station();
        frames.EffectiveGravity(fresh, Rim).Should().Be(frames.LinearGravity(fresh, Rim), "a frame that had no step reads no turn");

        FrameHandle station = Station();
        for (int k = 1; k <= 2; k++)
            RunStep(Intents(station, SpinningUp(End(k))));
        frames.EffectiveGravity(station, Rim).Should().NotBe(frames.LinearGravity(station, Rim));
        frames.Destroy(station);
        FrameHandle room = Station();

        room.Index.Should().Be(station.Index, "the tree reuses the slot of the destroyed frame");
        frames.EffectiveGravity(room, Rim).Should().Be(frames.LinearGravity(room, Rim));
        frames.EffectiveGravity(room, Rim).Should().Be(new Double3(0, -2, 0));
    }

    [Fact]
    public void TheSemanticGravityInANumericFrameOfATurningFrameHoldsTheRotationTermsAtTheBodysPlaceInTheTurningFrame()
    {
        // A pod falls 10 km from the station's axis. A body 5 m past the pod's origin along +X is 10,005 m from the axis.
        FrameHandle station = Station();
        FrameHandle pod = FallingFrame(station, new Double3(10_000, 0, 0), new Double3(0, 0, -3_000));
        for (int k = 1; k <= 3; k++)
            RunStep(Intents(station, Spin(End(k))));

        ExpectNear(frames.SemanticGravity(pod, new Double3(5, 0, 0)), new Double3(StationRate * StationRate * 10_005, -2, 0), 1e-9);
        frames.SemanticGravity(station, Rim).Should().Be(frames.EffectiveGravity(station, Rim), "in a semantic frame the two are one");
    }

    [Fact]
    public void TheEffectiveGravityInANumericFrameOfATurningFrameIsItsSemanticGravityLessTheHomesAcceleration()
    {
        FrameHandle station = Station();
        FrameHandle pod = FallingFrame(station, new Double3(10_000, 0, 0), new Double3(0, 0, -3_000));
        for (int k = 1; k <= 3; k++)
            RunStep(Intents(station, Spin(End(k))));
        var member = new Double3(5, 0, 0);

        frames.HomeAcceleration(pod, station).Should().NotBe(Double3.Zero, "the origin of the falling pod keeps to the inertial line of a free body");
        ExpectNear(frames.EffectiveGravity(pod, member), frames.SemanticGravity(pod, member) - frames.HomeAcceleration(pod, station), 1e-9);
    }

    /// <summary>The falling numeric frame that a numeric update gives one body of <paramref name="semantic"/> at <paramref name="position"/>.</summary>
    private FrameHandle FallingFrame(FrameHandle semantic, Double3 position, Double3 velocity)
    {
        FrameHandle home = FrameHandle.Null;
        var body = new NumericBody
        {
            Entity = new Entity(100, 1), Semantic = semantic, Home = semantic, Position = position, HomePosition = position,
            Velocity = velocity, Radius = 1, Mass = 1,
        };
        frames.UpdateNumericFrames([body], 0.1, 0.1, new PhysicsChanges(), moves => home = moves.Single().To);
        frames.Numeric(home).Should().Be(NumericMode.Falling);
        return home;
    }
}

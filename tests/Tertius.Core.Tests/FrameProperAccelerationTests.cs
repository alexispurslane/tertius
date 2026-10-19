using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The rule that gives a frame its proper acceleration, on a tree built in the test (docs/plans/rails-components.md, "The
/// proper acceleration of a frame"): a frame's proper acceleration is its acceleration over the step minus the gravity
/// that acts at its place where the step starts, in the frame's own axes. The gravity at its place is its semantic
/// parent's field there minus the parent's own proper acceleration. The tests call what step 2 of the sync point calls:
/// <see cref="FrameTree.Follow"/> for a frame with a step record and <see cref="FrameTree.HoldStill"/> for one without.
/// The scenes on the real World are in Tertius.Physics.Tests.
/// </summary>
public class FrameProperAccelerationTests
{
    private const double EarthMu = 3.986004418e14;
    private const double OrbitRadius = 6.771e6;
    private const double OrbitSpeed = 7_672.6;
    private const double Dt = 1.0 / 60;
    private static readonly GravityField Earth = GravityField.Point(EarthMu);

    private readonly FrameTree frames = new();
    private uint nextOwner = 1;

    private FrameHandle Frame(FrameHandle parent, DPose pose = default, GravityField field = default) =>
        frames.Create(new Entity(nextOwner++, 1), parent, pose.Rotation.LengthSquared == 0 ? new DPose(pose.Position, DQuaternion.Identity) : pose,
            FrameBounds.Sphere(1e9f), field);

    [Fact]
    public void AFrameAtRestInAFrameThatHoldsStillInAFieldFeelsThatFieldOneSyncPointLater()
    {
        FrameHandle ground = Frame(FrameHandle.Null, field: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        FrameHandle cabin = Frame(ground, TestPose(0, 10, 0));
        FrameHandle crate = Frame(cabin, TestPose(1, 0, 0));

        HoldStillAndStore(cabin, crate);

        frames.ProperAcceleration(cabin).Should().Be(new Vector3(0, 9.81f, 0), "the cabin holds still against the ground's gravity");
        frames.ProperAcceleration(crate).Should().Be(Vector3.Zero, "the rule read the cabin's value as stored before the pass");

        HoldStillAndStore(cabin, crate);

        frames.ProperAcceleration(crate).Should().Be(new Vector3(0, 9.81f, 0),
            "the cabin has no field of its own, so the gravity at the crate's place is the cabin's proper acceleration reversed");
    }

    [Fact]
    public void TheNewProperAccelerationsReachTheRowsOnlyWhenTheTreeStoresThem()
    {
        FrameHandle ground = Frame(FrameHandle.Null, field: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        FrameHandle cabin = Frame(ground, TestPose(0, 10, 0));

        frames.HoldStill(cabin, Dt);
        frames.ProperAcceleration(cabin).Should().Be(Vector3.Zero, "the pass of step 2 keeps each new value aside");

        frames.StoreNewProperAccelerations();
        frames.ProperAcceleration(cabin).Should().Be(new Vector3(0, 9.81f, 0));
        frames.AssertInvariants();
    }

    [Fact]
    public void AFrameThatHoldsStillInAFallingFrameIsInFreeFall()
    {
        FrameHandle earth = Frame(FrameHandle.Null, field: Earth);
        FrameHandle pod = FallingFrame(earth, new Double3(OrbitRadius, 0, 0), new Double3(0, 0, OrbitSpeed));
        FrameHandle cabin = Frame(pod);
        frames.Update();
        frames.BeginNumericStep([], 1.0 / 60);

        frames.AdvanceNumericFrames(Dt);
        HoldStillAndStore(cabin);

        frames.ProperAcceleration(cabin).Length().Should().BeLessThan(1e-6f,
            "the cabin moves with the falling frame, which accelerates by the gravity at its origin");
    }

    [Fact]
    public void AFrameThatAFollowedRecordAcceleratesFeelsItsAccelerationLessTheGravityWhereTheStepStarted()
    {
        FrameHandle earth = Frame(FrameHandle.Null, field: Earth);
        var place = new Double3(OrbitRadius, 0, 0);
        FrameHandle ship = Frame(earth, new DPose(place, DQuaternion.Identity));
        var chord = new Double3(0, 0, OrbitSpeed);
        var thrust = new Double3(0, 3, 0);
        Double3 end = place + chord * Dt;
        Double3 gravity = Earth.AtPrecise(place);
        var record = new MotionSample(new DPose(end, DQuaternion.Identity), chord, chord, gravity + thrust, Double3.Zero, Double3.Zero);

        frames.Follow(ship, earth, record, FollowKind.Rails, measured: null, Dt);
        frames.StoreNewProperAccelerations();

        Vector3 proper = frames.ProperAcceleration(ship);
        proper.X.Should().BeApproximately(0, 1e-9f);
        proper.Y.Should().BeApproximately(3, 1e-6f, "the thrust is the only acceleration that is not the gravity where the step started");
        proper.Z.Should().BeApproximately(0, 1e-9f);
    }

    [Fact]
    public void AFrameTakesTheProperAccelerationThatPhysicsMeasuredInPlaceOfTheRule()
    {
        FrameHandle ground = Frame(FrameHandle.Null, field: GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        FrameHandle ship = Frame(ground);
        var jolt = new Vector3(0, 0, 120);

        frames.Follow(ship, ground, new MotionSample(DPose.Identity, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero),
            FollowKind.Physics, jolt, Dt);
        frames.StoreNewProperAccelerations();

        frames.ProperAcceleration(ship).Should().Be(jolt);
    }

    /// <summary>What step 2 does for frames without a record: each holds still, then the tree stores their new values.</summary>
    private void HoldStillAndStore(params FrameHandle[] stillFrames)
    {
        foreach (FrameHandle frame in stillFrames)
            frames.HoldStill(frame, Dt);
        frames.StoreNewProperAccelerations();
    }

    private static DPose TestPose(double x, double y, double z) => new(new Double3(x, y, z), DQuaternion.Identity);

    /// <summary>The falling frame that a numeric update gives one fast body of <paramref name="semantic"/>.</summary>
    private FrameHandle FallingFrame(FrameHandle semantic, Double3 position, Double3 velocity)
    {
        FrameHandle home = FrameHandle.Null;
        var body = new NumericBody
        {
            Entity = new Entity(1000, 1), Semantic = semantic, Home = semantic, Position = position, HomePosition = position,
            Velocity = velocity, Radius = 1, Mass = 1,
        };
        frames.UpdateNumericFrames([body], 0.1, new PhysicsChanges(), moves => home = moves.Single().To);
        frames.IsNumeric(home).Should().BeTrue();
        return home;
    }
}

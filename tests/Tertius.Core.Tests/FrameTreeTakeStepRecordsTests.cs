using System.Numerics;
using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// Step 2 of the sync point on a tree built in the test (<see cref="FrameTree.TakeStepRecords"/>, docs/plans/rails-components.md,
/// "The step record of the last finished step"): every frame that is not numeric takes its step record, and the kind of its
/// owner decides where the record comes from. A frame with a hull takes the record that physics released for the hull, and
/// holds still without one. A frame without a hull takes the record of its owner's motion intent in the intents that the
/// step applied, and holds still without one. A write of the owner's transform in the tick comes last and wins, at rest. A
/// frame whose update the step skipped keeps its state: a frame with a hull after a step that failed, and a frame whose
/// owner's update the check of the physics output skipped.
/// </summary>
public class FrameTreeTakeStepRecordsTests
{
    private const double Step = 1.0 / 60;
    private const float G = 9.81f;
    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;

    public FrameTreeTakeStepRecordsTests() =>
        ground = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f),
            GravityField.Uniform(new Vector3(0, -G, 0)));

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>A frame of the owner <paramref name="id"/> 100 m along +X in ground, moving at 3 m/s along +Z, with a hull of <paramref name="motion"/>.</summary>
    private FrameHandle Child(uint id, BodyMotion? motion)
    {
        FrameHandle frame = frames.Create(new Entity(id, 1), ground, new DPose(X(100), DQuaternion.Identity), FrameBounds.Sphere(10));
        frames.SetHullMotion(frame, motion);
        frames.SetTreeLinearVelocity(frame, new Double3(0, 0, 3));
        frames.SetAngularVelocity(frame, new Vector3(0, 0.2f, 0));
        return frame;
    }

    /// <summary>A full sample 5 m on along +X, turned about Z, with velocities of its own.</summary>
    private static readonly MotionSample Sample = new(new DPose(X(105), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.1)), X(300), X(301),
        new Double3(0, -G, 0), new Double3(0, 0, 0.5), Double3.Zero);

    /// <summary>The intents of a step in which only the owner of <paramref name="frame"/> wrote one, <paramref name="move"/> relative to ground.</summary>
    private PhysicsIntent[] Moving(FrameHandle frame, in MotionIntent move)
    {
        var intents = new PhysicsIntent[frame.Entity.Index + 1];
        intents[frame.Entity.Index] = new PhysicsIntent { Frame = ground, Move = move };
        return intents;
    }

    /// <summary>The pose, the velocities and the follow kind of <paramref name="frame"/>.</summary>
    private (DPose Pose, Double3 Velocity, Vector3 Spin, FollowKind Followed) State(FrameHandle frame) =>
        (frames.TreeLocalPose(frame), frames.TreeLinearVelocity(frame), frames.AngularVelocity(frame), frames.Followed(frame));

    [Fact]
    public void AFrameWithoutAHullTakesTheFullSampleOfItsOwnersMotionIntent()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(Moving(moon, new MotionIntent(Sample)), Step);

        State(moon).Should().Be((Sample.Pose, Sample.Velocity, Sample.AngularVelocity.ToVector3(), FollowKind.Rails));
    }

    [Fact]
    public void AFrameWithoutAHullOnAnEndPoseMovesAtAConstantVelocityFromWhereItStood()
    {
        FrameHandle moon = Child(2, motion: null);
        var end = new DPose(X(101), DQuaternion.Identity);

        frames.TakeStepRecords(Moving(moon, new MotionIntent(new EndPose(end))), Step);

        State(moon).Should().Be((end, X(1) / Step, Vector3.Zero, FollowKind.Kinematic));
        frames.ProperAcceleration(moon).Should().Be(new Vector3(0, G, 0), "an end pose has no acceleration, so the frame feels the gravity reversed");
    }

    [Fact]
    public void AFrameWithAHullTakesTheRecordThatPhysicsReleasedAndNotItsOwnersMotionIntent()
    {
        FrameHandle ship = Child(2, BodyMotion.Kinematic);
        MotionSample released = Sample with { Pose = new DPose(X(104), DQuaternion.Identity), Velocity = X(240) };

        frames.TakeStepRecords(Moving(ship, new MotionIntent(Sample)), Step, new StepOwners().WithHullRecord(ship, released));

        State(ship).Should().Be((released.Pose, released.Velocity, released.AngularVelocity.ToVector3(), FollowKind.Rails));
    }

    [Fact]
    public void AFrameWithAHullThatPhysicsReleasedNoRecordForHoldsStillInItsParent()
    {
        FrameHandle ship = Child(2, BodyMotion.Kinematic);

        frames.TakeStepRecords(Moving(ship, new MotionIntent(Sample)), Step);

        State(ship).Should().Be((new DPose(X(100), DQuaternion.Identity), Double3.Zero, Vector3.Zero, FollowKind.Static),
            "the motion intent of a hull moves the hull in physics, which released no record of it");
        frames.ProperAcceleration(ship).Should().Be(new Vector3(0, G, 0));
    }

    [Fact]
    public void AFrameWithoutAHullWhoseOwnerSteersHoldsStill()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(Moving(moon, new MotionIntent(new SteerTarget(Sample.Pose))), Step);

        State(moon).Should().Be((new DPose(X(100), DQuaternion.Identity), Double3.Zero, Vector3.Zero, FollowKind.Static),
            "a steer target gives no record");
    }

    [Fact]
    public void AFrameWithAHullKeepsItsStateAfterAStepThatFailed()
    {
        FrameHandle ship = Child(2, BodyMotion.Dynamic);
        frames.TakeStepRecords([], Step, new StepOwners().WithHullRecord(ship, Sample));
        var before = State(ship);
        Vector3 proper = frames.ProperAcceleration(ship);

        frames.TakeStepRecords([], Step, stepFailed: true);

        State(ship).Should().Be(before, "physics may have moved the hull or not, so the next step that works moves it on");
        frames.ProperAcceleration(ship).Should().Be(proper);
    }

    [Fact]
    public void AFrameWithoutAHullFollowsItsOwnersMotionIntentAfterAStepThatFailed()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(Moving(moon, new MotionIntent(Sample)), Step, stepFailed: true);

        State(moon).Should().Be((Sample.Pose, Sample.Velocity, Sample.AngularVelocity.ToVector3(), FollowKind.Rails),
            "the sync point moves a frame without a hull, whatever physics did");
    }

    [Fact]
    public void AFrameWithoutAHullOrAMotionIntentHoldsStillAfterAStepThatFailed()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords([], Step, stepFailed: true);

        State(moon).Should().Be((new DPose(X(100), DQuaternion.Identity), Double3.Zero, Vector3.Zero, FollowKind.Static));
    }

    [Fact]
    public void AFrameWhoseOwnersUpdateTheOutputCheckSkippedKeepsItsState()
    {
        FrameHandle ship = Child(2, BodyMotion.Dynamic);
        var before = State(ship);
        var owners = new StepOwners();
        owners.Skipped.Add(ship);

        frames.TakeStepRecords([], Step, owners);

        State(ship).Should().Be(before);
        frames.ProperAcceleration(ship).Should().Be(Vector3.Zero, "the frame kept the value of its spawn");
    }

    [Fact]
    public void ATransformWriteWinsOverTheOwnersMotionIntentAndLeavesTheFrameAtRest()
    {
        FrameHandle moon = Child(2, motion: null);
        var written = new DPose(X(90), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.3));
        var owners = new StepOwners();
        owners.Writes[moon] = written;

        frames.TakeStepRecords(Moving(moon, new MotionIntent(Sample)), Step, owners);

        State(moon).Should().Be((written, Double3.Zero, Vector3.Zero, FollowKind.Static));
        frames.ProperAcceleration(moon).Should().Be(written.Rotation.Conjugate().Rotate(new Double3(0, G, 0)).ToVector3(),
            "the frame holds still at the written place, so it feels the gravity there reversed, in its own axes");
    }

    [Fact]
    public void TheTransformOfTheOwnerOfAFrameWithAHullIsPhysicsBusiness()
    {
        FrameHandle ship = Child(2, BodyMotion.Kinematic);
        var owners = new StepOwners().WithHullRecord(ship, Sample);
        owners.Writes[ship] = new DPose(X(90), DQuaternion.Identity);

        frames.TakeStepRecords(Moving(ship, new MotionIntent(Sample)), Step, owners);

        frames.TreeLocalPose(ship).Should().Be(Sample.Pose, "physics owns the transform of a body, so a hull takes only its record");
    }

    [Fact]
    public void ANumericFrameTakesNoRecord()
    {
        FrameHandle hull = Child(20, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(hull, X(60));
        var body = new NumericBody
        {
            Entity = hull.Entity, Semantic = ground, Home = ground, Position = X(100), HomePosition = X(100), Velocity = X(60),
            Radius = 1, Mass = 1, Character = true, OwnedFrame = hull,
        };
        frames.UpdateNumericFrames([body], 0.1, 0.1, new PhysicsChanges(), _ => { });
        FrameHandle pod = frames.TreeParent(hull);
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        var before = (frames.TreeLocalPose(pod), frames.TreeLinearVelocity(pod));

        frames.TakeStepRecords([], Step, new StepOwners().WithHullRecord(hull, Sample with { Pose = DPose.Identity }));

        (frames.TreeLocalPose(pod), frames.TreeLinearVelocity(pod)).Should().Be(before, "the engine moves a numeric frame by its own rules");
        frames.TreeLocalPose(hull).Should().Be(DPose.Identity, "the hull's frame takes its record relative to the pod, its tree parent");
    }

#if TERTIUS_CONTRACTS
    /// <summary>Owner facts that move <paramref name="frame"/> under <paramref name="parent"/> when the pass asks about it.</summary>
    private readonly struct Mover(FrameTree frames, FrameHandle frame, FrameHandle parent) : IFrameOwnerFacts
    {
        public bool TryGetHullRecord(FrameHandle asked, out FrameStepRecord record)
        {
            if (asked == frame)
                frames.Reparent(frame, parent);
            record = default;
            return false;
        }

        public bool IsSkipped(FrameHandle asked) => false;

        public bool TryGetTransformWrite(FrameHandle asked, out DPose pose)
        {
            pose = default;
            return false;
        }
    }

    // The pass checks it with Contract.Assert, which compiles out of Release builds, so the test runs only in Debug.
    [Fact]
    public void OwnerFactsThatMoveAFrameDuringThePassBreakAContract()
    {
        FrameHandle a = Child(2, motion: null);
        FrameHandle b = Child(3, motion: null);
        var mover = new Mover(frames, b, a);

        Action pass = () => frames.TakeStepRecords(ref mover, [], stepFailed: false, Step);

        pass.Should().Throw<ContractException>();
    }
#endif
}

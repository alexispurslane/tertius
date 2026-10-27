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
public sealed class FrameTreeTakeStepRecordsTests
{
    private const double Step = 1.0 / 60;
    private const float G = 9.81f;

    /// <summary>A full sample 5 m on along +X, turned about Z, with velocities of its own.</summary>
    private static readonly MotionSample Sample = new(
        Pose: new(X(105), DQuaternion.FromAxisAngle(Double3.UnitZ, 0.1)),
        ChordVelocity: X(300),
        Velocity: X(301),
        Acceleration: new(0, -G, 0),
        AngularVelocity: new(0, 0, 0.5),
        AngularAcceleration: Double3.Zero
    );

    private readonly FrameTree frames = new();
    private readonly FrameHandle ground;

    public FrameTreeTakeStepRecordsTests()
    {
        ground = frames.Create(
            frameOwner: new(1, 1),
            parent: FrameHandle.Null,
            localPose: DPose.Identity,
            frameBounds: FrameBounds.Sphere(1e9f),
            field: GravityField.Uniform(new(0, -G, 0))
        );
    }

    private static Double3 X(double x) => new(x, 0, 0);

    /// <summary>A frame of the owner <paramref name="id"/> 100 m along +X in ground, moving at 3 m/s along +Z, with a hull of
    /// <paramref name="motion"/>.</summary>
    private FrameHandle Child(uint id, BodyMotion? motion)
    {
        FrameHandle frame = frames.Create(
            frameOwner: new(id, 1),
            parent: ground,
            localPose: new(X(100), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(10)
        );
        frames.SetHullMotion(frame, motion);
        frames.SetTreeLinearVelocity(frame, new(0, 0, 3));
        frames.SetAngularVelocity(frame, new(0, 0.2f, 0));
        return frame;
    }

    /// <summary>The intents of a step in which only the owner of <paramref name="frame"/> wrote one, <paramref name="move"/>
    /// relative to ground.</summary>
    private PhysicsIntent[] Moving(FrameHandle frame, in MotionIntent move)
    {
        var intents = new PhysicsIntent[frame.Entity.Index + 1];
        intents[frame.Entity.Index] = new() { Frame = ground, Move = move };
        return intents;
    }

    /// <summary>The pose, the velocities and the follow kind of <paramref name="frame"/>.</summary>
    private (DPose Pose, Double3 Velocity, Vector3 Spin, FollowKind Followed) State(FrameHandle frame) =>
        (
            frames.TreeLocalPose(frame),
            frames.TreeLinearVelocity(frame),
            frames.AngularVelocity(frame),
            frames.Followed(frame)
        );

    [Fact]
    public void AFrameWithoutAHullTakesTheFullSampleOfItsOwnersMotionIntent()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(stepIntents: Moving(moon, new(Sample)), step: Step);

        State(moon).Should().Be((Sample.Pose, Sample.Velocity, Sample.AngularVelocity.ToVector3(), FollowKind.Rails));
    }

    [Fact]
    public void AFrameWithoutAHullOnAnEndPoseMovesAtAConstantVelocityFromWhereItStood()
    {
        FrameHandle moon = Child(2, motion: null);
        var end = new DPose(X(101), DQuaternion.Identity);

        frames.TakeStepRecords(stepIntents: Moving(moon, new(new EndPose(end))), step: Step);

        State(moon).Should().Be((end, X(1) / Step, Vector3.Zero, FollowKind.Kinematic));
        frames
            .ProperAcceleration(moon)
            .Should()
            .Be(new Vector3(0, G, 0), "an end pose has no acceleration, so the frame feels the gravity reversed");
    }

    [Fact]
    public void AFrameWithAHullTakesTheRecordThatPhysicsReleasedAndNotItsOwnersMotionIntent()
    {
        FrameHandle ship = Child(2, BodyMotion.Kinematic);
        MotionSample released = Sample with { Pose = new(X(104), DQuaternion.Identity), Velocity = X(240) };

        frames.TakeStepRecords(
            stepIntents: Moving(ship, new(Sample)),
            step: Step,
            owners: new StepOwners().WithHullRecord(ship, released)
        );

        State(ship)
            .Should()
            .Be((released.Pose, released.Velocity, released.AngularVelocity.ToVector3(), FollowKind.Rails));
    }

    [Fact]
    public void AFrameWithAHullThatPhysicsReleasedNoRecordForHoldsStillInItsParent()
    {
        FrameHandle ship = Child(2, BodyMotion.Kinematic);

        frames.TakeStepRecords(stepIntents: Moving(ship, new(Sample)), step: Step);

        State(ship)
            .Should()
            .Be(
                (new(X(100), DQuaternion.Identity), Double3.Zero, Vector3.Zero, FollowKind.Static),
                "the motion intent of a hull moves the hull in physics, which released no record of it"
            );
        frames.ProperAcceleration(ship).Should().Be(new Vector3(0, G, 0));
    }

    [Fact]
    public void AFrameWithoutAHullWhoseOwnerSteersHoldsStill()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(stepIntents: Moving(moon, new(new SteerTarget(Sample.Pose))), step: Step);

        State(moon)
            .Should()
            .Be(
                (new(X(100), DQuaternion.Identity), Double3.Zero, Vector3.Zero, FollowKind.Static),
                "a steer target gives no record"
            );
    }

    [Fact]
    public void AFrameWithAHullKeepsItsStateAfterAStepThatFailed()
    {
        FrameHandle ship = Child(2, BodyMotion.Dynamic);
        frames.TakeStepRecords(stepIntents: [], step: Step, owners: new StepOwners().WithHullRecord(ship, Sample));
        (DPose Pose, Double3 Velocity, Vector3 Spin, FollowKind Followed) before = State(ship);
        Vector3 proper = frames.ProperAcceleration(ship);

        frames.TakeStepRecords(stepIntents: [], step: Step, stepFailed: true);

        State(ship)
            .Should()
            .Be(before, "physics may have moved the hull or not, so the next step that works moves it on");
        frames.ProperAcceleration(ship).Should().Be(proper);
    }

    [Fact]
    public void AFrameWithoutAHullFollowsItsOwnersMotionIntentAfterAStepThatFailed()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(stepIntents: Moving(moon, new(Sample)), step: Step, stepFailed: true);

        State(moon)
            .Should()
            .Be(
                (Sample.Pose, Sample.Velocity, Sample.AngularVelocity.ToVector3(), FollowKind.Rails),
                "the sync point moves a frame without a hull, whatever physics did"
            );
    }

    [Fact]
    public void AFrameWithoutAHullOrAMotionIntentHoldsStillAfterAStepThatFailed()
    {
        FrameHandle moon = Child(2, motion: null);

        frames.TakeStepRecords(stepIntents: [], step: Step, stepFailed: true);

        State(moon).Should().Be((new(X(100), DQuaternion.Identity), Double3.Zero, Vector3.Zero, FollowKind.Static));
    }

    [Fact]
    public void AFrameWhoseOwnersUpdateTheOutputCheckSkippedKeepsItsState()
    {
        FrameHandle ship = Child(2, BodyMotion.Dynamic);
        (DPose Pose, Double3 Velocity, Vector3 Spin, FollowKind Followed) before = State(ship);
        var owners = new StepOwners();
        owners.Skipped.Add(ship);

        frames.TakeStepRecords(stepIntents: [], step: Step, owners: owners);

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

        frames.TakeStepRecords(stepIntents: Moving(moon, new(Sample)), step: Step, owners: owners);

        State(moon).Should().Be((written, Double3.Zero, Vector3.Zero, FollowKind.Static));
        frames
            .ProperAcceleration(moon)
            .Should()
            .Be(
                written.Rotation.Conjugate().Rotate(new Double3(0, G, 0)).ToVector3(),
                "the frame holds still at the written place, so it feels the gravity there reversed, in its own axes"
            );
    }

    [Fact]
    public void TheTransformOfTheOwnerOfAFrameWithAHullIsPhysicsBusiness()
    {
        FrameHandle ship = Child(2, BodyMotion.Kinematic);
        StepOwners owners = new StepOwners().WithHullRecord(ship, Sample);
        owners.Writes[ship] = new(X(90), DQuaternion.Identity);

        frames.TakeStepRecords(stepIntents: Moving(ship, new(Sample)), step: Step, owners: owners);

        frames
            .TreeLocalPose(ship)
            .Should()
            .Be(Sample.Pose, "physics owns the transform of a body, so a hull takes only its record");
    }

    [Fact]
    public void ANumericFrameTakesNoRecord()
    {
        FrameHandle hull = Child(20, BodyMotion.Dynamic);
        frames.SetTreeLinearVelocity(hull, X(60));
        var body = new NumericBody
        {
            Entity = hull.Entity,
            Semantic = ground,
            Home = ground,
            Position = X(100),
            HomePosition = X(100),
            Velocity = X(60),
            Radius = 1,
            Mass = 1,
            Character = true,
            OwnedFrame = hull,
        };
        frames.UpdateNumericFrames(bodies: [body], ticks: 1, tickRate: 10, changes: new(), applyMoves: _ => { });
        FrameHandle pod = frames.TreeParent(hull);
        frames.Numeric(pod).Should().Be(NumericMode.Falling);
        (DPose Pose, Double3 Velocity) before = (frames.TreeLocalPose(pod), frames.TreeLinearVelocity(pod));

        frames.TakeStepRecords(
            stepIntents: [],
            step: Step,
            owners: new StepOwners().WithHullRecord(hull, Sample with { Pose = DPose.Identity })
        );

        (frames.TreeLocalPose(pod), frames.TreeLinearVelocity(pod))
            .Should()
            .Be(before, "the engine moves a numeric frame by its own rules");
        frames
            .TreeLocalPose(hull)
            .Should()
            .Be(DPose.Identity, "the hull's frame takes its record relative to the pod, its tree parent");
    }

    [Fact]
    public void AFrameThatHoldsStillInAFallingPodKeepsThePodsInstantVelocityPastItsChord()
    {
        FrameHandle hull = Child(20, BodyMotion.Dynamic);
        var body = new NumericBody
        {
            Entity = hull.Entity,
            Semantic = ground,
            Home = ground,
            Position = X(100),
            HomePosition = X(100),
            Velocity = X(60),
            Radius = 1,
            Mass = 1,
            Character = true,
            OwnedFrame = hull,
        };
        frames.UpdateNumericFrames(bodies: [body], ticks: 1, tickRate: 10, changes: new(), applyMoves: _ => { });
        frames.Numeric(frames.TreeParent(hull)).Should().Be(NumericMode.Falling);
        frames.Update();
        frames.BeginStep([], Step, lastStepFailed: false);
        frames.AdvanceNumericFrames(Step);

        frames.TakeStepRecords(stepIntents: [], step: Step);

        // The pod's velocity is its chord of the step, and it falls by ground's field, so its instant velocity at the end of
        // the step lies half a step of that fall past the chord.
        Double3 velocity = frames.TreeLinearVelocity(hull);
        velocity.X.Should().Be(0);
        velocity
            .Y.Should()
            .BeApproximately(-G * Step / 2, 1e-12, "a frame at rest in the pod moves with the pod's instant velocity");
        velocity.Z.Should().Be(0);
        frames.Followed(hull).Should().Be(FollowKind.Static);
    }

#if TERTIUS_CONTRACTS
    /// <summary>Owner facts that move <paramref name="frame"/> under <paramref name="parent"/> when the pass asks about it.</summary>
    private readonly struct Mover(FrameTree frames, FrameHandle frame, FrameHandle parent) : IFrameOwnerFacts
    {
        public FrameStepRecord? HullRecord(FrameHandle asked)
        {
            if (asked == frame)
                frames.Reparent(frame, parent);
            return null;
        }

        public bool IsSkipped(FrameHandle asked) => false;

        public DPose? TransformWrite(FrameHandle asked) => null;
    }

    // The pass checks it with Contract.Assert, which compiles out of Release builds, so the test runs only in Debug.
    [Fact]
    public void OwnerFactsThatMoveAFrameDuringThePassBreakAContract()
    {
        FrameHandle a = Child(2, motion: null);
        FrameHandle b = Child(3, motion: null);
        var mover = new Mover(frames, b, a);

        Action pass = () => frames.TakeStepRecords(owners: ref mover, stepIntents: [], stepFailed: false, step: Step);

        pass.Should().Throw<ContractException>();
    }
#endif
}

using AwesomeAssertions;
using Tertius.Frames.Rails;

namespace Tertius.Core.Tests;

/// <summary>
/// The frame tree tells how each frame followed its owner at the last sync point (<see cref="FrameTree.Followed"/>): the
/// follow kind that the frame's row stores beside the record of the step that ended (docs/plans/rails-components.md, "The
/// follow kind"). Step 2 writes it with the record (<see cref="FrameTree.TakeStepRecords"/>), and
/// the tree tells which kind a record of a step has, from the intents that the step applied
/// (<see cref="FrameTree.FollowKindInStep"/>): the record of a hull that was dynamic in the step is physics, the record of a
/// full sample is rails, that of an end pose is kinematic, and any other record is static.
/// </summary>
public class FrameTreeFollowedTests
{
    private const double Step = 1.0 / 60;

    /// <summary>A sample at rest 5 m along +X.</summary>
    private static MotionSample Sample =>
        new(Pose: new(new(5, 0, 0), DQuaternion.Identity), ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Double3.Zero, AngularVelocity: Double3.Zero,
            AngularAcceleration: Double3.Zero);

    private readonly FrameTree frames = new();
    private readonly FrameHandle root;

    public FrameTreeFollowedTests()
    {
        root = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity, frameBounds: FrameBounds.Sphere(1e9f));
    }

    /// <summary>A frame of the owner <paramref name="id"/>, 100 m along +X in <paramref name="parent"/>, with a hull of
    /// <paramref name="motion"/>.</summary>
    private FrameHandle Child(uint id, FrameHandle parent, BodyMotion? motion)
    {
        FrameHandle frame = frames.Create(frameOwner: new(id, 1), parent: parent, localPose: new(new(100, 0, 0), DQuaternion.Identity),
            frameBounds: FrameBounds.Sphere(10));
        frames.SetHullMotion(frame, motion);
        return frame;
    }

    /// <summary>The intents of a step in which only the owner of <paramref name="frame"/> wrote one: <paramref name="intent"/>
    /// .</summary>
    private static PhysicsIntent[] Only(FrameHandle frame, in PhysicsIntent intent)
    {
        var intents = new PhysicsIntent[frame.Entity.Index + 1];
        intents[frame.Entity.Index] = intent;
        return intents;
    }

    /// <summary>
    /// Step 2: <paramref name="frame"/>, which has no hull, takes the record of its owner's full sample, <see cref="Sample"/>,
    /// relative to its parent.
    /// </summary>
    private void FollowRails(FrameHandle frame) =>
        frames.TakeStepRecords(stepIntents: Only(frame, new() { Frame = frames.Parent(frame), Move = new(Sample) }), step: Step);

    [Fact]
    public void ANewFrameFollowedNothing()
    {
        FrameHandle moon = Child(2, root, motion: null);

        frames.Followed(moon).Should().Be(FollowKind.Static);
    }

    [Fact]
    public void AFrameThatTookItsOwnersFullSampleFollowedRails()
    {
        FrameHandle moon = Child(2, root, motion: null);

        FollowRails(moon);

        frames.Followed(moon).Should().Be(FollowKind.Rails);
    }

    [Fact]
    public void AFrameThatTookItsOwnersEndPoseFollowedKinematic()
    {
        FrameHandle moon = Child(2, root, motion: null);

        frames.TakeStepRecords(stepIntents: Only(moon, new() { Frame = root, Move = new(new EndPose(Sample.Pose)) }), step: Step);

        frames.Followed(moon).Should().Be(FollowKind.Kinematic);
    }

    [Fact]
    public void AFrameThatTookTheRecordOfItsDynamicHullFollowedPhysics()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);

        frames.TakeStepRecords(stepIntents: [], step: Step, owners: new StepOwners().WithHullRecord(ship, Sample));

        frames.Followed(ship).Should().Be(FollowKind.Physics);
    }

    [Fact]
    public void AFrameWhoseOwnerWroteItsTransformFollowedStaticWhateverItsMotionIntent()
    {
        FrameHandle moon = Child(2, root, motion: null);
        var owners = new StepOwners();
        owners.Writes[moon] = Sample.Pose;

        frames.TakeStepRecords(stepIntents: Only(moon, new() { Frame = root, Move = new(Sample) }), step: Step, owners: owners);

        frames.Followed(moon).Should().Be(FollowKind.Static, "the write comes last, so it wins for one tick");
    }

    [Fact]
    public void AFrameThatHoldsStillFollowsAsAStaticFrame()
    {
        FrameHandle moon = Child(2, root, motion: null);
        FollowRails(moon);

        frames.TakeStepRecords(stepIntents: [], step: Step);

        frames.Followed(moon).Should().Be(FollowKind.Static, "the frame took no record of the step");
    }

    [Fact]
    public void AFrameThatMovesToAnotherParentKeepsTheKindOfItsLastRecord()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);
        FrameHandle moon = Child(3, ship, motion: null);
        FollowRails(moon);

        frames.Reparent(moon, root);

        frames.Followed(moon).Should()
            .Be(FollowKind.Rails, "the kind describes the step that ended, as the frame's row does");
    }

    [Fact]
    public void AFrameInTheSlotOfADeadFrameFollowsNothingOfTheDeadFrame()
    {
        FrameHandle first = Child(2, root, motion: null);
        FollowRails(first);
        frames.Destroy(first);

        FrameHandle second = Child(3, root, motion: null);

        second.Index.Should().Be(first.Index, "the tree reuses the slot of the dead frame");
        frames.Followed(second).Should().Be(FollowKind.Static);
    }

    [Fact]
    public void AStaleFrameHasNoWayOfFollowing()
    {
        FrameHandle ship = Child(2, root, motion: null);
        frames.Destroy(ship);

        FluentActions.Invoking(() => frames.Followed(ship)).Should().Throw<InvalidOperationException>();
    }

    // ---- The kind of a record of a step ---------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRecordOfAHullThatWasDynamicInTheStepIsPhysicsWithOrWithoutASteerTarget(bool steered)
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);
        MotionIntent move = steered ? new MotionIntent(new SteerTarget(Sample.Pose)) : default;

        frames.FollowKindInStep(ship, Only(ship, new() { Frame = root, Move = move })).Should().Be(FollowKind.Physics);
    }

    [Fact]
    public void TheRecordOfAHullThatBecameDynamicInTheStepIsPhysics()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Kinematic);

        frames.FollowKindInStep(ship, StepIntents.Changing(ship, BodyMotion.Dynamic)).Should().Be(FollowKind.Physics);
    }

    [Fact]
    public void TheRecordOfAHullThatParkedInTheStepIsStatic()
    {
        FrameHandle ship = Child(2, root, BodyMotion.Dynamic);

        frames.FollowKindInStep(ship, StepIntents.Changing(ship, BodyMotion.Static)).Should().Be(FollowKind.Static,
            "the change of motion of the step counts, not the stored motion of the step before");
    }

    [Theory]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(null)]
    public void TheRecordOfAFullSampleIsRailsAndTheRecordOfAnEndPoseIsKinematic(BodyMotion? motion)
    {
        FrameHandle station = Child(2, root, motion);

        frames.FollowKindInStep(station, Only(station, new() { Frame = root, Move = new(Sample) }))
            .Should().Be(FollowKind.Rails);
        frames.FollowKindInStep(station, Only(station, new() { Frame = root, Move = new(new EndPose(Sample.Pose)) }))
            .Should().Be(FollowKind.Kinematic);
    }

    /// <summary>
    /// Physics moves a static hull only by a teleport. A static hull refuses every motion intent, so one in its intent comes
    /// only from a bug, which physics ignores (docs/plans/rails-components.md, "The flow per kind of entity"), and the
    /// record stays static.
    /// </summary>
    [Fact]
    public void TheRecordOfAStaticHullIsStaticWhateverItsMotionIntent()
    {
        FrameHandle dock = Child(2, root, BodyMotion.Static);
        var intent = new PhysicsIntent
        {
            Frame = root, Flags = IntentFlags.Teleport, TeleportPose = Sample.Pose, Move = new(new EndPose(Sample.Pose))
        };

        frames.FollowKindInStep(dock, Only(dock, intent)).Should().Be(FollowKind.Static);
    }

    [Theory]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Static)]
    public void TheRecordOfATeleportWithoutAMotionIntentIsStatic(BodyMotion motion)
    {
        FrameHandle ship = Child(2, root, motion);
        var teleport = new PhysicsIntent { Frame = root, Flags = IntentFlags.Teleport, TeleportPose = Sample.Pose };

        frames.FollowKindInStep(ship, Only(ship, teleport)).Should().Be(FollowKind.Static);
        frames.FollowKindInStep(ship, []).Should()
            .Be(FollowKind.Static, "a slot past the end of the intents holds none");
    }
}

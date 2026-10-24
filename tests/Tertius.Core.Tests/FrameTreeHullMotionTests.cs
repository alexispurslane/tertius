using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The frame tree stores the motion of each frame's hull, the body of the frame's owner in the frame's parent
/// (<see cref="FrameTree.HullMotion"/>): the motion of the last finished step. The World sets it at spawn from the body's
/// description. A frame without a hull has none: a root frame, whose owner's body is only the frame's static interior, a
/// frame whose owner has no body, and a numeric frame. The motion of the step after the last finished step is derived
/// from the intents that this step applies (<see cref="FrameTree.HullMotionInStep"/>).
/// </summary>
public class FrameTreeHullMotionTests
{
    private readonly FrameTree frames = new();
    private readonly FrameHandle root;

    public FrameTreeHullMotionTests()
    {
        root = frames.Create(frameOwner: new(1, 1), parent: FrameHandle.Null, localPose: DPose.Identity, frameBounds: FrameBounds.Sphere(1e9f));
    }

    /// <summary>A frame of the owner <paramref name="id"/>, 100 m along +X in <paramref name="parent"/>.</summary>
    private FrameHandle Child(uint id, FrameHandle parent) =>
        frames.Create(frameOwner: new(id, 1), parent: parent, localPose: new(new(100, 0, 0), DQuaternion.Identity), frameBounds: FrameBounds.Sphere(10));

    [Theory]
    [InlineData(BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Dynamic)]
    public void AFrameWithAParentHasTheHullMotionThatItWasLastGiven(BodyMotion motion)
    {
        FrameHandle ship = Child(2, root);

        frames.SetHullMotion(ship, BodyMotion.Static);
        frames.SetHullMotion(ship, motion);

        frames.HullMotion(ship).Should().Be(motion);
    }

    [Fact]
    public void ANewFrameHasNoHullMotion()
    {
        FrameHandle ship = Child(2, root);

        frames.HullMotion(ship).Should()
            .BeNull("only the World knows whether the owner has a body, and it sets the motion");
    }

    [Fact]
    public void AFrameInTheSlotOfADeadFrameStartsWithoutAHullMotion()
    {
        FrameHandle first = Child(2, root);
        frames.SetHullMotion(first, BodyMotion.Dynamic);
        frames.Destroy(first);

        FrameHandle second = Child(3, root);

        second.Index.Should().Be(first.Index, "the tree reuses the slot of the dead frame");
        frames.HullMotion(second).Should().BeNull();
    }

    [Fact]
    public void ARootFrameHasNoHullMotion()
    {
        frames.SetHullMotion(root, BodyMotion.Static);

        frames.HullMotion(root).Should()
            .BeNull("a root frame has no parent, so its owner's body is only the frame's static interior");
    }

    [Fact]
    public void AFrameWhoseRootParentDiesLosesItsHullMotion()
    {
        FrameHandle ship = Child(2, root);
        frames.SetHullMotion(ship, BodyMotion.Dynamic);

        frames.Destroy(root);

        frames.Parent(ship).IsNull.Should().BeTrue("a child of a root frame that dies becomes a root");
        frames.HullMotion(ship).Should().BeNull("a root frame has no hull");
    }

    // ---- The motion of the running step -----------------------------------------------------------------------------

    [Fact]
    public void TheRunningStepHasTheChangeOfMotionThatItsIntentsHoldForTheOwner()
    {
        FrameHandle ship = Child(2, root);
        frames.SetHullMotion(ship, BodyMotion.Dynamic);

        frames.HullMotionInStep(ship, StepIntents.Changing(ship, BodyMotion.Static)).Should().Be(BodyMotion.Static);
        frames.HullMotion(ship).Should().Be(BodyMotion.Dynamic, "the stored motion describes the last finished step");
    }

    [Fact]
    public void TheRunningStepHasTheStoredMotionWhenItsIntentsHoldNoChangeForTheOwner()
    {
        FrameHandle ship = Child(2, root);
        FrameHandle other = Child(3, root);
        frames.SetHullMotion(ship, BodyMotion.Dynamic);
        frames.SetHullMotion(other, BodyMotion.Dynamic);
        PhysicsIntent[] intents = StepIntents.Changing(other, BodyMotion.Static);
        intents[ship.Entity.Index] = new() { Motion = BodyMotion.Static, Force = Vector3.UnitX };

        frames.HullMotionInStep(ship, intents).Should()
            .Be(BodyMotion.Dynamic, "the owner's intent has no SetMotion flag");
        frames.HullMotionInStep(ship, []).Should()
            .Be(BodyMotion.Dynamic, "a slot past the end of the intents holds none");
    }

    [Fact]
    public void AFrameWithoutAHullHasNoMotionInTheRunningStepWhateverTheIntentsSay()
    {
        FrameHandle room = Child(2, root);

        frames.HullMotionInStep(room, StepIntents.Changing(room, BodyMotion.Static)).Should().BeNull();
        frames.HullMotionInStep(root, StepIntents.Changing(root, BodyMotion.Static)).Should().BeNull();
    }

    [Fact]
    public void AHullMotionReadOfAStaleFrameIsRefused()
    {
        FrameHandle ship = Child(2, root);
        frames.Destroy(ship);

        Action read = () => frames.HullMotion(ship);

        read.Should().Throw<InvalidOperationException>();
    }
}

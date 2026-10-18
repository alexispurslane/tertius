using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The frame tree keeps the motion of each frame's hull, the body of the frame's owner in the frame's parent
/// (<see cref="FrameTree.HullMotion"/>). The World sets it at spawn from the body's description. A frame without a hull has
/// none: a root frame, whose owner's body is only the frame's static interior, a frame whose owner has no body, and a
/// numeric frame.
/// </summary>
public class FrameTreeHullMotionTests
{
    private readonly FrameTree frames = new();
    private readonly FrameHandle root;

    public FrameTreeHullMotionTests() =>
        root = frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));

    /// <summary>A frame of the owner <paramref name="id"/>, 100 m along +X in <paramref name="parent"/>.</summary>
    private FrameHandle Child(uint id, FrameHandle parent) =>
        frames.Create(new Entity(id, 1), parent, new DPose(new Double3(100, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(10));

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

        frames.HullMotion(ship).Should().BeNull("only the World knows whether the owner has a body, and it sets the motion");
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

        frames.HullMotion(root).Should().BeNull("a root frame has no parent, so its owner's body is only the frame's static interior");
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

    [Fact]
    public void AHullMotionReadOfAStaleFrameIsRefused()
    {
        FrameHandle ship = Child(2, root);
        frames.Destroy(ship);

        Action read = () => frames.HullMotion(ship);

        read.Should().Throw<InvalidOperationException>();
    }
}

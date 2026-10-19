using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// <see cref="FrameTree.Reparent"/> moves a frame under a new parent. A frame cannot go under itself or under one of
/// its descendants, and such a move must leave the frame exactly as it was: its parent, its pose and its velocities.
/// </summary>
public class FrameTreeReparentTests
{
    private static readonly Double3 HullVelocity = new(1, 2, 3);
    private static readonly Vector3 HullSpin = new(0, 0.5f, 0);
    private static readonly DPose HullPose = new(new Double3(100, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.3));

    /// <summary>
    /// A root frame, a hull frame inside it that moves with <see cref="HullVelocity"/> and spins with
    /// <see cref="HullSpin"/>, and a cabin frame inside the hull.
    /// </summary>
    private sealed class Ship
    {
        public readonly FrameTree Frames = new();
        public readonly FrameHandle Root;
        public readonly FrameHandle Hull;
        public readonly FrameHandle Cabin;

        public Ship()
        {
            Root = Frames.Create(new Entity(1, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f));
            Hull = Frames.Create(new Entity(2, 1), Root, HullPose, FrameBounds.Sphere(50));
            Cabin = Frames.Create(new Entity(3, 1), Hull, new DPose(new Double3(10, 0, 0), DQuaternion.Identity), FrameBounds.Sphere(5));
            Frames.SetTreeLinearVelocity(Hull, HullVelocity);
            Frames.SetAngularVelocity(Hull, HullSpin);
            Frames.Update();
        }

        public void ShouldBeUnchanged()
        {
            Frames.Parent(Hull).Should().Be(Root);
            Frames.LocalPose(Hull).Should().Be(HullPose);
            Frames.LinearVelocity(Hull).Should().Be(HullVelocity);
            Frames.AngularVelocity(Hull).Should().Be(HullSpin);
        }
    }

    [Fact]
    public void AFrameMovedUnderItsOwnDescendantIsRefusedAndKeepsItsParentPoseAndVelocities()
    {
        var ship = new Ship();

        Action move = () => ship.Frames.Reparent(ship.Hull, ship.Cabin);

        move.Should().Throw<InvalidOperationException>();
        ship.ShouldBeUnchanged();
    }

    [Fact]
    public void AFrameMovedUnderItselfIsRefusedAndKeepsItsParentPoseAndVelocities()
    {
        var ship = new Ship();

        Action move = () => ship.Frames.Reparent(ship.Hull, ship.Hull);

        move.Should().Throw<InvalidOperationException>();
        ship.ShouldBeUnchanged();
    }
}

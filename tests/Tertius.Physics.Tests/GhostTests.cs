using System.Numerics;

namespace Tertius.Physics.Tests;

public class GhostTests
{
    [Fact]
    public void GhostExistsOnlyWhileTheBodyStraddlesTheBubble()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var center = new Double3(50, 0, 0);
        FrameHandle bubble = h.AddFrame(space, new DPose(center, DQuaternion.Identity), FrameBounds.Sphere(20), MotionDriver.Static);
        const float radius = 0.5f;
        Entity ball = h.AddBody(space, Shapes.Ball(radius), Shapes.At(28, 0, 0), velocity: new Vector3(3, 0, 0));

        // Outside and not touching: no ghost.
        h.Step();
        h[ball].Frame.Should().Be(space);
        h.Backend.GhostCount(ball).Should().Be(0);

        // Entered on first touch. Part of the ball is still outside, so the parent holds a ghost.
        h.StepUntil(() => h[ball].Frame == bubble, 60);
        h[ball].Frame.Should().Be(bubble);
        h.Backend.HasGhostIn(ball, space).Should().BeTrue();
        h.Backend.GhostCount(ball).Should().Be(1);

        // Fully inside: the ghost goes away.
        h.StepUntil(() => h[ball].Pose.Position.Length < 20 - radius - 0.1, 60);
        h.Step();
        h.Backend.GhostCount(ball).Should().Be(0);

        // Back out through the boundary: the ghost returns while the ball straddles it...
        h.Intent(ball).Flags = IntentFlags.SetVelocity;
        h.Intent(ball).Velocity = new Vector3(-3, 0, 0);
        h.StepUntil(() => h[ball].Pose.Position.Length > 20 - radius + 0.05, 60);
        h[ball].Frame.Should().Be(bubble);
        h.Backend.HasGhostIn(ball, space).Should().BeTrue();

        // ...and after the exit hand-off the real body replaces it. Outside the margin nothing is ghosted.
        h.StepUntil(() => h[ball].Frame == space, 120);
        h[ball].Frame.Should().Be(space);
        h.Backend.GhostCount(ball).Should().Be(0);
    }

    [Fact]
    public void BodyInParentCollidesWithGhostOfBodyStickingOutOfChild()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle bubble = h.AddFrame(space, DPose.Identity, FrameBounds.Sphere(10), MotionDriver.Static);
        // A kinematic block in the child sits on the bubble's edge and sticks 1 m out of it.
        var blockDesc = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Kinematic);
        Entity block = h.AddBody(bubble, blockDesc, Shapes.At(10, 0, 0));
        // A ball in the parent flies at the part that sticks out. It never touches the child's bubble.
        Entity ball = h.AddBody(space, Shapes.Ball(0.25f), Shapes.At(16, 0, 0), velocity: new Vector3(-5, 0, 0));

        h.Step();
        h.Backend.HasGhostIn(block, space).Should().BeTrue();

        h.Step(90);

        BodyState state = h[ball];
        state.Frame.Should().Be(space);
        state.Pose.Position.X.Should().BeGreaterThan(11.25 - 0.05, "the ball must not go through the ghost");
        state.LinearVelocity.X.Should().BeGreaterThan(-0.5f);
        h.HandOffs.Should().BeEmpty();
        // The ghost pushed, but the block did not move: ghosts never push back on the real body.
        (h[block].Pose.Position - new Double3(10, 0, 0)).Length.Should().BeLessThan(1e-6);
        // Ghost contacts are not reported as collisions.
        h.Contacts.Should().NotContain(c => c.A == block || c.B == block);

        // Move the block fully inside: the ghost goes away.
        h.Intent(block).Flags = IntentFlags.Teleport;
        h.Intent(block).TeleportPose = new DPose(new Double3(5, 0, 0), DQuaternion.Identity);
        h.Step();
        h.Backend.GhostCount(block).Should().Be(0);
    }

    [Fact]
    public void GhostsInADestroyedFrameGoWithIt()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle left = h.AddFrame(space, Shapes.At(-10.6, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static);
        FrameHandle right = h.AddFrame(space, Shapes.At(10.4, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static);
        // The ball enters the right bubble and sticks out of it: space and the left frame hold ghosts of it.
        Entity ball = h.AddBody(space, Shapes.Ball(1f), Shapes.At(0, 0, 0));
        h.Step();
        h[ball].Frame.Should().Be(right);
        h.Backend.HasGhostIn(ball, left).Should().BeTrue();
        h.Backend.GhostCount(ball).Should().Be(2);

        h.Frames.Destroy(left);
        h.Changes.FramesDestroyed.Add(left);
        h.Step(3);

        // The ghost went with the left frame's system; the ball itself is untouched.
        h.Backend.HasGhostIn(ball, left).Should().BeFalse();
        h.Backend.HasGhostIn(ball, space).Should().BeTrue();
        h.Backend.GhostCount(ball).Should().Be(1);
        h[ball].Frame.Should().Be(right);
        h.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void GhostMirrorsTheRealBodyInTheOtherFramesCoordinates()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var rotation = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.7);
        FrameHandle bubble = h.AddFrame(space, new DPose(new Double3(-30, 5, 2), rotation), FrameBounds.Sphere(10), MotionDriver.Static);
        // A kinematic body at the child's edge, sticking out of the bubble.
        var desc = new BodyDesc(new SphereShapeDesc(1), BodyMotion.Kinematic);
        Entity rider = h.AddBody(bubble, desc, Shapes.At(0, 0, 9.8));
        // A probe ball in the parent, placed so that it rests against where the ghost will be.
        Entity probe = h.AddBody(space, Shapes.Ball(0.5f),
            new DPose(new DPose(new Double3(-30, 5, 2), rotation).TransformPoint(new Double3(0, 0, 11.6)), DQuaternion.Identity),
            velocity: (rotation.Rotate(new Vector3(0, 0, -1))));

        h.Step(30);

        // The probe hit the ghost where the real body is, converted into the parent's coordinates, and stopped.
        Double3 probeInChild = h.Frames.PointFromParent(bubble, h[probe].Pose.Position);
        probeInChild.Z.Should().BeGreaterThan(9.8 + 1.5 - 0.05, "the probe stops against the ghost");
        h.Backend.HasGhostIn(rider, space).Should().BeTrue();
    }

    [Fact]
    public void FrameHullsAreGhostedIntoOverlappingSiblingFrames()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // A static station whose hull is a triangle mesh: a wall facing -X at x = 15, inside both bubbles. Jolt
        // ignores the back faces of triangles, so the winding must face the ship.
        var wall = new MeshShapeDesc([new(-15, -5, -5), new(-15, 5, -5), new(-15, 5, 5), new(-15, -5, 5)], [0, 2, 1, 0, 3, 2]);
        FrameHandle station = h.AddFrameWithBody(space, Shapes.At(30, 0, 0), FrameBounds.Sphere(20), MotionDriver.Static,
            new BodyDesc(wall, BodyMotion.Static, InteriorShape: new SphereShapeDesc(1)));
        // A ship next to it, at rest, whose bubble overlaps the station's.
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(20), MotionDriver.Physics,
            new BodyDesc(new SphereShapeDesc(2), Mass: 1000, InteriorShape: new SphereShapeDesc(1)));
        // A crate in the ship flies at the station's wall. It stays in the ship until it leaves the ship's bubble,
        // so it must hit the wall's ghost in the ship's system.
        Entity crate = h.AddBody(ship, Shapes.Box(0.5f), Shapes.At(10, 0, 0), velocity: new Vector3(4, 0, 0));

        h.Step();
        h.Backend.HasGhostIn(station.Entity, ship).Should().BeTrue();
        h.Backend.HasGhostIn(ship.Entity, station).Should().BeTrue();

        h.Step(120);
        h[crate].Frame.Should().Be(ship);
        h[crate].Pose.Position.X.Should().BeInRange(14.5 - 0.6, 14.5 + 0.05);
        h[crate].LinearVelocity.X.Should().BeLessThan(0.5f);
    }
}

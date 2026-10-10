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
        Assert.Equal(space, h[ball].Frame);
        Assert.Equal(0, h.Backend.GhostCount(ball));

        // Entered on first touch. Part of the ball is still outside, so the parent holds a ghost.
        h.StepUntil(() => h[ball].Frame == bubble, 60);
        Assert.Equal(bubble, h[ball].Frame);
        Assert.True(h.Backend.HasGhostIn(ball, space));
        Assert.Equal(1, h.Backend.GhostCount(ball));

        // Fully inside: the ghost goes away.
        h.StepUntil(() => h[ball].Pose.Position.Length < 20 - radius - 0.1, 60);
        h.Step();
        Assert.Equal(0, h.Backend.GhostCount(ball));

        // Back out through the boundary: the ghost returns while the ball straddles it...
        h.Intent(ball).Flags = IntentFlags.SetVelocity;
        h.Intent(ball).Velocity = new Vector3(-3, 0, 0);
        h.StepUntil(() => h[ball].Pose.Position.Length > 20 - radius + 0.05, 60);
        Assert.Equal(bubble, h[ball].Frame);
        Assert.True(h.Backend.HasGhostIn(ball, space));

        // ...and after the exit hand-off the real body replaces it. Outside the margin nothing is ghosted.
        h.StepUntil(() => h[ball].Frame == space, 120);
        Assert.Equal(space, h[ball].Frame);
        Assert.Equal(0, h.Backend.GhostCount(ball));
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
        Assert.True(h.Backend.HasGhostIn(block, space));

        h.Step(90);

        BodyState state = h[ball];
        Assert.Equal(space, state.Frame);
        Assert.True(state.Pose.Position.X > 11.25 - 0.05, $"the ball went through the ghost: {state.Pose.Position}");
        Assert.True(state.LinearVelocity.X > -0.5f, state.LinearVelocity.ToString());
        Assert.Empty(h.HandOffs);
        // The ghost pushed, but the block did not move: ghosts never push back on the real body.
        Assert.True((h[block].Pose.Position - new Double3(10, 0, 0)).Length < 1e-6);
        // Ghost contacts are not reported as collisions.
        Assert.DoesNotContain(h.Contacts, c => c.A == block || c.B == block);

        // Move the block fully inside: the ghost goes away.
        h.Intent(block).Flags = IntentFlags.Teleport;
        h.Intent(block).Target = new DPose(new Double3(5, 0, 0), DQuaternion.Identity);
        h.Step();
        Assert.Equal(0, h.Backend.GhostCount(block));
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
        Assert.True(probeInChild.Z > 9.8 + 1.5 - 0.05, $"probe at {probeInChild} in the child");
        Assert.True(h.Backend.HasGhostIn(rider, space));
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
        Assert.True(h.Backend.HasGhostIn(station.Entity, ship));
        Assert.True(h.Backend.HasGhostIn(ship.Entity, station));

        h.Step(120);
        Assert.Equal(ship, h[crate].Frame);
        Assert.InRange(h[crate].Pose.Position.X, 14.5 - 0.6, 14.5 + 0.05);
        Assert.True(h[crate].LinearVelocity.X < 0.5f);
    }
}

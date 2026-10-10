using System.Numerics;

namespace Tertius.Physics.Tests;

public class HandOffTests
{
    private static Double3 ToParentVelocity(FrameMotionData frame, Double3 positionInFrame, Double3 velocityInFrame) =>
        frame.Pose.TransformDirection(velocityInFrame) + frame.Velocity + Double3.Cross(frame.AngularVelocity, frame.Pose.TransformDirection(positionInFrame));

    private readonly record struct FrameMotionData(DPose Pose, Double3 Velocity, Double3 AngularVelocity);

    [Fact]
    public void BallThrownOutOfSpinningPhysicsShipKeepsItsParentVelocity()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        // A sphere hull keeps its spin constant. No damping anywhere, so the motion is exact.
        var hull = new BodyDesc(new SphereShapeDesc(5), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(2));
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(100, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics, hull,
            velocity: new Vector3(0, 0, 20), angularVelocity: new Vector3(0, 0.2f, 0));
        Entity ball = h.AddBody(ship, Shapes.Ball(0.25f), Shapes.At(8, 0, 0), velocity: new Vector3(6, 0, 1));

        BodyState before = default;
        HandOff? exit = null;
        FrameState shipAtExit = default;
        h.AfterStep = output =>
        {
            foreach (HandOff handOff in output.HandOffs)
                if (handOff.Entity == ball)
                {
                    exit = handOff;
                    shipAtExit = output.Frames.Single(f => f.Frame == ship);
                }
        };
        int ticks = h.StepUntil(() =>
        {
            if (exit is null)
                before = h[ball];
            return exit is not null;
        }, 400);
        Assert.True(exit is not null, $"no hand-off after {ticks} ticks");

        HandOff result = exit!.Value;
        Assert.Equal(ship, result.From);
        Assert.Equal(space, result.To);
        Assert.Equal(space, h[ball].Frame);
        Assert.Equal(ship, before.Frame);

        // In ship coordinates the ball coasts in a straight line (no Coriolis force acts), so at the end of the
        // hand-off step it is one step further along. Its parent velocity includes ω × r of the spinning ship.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * PhysicsHarness.Dt;
        var shipMotion = new FrameMotionData(shipAtExit.LocalPose, shipAtExit.LinearVelocity, shipAtExit.AngularVelocity);
        Double3 expected = ToParentVelocity(shipMotion, position, before.LinearVelocity);
        Double3 actual = result.LinearVelocity;
        Assert.True((actual - expected).Length < 1e-3 * expected.Length, $"expected {expected}, got {actual}");
        Assert.True((result.Pose.Position - shipMotion.Pose.TransformPoint(position)).Length < 1e-3, result.Pose.Position.ToString());

        // The spin term matters: without ω × r the answer would be off by meters per second.
        Double3 withoutSpin = shipMotion.Pose.TransformDirection(before.LinearVelocity) + shipMotion.Velocity;
        Assert.True((withoutSpin - expected).Length > 1);
    }

    [Fact]
    public void BallLeavingOrbitingSpinningKeplerFrameKeepsItsParentVelocity()
    {
        using var h = new PhysicsHarness();
        const double mu = 3.986004418e14;
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(mu));
        var orbit = KeplerOrbit.Circular(7_000_000, mu, phase: 0.3, spinRate: 0.15);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(4)), InteriorShape: new SphereShapeDesc(1));
        FrameHandle station = h.AddFrameWithBody(planet, DPose.Identity, FrameBounds.Sphere(25), MotionDriver.Kepler, hull, orbit: orbit);
        Entity ball = h.AddBody(station, Shapes.Ball(0.2f), Shapes.At(0, 3, 10), velocity: new Vector3(0, 1, 8));

        BodyState before = default;
        HandOff? exit = null;
        h.AfterStep = output =>
        {
            foreach (HandOff handOff in output.HandOffs)
                if (handOff.Entity == ball)
                    exit = handOff;
        };
        h.StepUntil(() =>
        {
            if (exit is null)
                before = h[ball];
            return exit is not null;
        }, 400);
        Assert.True(exit is not null);
        Assert.Equal(station, exit!.Value.From);
        Assert.Equal(planet, exit.Value.To);

        // The harness has advanced the station to the end of the hand-off step, so the tree converts exactly.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * PhysicsHarness.Dt;
        Double3 expected = h.Frames.VelocityToParent(station, position, before.LinearVelocity);
        Double3 actual = exit.Value.LinearVelocity;
        Assert.True((actual - expected).Length < 1e-3 * expected.Length, $"expected {expected}, got {actual}");
        Assert.True((exit.Value.Pose.Position - h.Frames.PointToParent(station, position)).Length < 1e-2);
    }

    [Fact]
    public void BallEntersChildBubbleOnFirstTouchAndDoesNotFlipBack()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var center = new Double3(50, 0, 0);
        FrameHandle bubble = h.AddFrame(space, new DPose(center, DQuaternion.Identity), FrameBounds.Sphere(20), MotionDriver.Static);
        const float radius = 0.5f;
        Entity ball = h.AddBody(space, Shapes.Ball(radius), Shapes.At(25, 0, 0), velocity: new Vector3(5, 0, 0));

        // Approach: the ball enters on the first step that ends with its sphere touching the bubble.
        BodyState last = h[ball];
        h.StepUntil(() =>
        {
            if (h.HandOffs.Count > 0)
                return true;
            last = h[ball];
            return false;
        }, 120);
        Assert.Single(h.HandOffs);
        HandOff entry = h.HandOffs[0];
        Assert.Equal(space, entry.From);
        Assert.Equal(bubble, entry.To);
        Assert.True((last.Pose.Position - center).Length - 20 > radius, "the ball touched the bubble one step earlier");
        double depth = entry.Pose.Position.Length - 20;
        Assert.True(depth <= radius && depth > radius - 5 * PhysicsHarness.Dt - 1e-6, $"signed distance at entry {depth}");
        Assert.True((entry.Pose.Position - (last.Pose.Position + (Double3)last.LinearVelocity * PhysicsHarness.Dt - center)).Length < 1e-6);

        // Turn back slowly. The ball stays in the child until it is fully outside plus the margin.
        h.Intent(ball).Flags = IntentFlags.SetVelocity;
        h.Intent(ball).Velocity = new Vector3(-1, 0, 0);
        FrameBounds bounds = FrameBounds.Sphere(20);
        double margin = bounds.ExitMargin(radius);
        int ticksInside = 0;
        h.StepUntil(() =>
        {
            if (h.HandOffs.Count > 1)
                return true;
            Assert.Equal(bubble, h[ball].Frame);
            Assert.False(bounds.FullyOutside(h[ball].Pose.Position, radius, margin));
            ticksInside++;
            return false;
        }, 200);
        Assert.Equal(2, h.HandOffs.Count);
        HandOff exit = h.HandOffs[1];
        Assert.Equal(bubble, exit.From);
        Assert.Equal(space, exit.To);
        Assert.True(ticksInside > 20, $"left after {ticksInside} ticks");
        Assert.True((exit.Pose.Position - center).Length - 20 > radius + margin);

        // Back in the parent and outside the margin, but not touching: it stays out.
        h.Step(5);
        Assert.Equal(2, h.HandOffs.Count);
        Assert.Equal(space, h[ball].Frame);
    }

    [Fact]
    public void BodyTouchingTwoSiblingBubblesEntersTheDeeperOne()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle left = h.AddFrame(space, Shapes.At(-10.6, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static);
        FrameHandle right = h.AddFrame(space, Shapes.At(10.4, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static);
        // A ball of radius 1 between them touches both bubbles, the right one more deeply.
        Entity ball = h.AddBody(space, Shapes.Ball(1f), Shapes.At(0, 0, 0));

        h.Step();

        Assert.Single(h.HandOffs);
        Assert.Equal(right, h.HandOffs[0].To);
        Assert.Equal(right, h[ball].Frame);
        // It still touches the left bubble, so the left system holds a ghost of it.
        Assert.True(h.Backend.HasGhostIn(ball, left));
    }

    [Fact]
    public void SmallFrameEntersBigFrameButSimilarFramesStaySiblingsAndCollide()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var carrierHull = new BodyDesc(new BoxShapeDesc(new Vector3(20, 10, 60)), Mass: 1e6f, LinearDamping: 0, AngularDamping: 0);
        FrameHandle carrier = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(200), MotionDriver.Physics, carrierHull);
        var cockpitFloor = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(1, 0.1f, 1)), new Pose(new Vector3(0, -0.9f, 0), Quaternion.Identity))]);
        var fighterHull = new BodyDesc(new BoxShapeDesc(new Vector3(2, 1, 4)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: cockpitFloor);
        FrameHandle fighter = h.AddFrameWithBody(space, Shapes.At(260, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics, fighterHull,
            velocity: new Vector3(-60, 0, 0));
        Entity pilot = h.AddBody(fighter, Shapes.Box(0.3f), Shapes.At(0, 0, 0));

        h.StepUntil(() => h.HandOffs.Count > 0, 120);

        HandOff entry = Assert.Single(h.HandOffs);
        Assert.Equal(fighter.Entity, entry.Entity);
        Assert.Equal(space, entry.From);
        Assert.Equal(carrier, entry.To);
        Assert.Equal(carrier, h.Frames.Parent(fighter));
        Assert.True(h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame) && hullFrame == carrier);
        // The fighter's own contents do not move.
        Assert.Equal(fighter, h[pilot].Frame);
        // It entered on first touch: its hull sphere reached the carrier's bubble.
        float hullRadius = h.Backend.BoundingRadius(fighterHull.Shape);
        Assert.InRange(entry.Pose.Position.Length - 200, hullRadius - 60 * PhysicsHarness.Dt - 1e-3, hullRadius);
        Assert.Equal(-60, entry.LinearVelocity.X, 3);

        // The fighter keeps flying inside the carrier frame, and the tree follows the hull.
        h.Step(10);
        Assert.Single(h.HandOffs);
        Assert.Equal(carrier, h[fighter.Entity].Frame);
        Assert.True((h.Frames.LocalPose(fighter).Position - h[fighter.Entity].Pose.Position).Length < 1e-9);
    }

    [Fact]
    public void SimilarSizeFramesDoNotNestAndTheirHullsCollide()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);
        FrameHandle a = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(50), MotionDriver.Physics, hull);
        FrameHandle b = h.AddFrameWithBody(space, Shapes.At(40, 0, 0), FrameBounds.Sphere(60), MotionDriver.Physics, hull,
            velocity: new Vector3(-10, 0, 0));
        // The bubbles overlap from the start, so each hull has a ghost in the other frame.
        h.Step();
        Assert.True(h.Backend.HasGhostIn(a.Entity, b));
        Assert.True(h.Backend.HasGhostIn(b.Entity, a));

        h.Step(240);

        Assert.Empty(h.HandOffs);
        Assert.Equal(space, h.Frames.Parent(a));
        Assert.Equal(space, h.Frames.Parent(b));
        // B hit A: momentum is shared, so both now move in -X and the hulls do not overlap.
        Assert.True(h.Frames.LinearVelocity(a).X < -3, h.Frames.LinearVelocity(a).ToString());
        Assert.True(h.Frames.LinearVelocity(b).X > -7, h.Frames.LinearVelocity(b).ToString());
        double gap = h.Frames.LocalPose(b).Position.X - h.Frames.LocalPose(a).Position.X;
        Assert.True(gap > 9.9, $"hulls overlap: centers {gap} m apart");
        Assert.Contains(h.Contacts, c => (c.A == a.Entity && c.B == b.Entity) || (c.A == b.Entity && c.B == a.Entity));
    }

    [Fact]
    public void CharacterWalkingOutOfAFrameIsRecreatedInTheParent()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        h.AddBody(ground, Shapes.Floor(100), Shapes.At(0, -0.5, 0));
        // A frame for a building on the ground. The same gravity holds inside.
        FrameHandle building = h.AddFrame(ground, Shapes.At(0, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static, GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        // The building's floor reaches past its bubble, so the walker has ground under it until it leaves the frame.
        h.AddBody(building, Shapes.Floor(15), Shapes.At(0, -0.5, 0));
        var body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);
        Entity walker = h.AddBody(building, body, Shapes.At(0, 0.95, 0));
        h.Intent(walker).Flags = IntentFlags.CharacterMove;
        h.Intent(walker).CharacterVelocity = new Vector3(4, 0, 0);

        h.StepUntil(() => h.HandOffs.Count > 0, 300);

        HandOff exit = Assert.Single(h.HandOffs);
        Assert.Equal(building, exit.From);
        Assert.Equal(ground, exit.To);
        Assert.Equal(4, exit.LinearVelocity.X, 1);
        // It keeps walking with the same desired velocity in the parent.
        double x = h[walker].Pose.Position.X;
        h.Step(30);
        Assert.Equal(ground, h[walker].Frame);
        Assert.InRange(h[walker].Pose.Position.X - x, 1.8, 2.2);
        Assert.InRange(h[walker].Pose.Position.Y, 0.85, 1.0);
    }
}

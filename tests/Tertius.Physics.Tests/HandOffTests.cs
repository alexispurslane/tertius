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
        exit.Should().NotBeNull("the ball leaves the ship within {0} ticks", ticks);

        HandOff result = exit!.Value;
        result.From.Should().Be(ship);
        result.To.Should().Be(space);
        h[ball].Frame.Should().Be(space);
        before.Frame.Should().Be(ship);

        // In ship coordinates the ball coasts in a straight line (no Coriolis force acts), so at the end of the
        // hand-off step it is one step further along. Its parent velocity includes ω × r of the spinning ship.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * PhysicsHarness.Dt;
        var shipMotion = new FrameMotionData(shipAtExit.LocalPose, shipAtExit.LinearVelocity, shipAtExit.AngularVelocity);
        Double3 expected = ToParentVelocity(shipMotion, position, before.LinearVelocity);
        Double3 actual = result.LinearVelocity;
        (actual - expected).Length.Should().BeLessThan(1e-3 * expected.Length);
        (result.Pose.Position - shipMotion.Pose.TransformPoint(position)).Length.Should().BeLessThan(1e-3);

        // The spin term matters: without ω × r the answer would be off by meters per second.
        Double3 withoutSpin = shipMotion.Pose.TransformDirection(before.LinearVelocity) + shipMotion.Velocity;
        (withoutSpin - expected).Length.Should().BeGreaterThan(1);
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
        exit.Should().NotBeNull();
        exit!.Value.From.Should().Be(station);
        exit.Value.To.Should().Be(planet);

        // The harness has advanced the station to the end of the hand-off step, so the tree converts exactly.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * PhysicsHarness.Dt;
        Double3 expected = h.Frames.VelocityToParent(station, position, before.LinearVelocity);
        Double3 actual = exit.Value.LinearVelocity;
        (actual - expected).Length.Should().BeLessThan(1e-3 * expected.Length);
        (exit.Value.Pose.Position - h.Frames.PointToParent(station, position)).Length.Should().BeLessThan(1e-2);
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
        HandOff entry = h.HandOffs.Should().ContainSingle().Which;
        entry.From.Should().Be(space);
        entry.To.Should().Be(bubble);
        ((last.Pose.Position - center).Length - 20).Should().BeGreaterThan(radius, "the ball did not touch the bubble one step earlier");
        double depth = entry.Pose.Position.Length - 20;
        depth.Should().BeLessThanOrEqualTo(radius).And.BeGreaterThan(radius - 5 * PhysicsHarness.Dt - 1e-6);
        (entry.Pose.Position - (last.Pose.Position + (Double3)last.LinearVelocity * PhysicsHarness.Dt - center)).Length.Should().BeLessThan(1e-6);

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
            h[ball].Frame.Should().Be(bubble);
            bounds.FullyOutside(h[ball].Pose.Position, radius, margin).Should().BeFalse();
            ticksInside++;
            return false;
        }, 200);
        h.HandOffs.Should().HaveCount(2);
        HandOff exit = h.HandOffs[1];
        exit.From.Should().Be(bubble);
        exit.To.Should().Be(space);
        ticksInside.Should().BeGreaterThan(20);
        ((exit.Pose.Position - center).Length - 20).Should().BeGreaterThan(radius + margin);

        // Back in the parent and outside the margin, but not touching: it stays out.
        h.Step(5);
        h.HandOffs.Should().HaveCount(2);
        h[ball].Frame.Should().Be(space);
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

        h.HandOffs.Should().ContainSingle().Which.To.Should().Be(right);
        h[ball].Frame.Should().Be(right);
        // It still touches the left bubble, so the left system holds a ghost of it.
        h.Backend.HasGhostIn(ball, left).Should().BeTrue();
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

        HandOff entry = h.HandOffs.Should().ContainSingle().Which;
        entry.Entity.Should().Be(fighter.Entity);
        entry.From.Should().Be(space);
        entry.To.Should().Be(carrier);
        h.Frames.Parent(fighter).Should().Be(carrier);
        h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(carrier);
        // The fighter's own contents do not move.
        h[pilot].Frame.Should().Be(fighter);
        // It entered on first touch: its hull sphere reached the carrier's bubble.
        float hullRadius = h.Backend.BoundingRadius(fighterHull.Shape);
        (entry.Pose.Position.Length - 200).Should().BeInRange(hullRadius - 60 * PhysicsHarness.Dt - 1e-3, hullRadius);
        entry.LinearVelocity.X.Should().BeApproximately(-60, 5e-4f);

        // The fighter keeps flying inside the carrier frame, and the tree follows the hull.
        h.Step(10);
        h.HandOffs.Should().ContainSingle();
        h[fighter.Entity].Frame.Should().Be(carrier);
        (h.Frames.LocalPose(fighter).Position - h[fighter.Entity].Pose.Position).Length.Should().BeLessThan(1e-9);
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
        h.Backend.HasGhostIn(a.Entity, b).Should().BeTrue();
        h.Backend.HasGhostIn(b.Entity, a).Should().BeTrue();

        h.Step(240);

        h.HandOffs.Should().BeEmpty();
        h.Frames.Parent(a).Should().Be(space);
        h.Frames.Parent(b).Should().Be(space);
        // B hit A: momentum is shared, so both now move in -X and the hulls do not overlap.
        h.Frames.LinearVelocity(a).X.Should().BeLessThan(-3);
        h.Frames.LinearVelocity(b).X.Should().BeGreaterThan(-7);
        double gap = h.Frames.LocalPose(b).Position.X - h.Frames.LocalPose(a).Position.X;
        gap.Should().BeGreaterThan(9.9, "the hull centers are that far apart when the hulls do not overlap");
        h.Contacts.Should().Contain(c => (c.A == a.Entity && c.B == b.Entity) || (c.A == b.Entity && c.B == a.Entity));
    }

    [Fact]
    public void AHullThatOverlapsTheBubbleOfAFrameInsideItsOwnFrameStaysWhereItIs()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics, Shapes.Box(2, mass: 1000));
        // A big hall inside the ship. The ship's hull would fit in the hall's bubble, and touches it.
        h.AddFrame(ship, Shapes.At(5, 0, 0), FrameBounds.Sphere(30), MotionDriver.Static);

        h.Step(3);

        h.HandOffs.Should().BeEmpty();
        h.Backend.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
    }

    [Fact]
    public void AHullNeverEntersAFrameThatTheTreeKeepsInsideItsOwnFrame()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e6f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics, Shapes.Box(2, mass: 1000));
        // A big annex of the ship, whose hull is outside the ship's bubble, so the first step hands the annex out into
        // space. The simulation keeps the annex inside the ship, so physics still holds that hand-off as pending and,
        // from the second step on, sees the annex beside the ship. The ship's hull would fit in the annex's bubble,
        // and touches it.
        FrameHandle annex = h.AddFrameWithBody(ship, Shapes.At(33, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics, Shapes.Box(1, mass: 10));
        h.TakesHandOff = handOff => handOff.Entity != annex.Entity;

        h.Step(3);

        h.Frames.Parent(annex).Should().Be(ship);
        h.HandOffs.Should().NotContain(handOff => handOff.Entity == ship.Entity, "the tree keeps the annex inside the ship");
        h.RefusedHandOffs.Should().ContainSingle().Which.Entity.Should().Be(annex.Entity);
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

        HandOff exit = h.HandOffs.Should().ContainSingle().Which;
        exit.From.Should().Be(building);
        exit.To.Should().Be(ground);
        exit.LinearVelocity.X.Should().BeApproximately(4, 0.05f);
        // It keeps walking with the same desired velocity in the parent.
        double x = h[walker].Pose.Position.X;
        h.Step(30);
        h[walker].Frame.Should().Be(ground);
        (h[walker].Pose.Position.X - x).Should().BeInRange(1.8, 2.2);
        h[walker].Pose.Position.Y.Should().BeInRange(0.85, 1.0);
    }
}

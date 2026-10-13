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
        var orbit = KeplerOrbit.Circular(7_000_000, phase: 0.3, spinRate: 0.15);
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
    public void ABallThatLeavesAStationFacingItsPlanetNearApoapsisTakesTheStationsTurnRateThere()
    {
        using var h = new PhysicsHarness();
        const double mu = 5e9;
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(mu));
        // Periapsis at t = 0 and apoapsis at half the 31.4 s period. The station turns at 0.389 rad/s at periapsis and
        // at 0.113 rad/s at apoapsis.
        var orbit = new KeplerOrbit(5000, 0.3, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(4)), InteriorShape: new SphereShapeDesc(1));
        FrameHandle station = h.AddFrameWithBody(planet, DPose.Identity, FrameBounds.Sphere(25), MotionDriver.Kepler, hull, orbit: orbit);
        // The ball drifts out along +Z at 1 m/s and leaves the bubble about 15.7 s later, near apoapsis.
        Entity ball = h.AddBody(station, Shapes.Ball(0.2f), Shapes.At(0, 0, 10), velocity: new Vector3(0, 0, 1));

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
        }, 1200);
        exit.Should().NotBeNull();
        h.Time.Should().BeInRange(orbit.Period(mu) / 2 - 0.5, orbit.Period(mu) / 2 + 0.5);

        // The harness has advanced the station to the end of the hand-off step, with the turn rate of that time.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * PhysicsHarness.Dt;
        Double3 expected = h.Frames.VelocityToParent(station, position, before.LinearVelocity);
        (exit!.Value.LinearVelocity - expected).Length.Should().BeLessThan(0.05);

        // The turn rate matters: the rate at periapsis would be off by about 7 m/s.
        FrameMotionData atPeriapsis = new(h.Frames.LocalPose(station), h.Frames.LinearVelocity(station), orbit.AngularVelocityAt(0, mu));
        (ToParentVelocity(atPeriapsis, position, before.LinearVelocity) - expected).Length.Should().BeGreaterThan(5);
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
        // A big annex of the ship, whose hull is outside the ship's bubble, so every step hands the annex out into
        // space. The test turns each of those reports into a move of the annex into itself, as a bug would, so the
        // harness refuses it and the tree keeps the annex inside the ship. Beside the ship, the annex's bubble would
        // touch the ship's hull, which would fit in it.
        FrameHandle annex = h.AddFrameWithBody(ship, Shapes.At(33, 0, 0), FrameBounds.Sphere(30), MotionDriver.Physics, Shapes.Box(1, mass: 10));
        h.BeforeApply = output => PhysicsHarness.ReplaceHandOffs(output, annex.Entity, handOff => handOff with { To = annex });
        h.RefusesHandOff = handOff => handOff.Entity == annex.Entity;

        h.Step(3);

        h.Frames.Parent(annex).Should().Be(ship);
        h.HandOffs.Should().NotContain(handOff => handOff.Entity == ship.Entity, "the tree keeps the annex inside the ship");
        // Physics follows the tree, so each step starts with the annex's hull back in the ship's system.
        h.RefusedHandOffs.Should().HaveCount(3).And.OnlyContain(handOff => handOff.Entity == annex.Entity && handOff.From == ship);
    }

    /// <summary>
    /// A carrier bubble of radius 200 m, 1 km along +X from the origin of space, and a fighter 1,260 m along +X that
    /// flies at 60 m/s toward the carrier, whose bubble it fits in.
    /// </summary>
    private static (FrameHandle Space, FrameHandle Carrier, FrameHandle Fighter) FighterApproachingCarrier(PhysicsHarness h)
    {
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle carrier = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(200), MotionDriver.Static);
        FrameHandle fighter = h.AddFrameWithBody(space, Shapes.At(1260, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
        return (space, carrier, fighter);
    }

    [Fact]
    public void AHullWhoseHandOffTheSimulationRefusedIsBackInTheTreesParentOnTheNextStep()
    {
        using var h = new PhysicsHarness();
        var (space, carrier, fighter) = FighterApproachingCarrier(h);
        // The test gives the report of the fighter's hand-off a wrong origin, as a bug would, so the harness refuses it.
        h.BeforeApply = output => PhysicsHarness.ReplaceHandOffs(output, fighter.Entity, handOff => handOff with { From = carrier });
        h.RefusesHandOff = handOff => handOff.Entity == fighter.Entity;
        h.StepUntil(() => h.RefusedHandOffs.Count > 0, 120);
        h.RefusedHandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);

        // The tree still has the fighter in space, where it was before the refused step. It stops there, so the next
        // step does not take it into the carrier's bubble again.
        DPose inSpace = h.Frames.LocalPose(fighter);
        h.Intent(fighter.Entity).Flags = IntentFlags.SetVelocity;
        h.Intent(fighter.Entity).Velocity = Vector3.Zero;
        FrameState reported = default;
        h.AfterStep = output => reported = output.Frames.Single(state => state.Frame == fighter);
        h.Step();

        h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        h[fighter.Entity].Frame.Should().Be(space);
        (h[fighter.Entity].Pose.Position - inSpace.Position).Length.Should().BeLessThan(1e-6);
        // Relative to the carrier, the fighter would be about 1 km closer to the origin.
        (reported.LocalPose.Position - inSpace.Position).Length.Should().BeLessThan(1e-6);
        h.RefusedHandOffs.Should().ContainSingle();
    }

    [Fact]
    public void PhysicsFollowsAFrameThatTheGameReparentsRightAfterItsHandOff()
    {
        using var h = new PhysicsHarness();
        var (space, carrier, fighter) = FighterApproachingCarrier(h);
        h.StepUntil(() => h.HandOffs.Count > 0, 120);
        h.Frames.Parent(fighter).Should().Be(carrier);

        // Between steps, with no change batch, the game moves the fighter back into space and turns it around at
        // 120 m/s, fast enough that it leaves the carrier's bubble in one step.
        h.Frames.Reparent(fighter, space);
        h.Frames.TreeLinearVelocity(fighter) = new Double3(120, 0, 0);
        h.Frames.Update();
        Double3 expected = h.Frames.LocalPose(fighter).Position + new Double3(120 * (double)PhysicsHarness.Dt, 0, 0);
        FrameState reported = default;
        h.AfterStep = output => reported = output.Frames.Single(state => state.Frame == fighter);
        h.Step();

        h.Backend.TryGetBodyFrame(fighter.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        h[fighter.Entity].Frame.Should().Be(space);
        (h[fighter.Entity].Pose.Position - expected).Length.Should().BeLessThan(1e-5);
        (reported.LocalPose.Position - expected).Length.Should().BeLessThan(1e-5);
        reported.LinearVelocity.X.Should().BeApproximately(120, 1e-3);
        h.HandOffs.Should().ContainSingle("the fighter is out of the carrier's bubble");
    }

    [Fact]
    public void AHullThatTheGameMovesOutOfTheFrameItEnteredNeverLeavesThatFrameForItsOwnFrame()
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle hangar = h.AddFrame(space, Shapes.At(1000, 0, 0), FrameBounds.Sphere(30), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(1040, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(-60, 0, 0));
        h.StepUntil(() => h.HandOffs.Count > 0, 60);
        h.HandOffs.Should().ContainSingle().Which.To.Should().Be(hangar);

        // Between steps, with no change batch, the game moves the ship back into space and the hangar under the ship.
        // Both moves are legal. The hangar's parent is now the ship.
        h.Frames.Reparent(ship, space);
        h.Frames.Reparent(hangar, ship);
        h.Frames.Update();
        // At 60 m/s the hull needs about 70 steps to cross the hangar's bubble and leave it on the far side. A hull that
        // physics kept in the hangar's system would then leave it for the hangar's parent: the ship's own frame.
        h.Step(90);

        h.HandOffs.Should().ContainSingle("the ship has no other frame to enter or leave");
        h.Backend.TryGetBodyFrame(ship.Entity, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
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

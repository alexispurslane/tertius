using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Hand-offs of bodies and frames between the physics systems of frames, on the World and the real backend. A test reads
/// the hand-offs that Jolt really sent from <see cref="FaultInjectingBackend.Outputs"/>, and checks that the World applied
/// them. A fast test that checks which system a hull leaves or lands in turns the numeric frames of the World off, so that
/// no numeric frame, which would be the frame that Jolt names, comes into play under any membership rules. The comment at
/// the start of such a test says so.
/// </summary>
public class HandOffTests
{
    private const float Dt = 1f / 60f;

    /// <summary>A world on the fault-injecting backend with no numeric frames.</summary>
    private static World NewWorldWithoutNumericFrames(out FaultInjectingBackend faults, List<string>? warnings = null) =>
        NewWorldWithFaults(out faults, warnings, configure: options => options.NumericFrames = WithoutNumericFrames);

    private static Double3 ToParentVelocity(FrameMotionData frame, Double3 positionInFrame, Double3 velocityInFrame) =>
        frame.Pose.TransformDirection(velocityInFrame) + frame.Velocity + Double3.Cross(frame.AngularVelocity, frame.Pose.TransformDirection(positionInFrame));

    private readonly record struct FrameMotionData(DPose Pose, Double3 Velocity, Double3 AngularVelocity);

    /// <summary>Where an entity is and how it moves, as the World holds it after a tick: in its own frame.</summary>
    private readonly record struct Placed(FrameHandle Frame, DPose Pose, Vector3 LinearVelocity);

    private static Placed PlacedNow(World world, Entity entity) =>
        new(world.GetFrame(entity), world.PoseInOwnFrame(entity), world.VelocityInOwnFrame(entity));

    /// <summary>A body of a box with sides of 2 · <paramref name="half"/> m that nothing slows down, and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half, float mass = 1f) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A ball that nothing slows down and that does not bounce.</summary>
    private static BodyDesc BallBody(float radius) =>
        new(new SphereShapeDesc(radius), Mass: 1, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>A static floor of <paramref name="half"/> m to each side, 1 m thick.</summary>
    private static BodyDesc FloorBody(float half) => new(new BoxShapeDesc(new Vector3(half, 0.5f, half)), BodyMotion.Static);

    private static FrameHandle Space(World world, float radius = 1e7f) =>
        world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(radius)));

    /// <summary>A frame without a body, as a child of <paramref name="parent"/>.</summary>
    private static FrameHandle Bubble(World world, FrameHandle parent, DPose pose, float radius, GravityField gravity = default) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = parent,
            Pose = pose,
            AsFrame = new FrameSpec(FrameBounds.Sphere(radius), gravity),
        }));

    /// <summary>The hand-offs that Jolt sent, in the order of the steps.</summary>
    private static List<HandOff> HandOffsSent(FaultInjectingBackend faults) => faults.Outputs.SelectMany(output => output.HandOffs).ToList();

    /// <summary>
    /// Ticks until the output of a step holds a hand-off of <paramref name="entity"/>, at most <paramref name="limit"/>
    /// times. Returns the hand-off, or null. <paramref name="before"/> is the state of the entity before the last tick.
    /// </summary>
    private static HandOff? TickUntilHandedOff(World world, FaultInjectingBackend faults, Entity entity, int limit, out Placed before)
    {
        before = PlacedNow(world, entity);
        for (int i = 0; i < limit; i++)
        {
            before = PlacedNow(world, entity);
            world.Tick();
            foreach (HandOff handOff in faults.Outputs[^1].HandOffs)
                if (handOff.Entity == entity)
                    return handOff;
        }
        return null;
    }

    [Fact]
    public void BallThrownOutOfSpinningPhysicsShipKeepsItsParentVelocity()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        // A sphere hull keeps its spin constant. No damping anywhere, so the motion is exact.
        var hull = new BodyDesc(new SphereShapeDesc(5), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: new SphereShapeDesc(2));
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(100),
            LinearVelocity = new Vector3(0, 0, 20),
            AngularVelocity = new Vector3(0, 0.2f, 0),
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        Entity ball = world.Spawn(new SpawnDesc { Frame = ship, Pose = At(8), LinearVelocity = new Vector3(6, 0, 1), Body = BallBody(0.25f) });

        HandOff? exit = TickUntilHandedOff(world, faults, ball, 400, out Placed before);
        exit.Should().NotBeNull("the ball leaves the ship within 400 ticks");
        FrameStepRecord shipAtExit = faults.Outputs[^1].Frames.Single(f => f.Frame == ship);

        HandOff result = exit!.Value;
        result.From.Should().Be(ship);
        result.To.Should().Be(space);
        before.Frame.Should().Be(ship);
        // The World applied the hand-off that Jolt sent.
        world.GetFrame(ball).Should().Be(space);
        world.PoseInOwnFrame(ball).Should().Be(result.Pose);
        world.VelocityInOwnFrame(ball).Should().Be(result.LinearVelocity);

        // In ship coordinates the ball coasts in a straight line (no Coriolis force acts), so at the end of the
        // hand-off step it is one step further along. Its parent velocity includes ω × r of the spinning ship.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * Dt;
        var shipMotion = new FrameMotionData(shipAtExit.Motion.Pose, shipAtExit.Motion.Velocity, shipAtExit.Motion.AngularVelocity);
        Double3 expected = ToParentVelocity(shipMotion, position, before.LinearVelocity);
        Double3 actual = result.LinearVelocity;
        (actual - expected).Length.Should().BeLessThan(1e-3 * expected.Length);
        (result.Pose.Position - shipMotion.Pose.TransformPoint(position)).Length.Should().BeLessThan(1e-3);

        // The spin term matters: without ω × r the answer would be off by meters per second.
        Double3 withoutSpin = shipMotion.Pose.TransformDirection(before.LinearVelocity) + shipMotion.Velocity;
        (withoutSpin - expected).Length.Should().BeGreaterThan(1);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void BallLeavingOrbitingSpinningKeplerFrameKeepsItsParentVelocity()
    {
        // At 7.5 km/s the station's hull would rest in an orbit frame of its own, which would be the frame that Jolt names.
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        const double mu = 3.986004418e14;
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), GravityField.Point(mu)));
        var orbit = KeplerOrbit.Circular(7_000_000, phase: 0.3, spinRate: 0.15);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(4)), BodyMotion.Kinematic, InteriorShape: new SphereShapeDesc(1));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(25)),
        }.OnKeplerOrbit(world, orbit)));
        Entity ball = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 3, 10), LinearVelocity = new Vector3(0, 1, 8), Body = BallBody(0.2f) });

        HandOff? exit = TickUntilHandedOff(world, faults, ball, 400, out Placed before);
        exit.Should().NotBeNull();
        exit!.Value.From.Should().Be(station);
        exit.Value.To.Should().Be(planet);
        // The World applied the hand-off that Jolt sent.
        world.GetFrame(ball).Should().Be(planet);
        world.PoseInOwnFrame(ball).Should().Be(exit.Value.Pose);
        world.VelocityInOwnFrame(ball).Should().Be(exit.Value.LinearVelocity);

        // The World has advanced the station to the end of the hand-off step, so the tree converts exactly.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * Dt;
        Double3 expected = world.Frames.VelocityToParent(station, position, before.LinearVelocity);
        Double3 actual = exit.Value.LinearVelocity;
        (actual - expected).Length.Should().BeLessThan(1e-3 * expected.Length);
        (exit.Value.Pose.Position - world.Frames.PointToParent(station, position)).Length.Should().BeLessThan(1e-2);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ABallThatLeavesAStationFacingItsPlanetNearApoapsisTakesTheStationsTurnRateThere()
    {
        // The station's hull would rest in an orbit frame of its own, which would be the frame that Jolt names: it gets one
        // at 1,363 m/s at periapsis, faster than the 1,000 m/s from which a free hull gets one, and keeps it at 734 m/s near
        // apoapsis, faster than the 400 m/s down to which it keeps one. The test after this one runs the scene with numeric
        // frames.
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        const double mu = 5e9;
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(mu)));
        // Periapsis at t = 0 and apoapsis at half the 31.4 s period. The station turns at 0.389 rad/s at periapsis and
        // at 0.113 rad/s at apoapsis.
        var orbit = new KeplerOrbit(5000, 0.3, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(4)), BodyMotion.Kinematic, InteriorShape: new SphereShapeDesc(1));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(25)),
        }.OnKeplerOrbit(world, orbit, firstMove: true)));
        // The ball drifts out along +Z at 1 m/s and leaves the bubble about 15.7 s later, near apoapsis.
        Entity ball = world.Spawn(new SpawnDesc { Frame = station, Pose = At(0, 0, 10), LinearVelocity = new Vector3(0, 0, 1), Body = BallBody(0.2f) });

        HandOff? exit = TickUntilHandedOff(world, faults, ball, 1200, out Placed before);
        exit.Should().NotBeNull();
        world.Time.Should().BeInRange(orbit.Period(mu) / 2 - 0.5, orbit.Period(mu) / 2 + 0.5);
        world.GetFrame(ball).Should().Be(planet);
        world.VelocityInOwnFrame(ball).Should().Be(exit!.Value.LinearVelocity, "the World applied the hand-off that Jolt sent");

        // The World has advanced the station to the end of the hand-off step, with the turn rate of that time.
        Double3 position = before.Pose.Position + (Double3)before.LinearVelocity * Dt;
        Double3 instant = world.Frames.VelocityToParent(station, position, before.LinearVelocity);
        // Jolt gets the chord of that instant velocity: half a step of the planet's gravity, 118 m/s² here, behind it.
        Double3 expected = instant - GravityField.Point(mu).AtPrecise(world.Frames.PointToParent(station, position)) * (Dt / 2.0);
        (exit.Value.LinearVelocity - expected).Length.Should().BeLessThan(0.05);

        // The turn rate matters: the rate at periapsis would be off by about 7 m/s.
        FrameMotionData atPeriapsis = new(world.Frames.LocalPose(station), world.Frames.LinearVelocity(station), orbit.AngularVelocityAt(0, mu));
        (ToParentVelocity(atPeriapsis, position, before.LinearVelocity) - expected).Length.Should().BeGreaterThan(5);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ball of the test above, which leaves a station that faces its planet near apoapsis, runs on for a second after
    /// it leaves. With numeric frames the station's hull rests in an orbit frame of its own (ter-dhkf rule 3), the station's
    /// frame turns at the frame's origin, and the ball leaves into that orbit frame, the station's tree parent. The ball is
    /// 25 m from the hull then, farther than the merge test reaches, so the update of the same sync point moves it into a
    /// falling frame of its own. Every read of the ball in the planet is the same as without numeric frames, to the
    /// rounding of the hand-off's floats.
    /// <para>
    /// The ball leaves at about 734 m/s relative to the planet, below the 1,000 m/s from which a free body gets a falling
    /// frame. So it carries a gentle thruster (<see cref="Thruster"/>) along its drift, in both runs, and thrusts from its
    /// thirtieth tick on: a group with a body that thrusts falls from 50 m/s.
    /// </para>
    /// </summary>
    [Fact]
    public void ABallThatLeavesAStationFacingItsPlanetMovesInThePlanetAlikeWithNumericFramesAndWithout()
    {
        (Double3 Position, Double3 Velocity, string Home) Run(bool numericFrames)
        {
            using World world = numericFrames ? NewWorldWithFaults(out _) : NewWorldWithoutNumericFrames(out _);
            const double mu = 5e9;
            FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(mu)));
            var orbit = new KeplerOrbit(5000, 0.3, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);
            FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
            {
                Frame = planet,
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(4)), BodyMotion.Kinematic, InteriorShape: new SphereShapeDesc(1)),
                AsFrame = new FrameSpec(FrameBounds.Sphere(25)),
            }.OnKeplerOrbit(world, orbit, firstMove: true)));
            BodyDesc ballBody = BallBody(0.2f);
            Entity ball = world.Spawn(new SpawnDesc
            {
                Frame = station,
                Pose = At(0, 0, 10),
                LinearVelocity = new Vector3(0, 0, 1),
                Body = ballBody,
                Components = [new Thruster(ballBody.Mass) { Direction = Vector3.UnitZ }],
            });
            TickUntil(world, () => world.GetFrame(ball) == planet, 1200);
            if (numericFrames)
            {
                world.Frames.Numeric(world.Frames.TreeParent(station)).Should().Be(NumericMode.Orbiting,
                    "the station's hull rests in an orbit frame of its own when the ball leaves");
                world.Frames.Numeric(world.PhysicsHomeOf(ball)).Should().Be(NumericMode.Falling,
                    "the sync point of the hand-off moves the ball into a falling frame of its own");
                world.PhysicsHomeOf(ball).Should().NotBe(world.Frames.TreeParent(station));
            }
            Tick(world, 60);
            world.Errors.Should().BeEmpty();
            FrameHandle home = world.PhysicsHomeOf(ball);
            return (world.PoseIn(ball, planet).Position, world.VelocityIn(ball, planet), home == planet ? "planet" : world.Frames.Numeric(home).ToString());
        }

        var with = Run(numericFrames: true);
        var without = Run(numericFrames: false);
        with.Home.Should().Be("Falling", "the ball falls in a frame of its own apart from the hull");
        without.Home.Should().Be("planet");
        (with.Position - without.Position).Length.Should().BeLessThan(1e-3);
        (with.Velocity - without.Velocity).Length.Should().BeLessThan(1e-3);
    }

    [Fact]
    public void BallEntersChildBubbleOnFirstTouchAndDoesNotFlipBack()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var center = new Double3(50, 0, 0);
        FrameHandle bubble = Bubble(world, space, new DPose(center, DQuaternion.Identity), 20);
        const float radius = 0.5f;
        var pilot = new Pilot();
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(25),
            LinearVelocity = new Vector3(5, 0, 0),
            Body = BallBody(radius),
            Components = [pilot],
        });

        // Approach: the ball enters on the first step that ends with its sphere touching the bubble.
        Placed last = PlacedNow(world, ball);
        TickUntil(world, () =>
        {
            if (HandOffsSent(faults).Count > 0)
                return true;
            last = PlacedNow(world, ball);
            return false;
        }, 120);
        HandOff entry = HandOffsSent(faults).Should().ContainSingle().Which;
        entry.From.Should().Be(space);
        entry.To.Should().Be(bubble);
        world.GetFrame(ball).Should().Be(bubble);
        ((last.Pose.Position - center).Length - 20).Should().BeGreaterThan(radius, "the ball did not touch the bubble one step earlier");
        double depth = entry.Pose.Position.Length - 20;
        depth.Should().BeLessThanOrEqualTo(radius).And.BeGreaterThan(radius - 5 * Dt - 1e-6);
        (entry.Pose.Position - (last.Pose.Position + (Double3)last.LinearVelocity * Dt - center)).Length.Should().BeLessThan(1e-6);

        // Turn back slowly. The ball stays in the child until it is fully outside plus the margin. A body's intent acts in
        // the step of the tick after the one that writes it, so the turn starts one step later than the tick that sets it.
        pilot.Once = body => body.SetVelocity(new Double3(-1, 0, 0));
        world.Tick();
        FrameBounds bounds = FrameBounds.Sphere(20);
        double margin = bounds.ExitMargin(radius);
        int ticksInside = 0;
        TickUntil(world, () =>
        {
            if (HandOffsSent(faults).Count > 1)
                return true;
            world.GetFrame(ball).Should().Be(bubble);
            bounds.FullyOutside(world.PoseInOwnFrame(ball).Position, radius, margin).Should().BeFalse();
            ticksInside++;
            return false;
        }, 300);
        HandOffsSent(faults).Should().HaveCount(2);
        HandOff exit = HandOffsSent(faults)[1];
        exit.From.Should().Be(bubble);
        exit.To.Should().Be(space);
        ticksInside.Should().BeGreaterThan(20);
        ((exit.Pose.Position - center).Length - 20).Should().BeGreaterThan(radius + margin);

        // Back in the parent and outside the margin, but not touching: it stays out.
        Tick(world, 5);
        HandOffsSent(faults).Should().HaveCount(2);
        world.GetFrame(ball).Should().Be(space);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void BodyTouchingTwoSiblingBubblesEntersTheDeeperOne()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle left = Bubble(world, space, At(-10.6), 10);
        FrameHandle right = Bubble(world, space, At(10.4), 10);
        // A ball of radius 1 between them touches both bubbles, the right one more deeply.
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0), Body = BallBody(1f) });

        world.Tick();

        HandOffsSent(faults).Should().ContainSingle().Which.To.Should().Be(right);
        world.GetFrame(ball).Should().Be(right);
        // It still touches the left bubble, so the left system holds a ghost of it.
        faults.Jolt.HasGhostIn(ball, left).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SmallFrameEntersBigFrameButSimilarFramesStaySiblingsAndCollide()
    {
        // Numeric frames off: the test checks that the fighter's hull leaves the system of space.
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var carrierHull = new BodyDesc(new BoxShapeDesc(new Vector3(20, 10, 60)), Mass: 1e6f, LinearDamping: 0, AngularDamping: 0);
        Entity carrierOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0),
            Body = carrierHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(200)),
        });
        FrameHandle carrier = world.GetOwnedFrame(carrierOwner);
        var cockpitFloor = new CompoundShapeDesc([new CompoundChild(new BoxShapeDesc(new Vector3(1, 0.1f, 1)), new Pose(new Vector3(0, -0.9f, 0), Quaternion.Identity))]);
        var fighterHull = new BodyDesc(new BoxShapeDesc(new Vector3(2, 1, 4)), Mass: 1000, LinearDamping: 0, AngularDamping: 0, InteriorShape: cockpitFloor);
        Entity fighterOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(260),
            LinearVelocity = new Vector3(-60, 0, 0),
            Body = fighterHull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle fighter = world.GetOwnedFrame(fighterOwner);
        Entity pilot = world.Spawn(new SpawnDesc { Frame = fighter, Pose = At(0), Body = BoxBody(0.3f) });

        TickUntil(world, () => HandOffsSent(faults).Count > 0, 120);

        HandOff entry = HandOffsSent(faults).Should().ContainSingle().Which;
        entry.Entity.Should().Be(fighterOwner);
        entry.From.Should().Be(space);
        entry.To.Should().Be(carrier);
        world.Frames.Parent(fighter).Should().Be(carrier);
        world.GetFrame(fighterOwner).Should().Be(carrier);
        faults.Jolt.TryGetBodyFrame(fighterOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(carrier);
        // The fighter's own contents do not move.
        world.GetFrame(pilot).Should().Be(fighter);
        // It entered on first touch: its hull sphere reached the carrier's bubble.
        float hullRadius = fighterHull.Shape.BoundingRadius();
        (entry.Pose.Position.Length - 200).Should().BeInRange(hullRadius - 60 * Dt - 1e-3, hullRadius);
        entry.LinearVelocity.X.Should().BeApproximately(-60, 5e-4f);

        // The fighter keeps flying inside the carrier frame, and the tree follows the hull.
        Tick(world, 10);
        HandOffsSent(faults).Should().ContainSingle();
        world.GetFrame(fighterOwner).Should().Be(carrier);
        (world.Frames.LocalPose(fighter).Position - world.PoseInOwnFrame(fighterOwner).Position).Length.Should().BeLessThan(1e-9);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void SimilarSizeFramesDoNotNestAndTheirHullsCollide()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0);
        Entity ownerA = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0),
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(50)),
        });
        Entity ownerB = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(40),
            LinearVelocity = new Vector3(-10, 0, 0),
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(60)),
        });
        FrameHandle a = world.GetOwnedFrame(ownerA), b = world.GetOwnedFrame(ownerB);
        // The bubbles overlap from the start, so each hull has a ghost in the other frame.
        world.Tick();
        faults.Jolt.HasGhostIn(ownerA, b).Should().BeTrue();
        faults.Jolt.HasGhostIn(ownerB, a).Should().BeTrue();

        Tick(world, 240);

        HandOffsSent(faults).Should().BeEmpty();
        world.Frames.Parent(a).Should().Be(space);
        world.Frames.Parent(b).Should().Be(space);
        // B hit A: momentum is shared, so both now move in -X and the hulls do not overlap.
        world.Frames.LinearVelocity(a).X.Should().BeLessThan(-3);
        world.Frames.LinearVelocity(b).X.Should().BeGreaterThan(-7);
        double gap = world.Frames.LocalPose(b).Position.X - world.Frames.LocalPose(a).Position.X;
        gap.Should().BeGreaterThan(9.9, "the hull centers are that far apart when the hulls do not overlap");
        faults.Outputs.SelectMany(output => output.Contacts).Should().Contain(c => (c.A == ownerA && c.B == ownerB) || (c.A == ownerB && c.B == ownerA));
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AHullThatOverlapsTheBubbleOfAFrameInsideItsOwnFrameStaysWhereItIs()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world, 1e6f);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0),
            Body = BoxBody(2, mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        // A big hall inside the ship. The ship's hull would fit in the hall's bubble, and touches it.
        Bubble(world, ship, At(5), 30);

        Tick(world, 3);

        HandOffsSent(faults).Should().BeEmpty();
        faults.Jolt.TryGetBodyFrame(shipOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AHullNeverEntersAFrameThatTheTreeKeepsInsideItsOwnFrame()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = Space(world, 1e6f);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(0),
            Body = BoxBody(2, mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        // A big annex of the ship, whose hull is outside the ship's bubble, so every step hands the annex out into
        // space. The test turns each of those reports into a move of the annex into itself, as a bug would, so the
        // sync point refuses it and the tree keeps the annex inside the ship. Beside the ship, the annex's bubble would
        // touch the ship's hull, which would fit in it.
        Entity annexOwner = world.Spawn(new SpawnDesc
        {
            Frame = ship,
            Pose = At(33),
            Body = BoxBody(1, mass: 10),
            AsFrame = new FrameSpec(FrameBounds.Sphere(30)),
        });
        FrameHandle annex = world.GetOwnedFrame(annexOwner);
        faults.EditOutput = output =>
        {
            for (int i = 0; i < output.HandOffs.Count; i++)
                if (output.HandOffs[i].Entity == annexOwner)
                    output.HandOffs[i] = output.HandOffs[i] with { To = annex };
        };

#if TERTIUS_CONTRACTS
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{annexOwner}*");
#else
        Tick(world, 3);

        world.Frames.Parent(annex).Should().Be(ship);
        List<HandOff> sent = HandOffsSent(faults);
        sent.Should().NotContain(handOff => handOff.Entity == shipOwner, "the tree keeps the annex inside the ship");
        // Physics follows the tree, so each step starts with the annex's hull back in the ship's system, and each step
        // sends the same wrong hand-off again. The sync point refuses each one and records it once.
        sent.Should().HaveCount(3).And.OnlyContain(handOff => handOff.Entity == annexOwner && handOff.From == ship && handOff.To == annex);
        world.Errors.Should().HaveCount(3).And.OnlyContain(error => error.Contains(annexOwner.ToString()) && error.Contains("refused the hand-off"));
#endif
    }

    /// <summary>
    /// A carrier bubble of radius 200 m, 1 km along +X from the origin of space, and a fighter 1,260 m along +X that
    /// flies at 60 m/s toward the carrier, whose bubble it fits in.
    /// </summary>
    private static (FrameHandle Space, FrameHandle Carrier, Entity FighterOwner, FrameHandle Fighter) FighterApproachingCarrier(World world, Pilot? pilot = null)
    {
        FrameHandle space = Space(world);
        FrameHandle carrier = Bubble(world, space, At(1000), 200);
        Entity fighterOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1260),
            LinearVelocity = new Vector3(-60, 0, 0),
            Body = BoxBody(2, mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = pilot is null ? [] : [pilot],
        });
        return (space, carrier, fighterOwner, world.GetOwnedFrame(fighterOwner));
    }

    [Fact]
    public void AHullWhoseHandOffTheSimulationRefusedIsBackInTheTreesParentOnTheNextStep()
    {
        // Numeric frames off: the Release branch checks that the system of space holds the fighter's hull.
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        // The fighter stops once the next step would take its hull to the carrier's bubble. The stop is an intent, which
        // acts in the step of the tick after the one that writes it. So the tick that sees the hull one step short of
        // the bubble writes the stop, and the step of the next tick starts from the tree's pose with no speed.
        var pilot = new Pilot();
        var (space, carrier, fighterOwner, fighter) = FighterApproachingCarrier(world, pilot);
        double touchingX = 1000 + 200 + BoxBody(2).Shape.BoundingRadius();
        pilot.EveryTick = body =>
        {
            if (world.PoseIn(fighterOwner, space).Position.X <= touchingX + 60 * Dt)
                body.SetVelocity(Double3.Zero);
        };
        // The faulty hand-off is the report of the fighter's hand-off with a wrong origin, as a bug would give.
        faults.EditOutput = output =>
        {
            for (int i = 0; i < output.HandOffs.Count; i++)
                if (output.HandOffs[i].Entity == fighterOwner)
                    output.HandOffs[i] = output.HandOffs[i] with { From = carrier };
        };

#if TERTIUS_CONTRACTS
        Action ticks = () => TickUntil(world, () => false, 120);

        ticks.Should().Throw<ContractException>().WithMessage($"*{fighterOwner}*");
#else
        TickUntil(world, () => world.Errors.Count > 0, 120);
        world.Errors.Should().ContainSingle().Which.Should().Contain(fighterOwner.ToString()).And.Contain("ignored the hand-off");
        faults.Outputs[^1].HandOffs.Should().ContainSingle().Which.To.Should().Be(carrier);

        // The tree still has the fighter in space, where it was before the refused step. It stopped there, so the next
        // step does not take it into the carrier's bubble again.
        DPose inSpace = world.Frames.LocalPose(fighter);
        world.GetFrame(fighterOwner).Should().Be(space);
        world.Tick();

        faults.Jolt.TryGetBodyFrame(fighterOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        world.GetFrame(fighterOwner).Should().Be(space);
        (world.PoseInOwnFrame(fighterOwner).Position - inSpace.Position).Length.Should().BeLessThan(1e-6);
        // Relative to the carrier, the fighter would be about 1 km closer to the origin.
        FrameStepRecord reported = faults.Outputs[^1].Frames.Single(state => state.Frame == fighter);
        (reported.Motion.Pose.Position - inSpace.Position).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().ContainSingle("the second step sent no hand-off");
#endif
    }

    [Fact]
    public void PhysicsFollowsAFrameThatTheGameReparentsRightAfterItsHandOff()
    {
        // Numeric frames off: the test checks that the fighter's hull is in the system of space after the reparent.
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        var (space, carrier, fighterOwner, fighter) = FighterApproachingCarrier(world);
        TickUntil(world, () => HandOffsSent(faults).Count > 0, 120);
        world.Frames.Parent(fighter).Should().Be(carrier);

        // Between ticks, with no change batch, the game moves the fighter back into space and turns it around at
        // 120 m/s, fast enough that it leaves the carrier's bubble in one step.
        world.Frames.Reparent(fighter, space);
        world.Frames.SetTreeLinearVelocity(fighter, new Double3(120, 0, 0));
        world.Frames.Update();
        Double3 expected = world.Frames.LocalPose(fighter).Position + new Double3(120 * (double)Dt, 0, 0);
        world.Tick();

        faults.Jolt.TryGetBodyFrame(fighterOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        world.GetFrame(fighterOwner).Should().Be(space);
        (world.PoseInOwnFrame(fighterOwner).Position - expected).Length.Should().BeLessThan(1e-5);
        FrameStepRecord reported = faults.Outputs[^1].Frames.Single(state => state.Frame == fighter);
        (reported.Motion.Pose.Position - expected).Length.Should().BeLessThan(1e-5);
        reported.Motion.Velocity.X.Should().BeApproximately(120, 1e-3);
        HandOffsSent(faults).Should().ContainSingle("the fighter is out of the carrier's bubble");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AHullThatTheGameMovesOutOfTheFrameItEnteredNeverLeavesThatFrameForItsOwnFrame()
    {
        // Numeric frames off: the test checks that the ship's hull stays in the system of space.
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        FrameHandle space = Space(world);
        FrameHandle hangar = Bubble(world, space, At(1000), 30);
        Entity shipOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(1040),
            LinearVelocity = new Vector3(-60, 0, 0),
            Body = BoxBody(2, mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle ship = world.GetOwnedFrame(shipOwner);
        TickUntil(world, () => HandOffsSent(faults).Count > 0, 60);
        HandOffsSent(faults).Should().ContainSingle().Which.To.Should().Be(hangar);

        // Between ticks, with no change batch, the game moves the ship back into space and the hangar under the ship.
        // Both moves are legal. The hangar's parent is now the ship.
        world.Frames.Reparent(ship, space);
        world.Frames.Reparent(hangar, ship);
        world.Frames.Update();
        // At 60 m/s the hull needs about 70 steps to cross the hangar's bubble and leave it on the far side. A hull that
        // physics kept in the hangar's system would then leave it for the hangar's parent: the ship's own frame.
        Tick(world, 90);

        HandOffsSent(faults).Should().ContainSingle("the ship has no other frame to enter or leave");
        faults.Jolt.TryGetBodyFrame(shipOwner, out FrameHandle hullFrame).Should().BeTrue();
        hullFrame.Should().Be(space);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CharacterWalkingOutOfAFrameIsRecreatedInTheParent()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var gravity = GravityField.Uniform(new Vector3(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.5), Body = FloorBody(100) });
        // A frame for a building on the ground. The same gravity holds inside.
        FrameHandle building = Bubble(world, ground, At(0, 0, 0), 10, gravity);
        // The building's floor reaches past its bubble, so the walker has ground under it until it leaves the frame.
        world.Spawn(new SpawnDesc { Frame = building, Pose = At(0, -0.5), Body = FloorBody(15) });
        var body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);
        var pilot = new Pilot { EveryTick = handle => handle.MoveCharacter(new Vector3(4, 0, 0)) };
        Entity walker = world.Spawn(new SpawnDesc { Frame = building, Pose = At(0, 0.95), Body = body, Components = [pilot] });

        TickUntil(world, () => HandOffsSent(faults).Count > 0, 300);

        HandOff exit = HandOffsSent(faults).Should().ContainSingle().Which;
        exit.From.Should().Be(building);
        exit.To.Should().Be(ground);
        exit.LinearVelocity.X.Should().BeApproximately(4, 0.05f);
        // The World recreated the character in the parent, where Jolt sent it.
        world.GetFrame(walker).Should().Be(ground);
        world.PoseInOwnFrame(walker).Should().Be(exit.Pose);
        // It keeps walking with the same desired velocity in the parent.
        double x = world.PoseInOwnFrame(walker).Position.X;
        Tick(world, 30);
        world.GetFrame(walker).Should().Be(ground);
        (world.PoseInOwnFrame(walker).Position.X - x).Should().BeInRange(1.8, 2.2);
        world.PoseInOwnFrame(walker).Position.Y.Should().BeInRange(0.85, 1.0);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The scene of <see cref="CharacterWalkingOutOfAFrameIsRecreatedInTheParent"/> without the building's own floor. The
    /// ground's floor reaches the building's bubble, so it has a ghost there (ticket ter-xxmj), and the walker stands on that
    /// ghost from its spawn until it leaves the frame.
    /// </summary>
    [Fact]
    public void CharacterWalkingOutOfAFrameWithoutAFloorOfItsOwnStandsOnTheGhostOfTheParentsFloor()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var gravity = GravityField.Uniform(new Vector3(0, -9.81f, 0));
        FrameHandle ground = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), gravity));
        world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, -0.5), Body = FloorBody(100) });
        FrameHandle building = Bubble(world, ground, At(0, 0, 0), 10, gravity);
        var body = new BodyDesc(new CapsuleShapeDesc(0.6f, 0.3f), BodyMotion.Character, Mass: 70);
        var pilot = new Pilot { EveryTick = handle => handle.MoveCharacter(new Vector3(4, 0, 0)) };
        Entity walker = world.Spawn(new SpawnDesc { Frame = building, Pose = At(0, 0.95), Body = body, Components = [pilot] });

        TickUntil(world, () => HandOffsSent(faults).Count > 0, 300);

        HandOff exit = HandOffsSent(faults).Should().ContainSingle().Which;
        exit.From.Should().Be(building);
        exit.To.Should().Be(ground);
        exit.Pose.Position.Y.Should().BeInRange(0.85, 1.0, "the walker stood on the floor's ghost in the building");
        double x = world.PoseInOwnFrame(walker).Position.X;
        Tick(world, 30);
        world.GetFrame(walker).Should().Be(ground);
        (world.PoseInOwnFrame(walker).Position.X - x).Should().BeInRange(1.8, 2.2);
        world.PoseInOwnFrame(walker).Position.Y.Should().BeInRange(0.85, 1.0);
        world.Errors.Should().BeEmpty();
    }
}

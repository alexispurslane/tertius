using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A following frame without a plan holds still in its semantic frame (ticket ter-zsku, docs/plans/numeric-frames.md,
/// "Following frames"). In a step without a motion intent, the step plan of the frame's anchor is the record that physics
/// gives a kinematic body without a motion intent: it stays where the step starts, with no velocity. The frame lands on that
/// plan by the rule of every plan, so the anchor, a plain kinematic body or a kinematic hull, holds still whatever numeric
/// frame holds it, as a kinematic body without a motion intent holds still in its semantic frame's own system. Numeric
/// frames change nothing that gameplay sees. Before ter-zsku, such a frame fell by its semantic frame's field, and its
/// anchor fell with it. The world runs on the real backend.
/// </summary>
public class StillFollowingFrameTests
{
    /// <summary>The distance from the origin of its frame, in meters, past which a kinematic body anchors a following frame of its own: 2^23 m, about 8,389 km.</summary>
    private const double FollowDistance = 8_388_608;

    // ---- A Kepler hull whose component skips a tick ---------------------------------------------------------------

    private const double Mu = Scenes.AsteroidMu;

    /// <summary>The tick whose sample <see cref="SkippingKepler"/> does not write.</summary>
    private const long SkippedTick = 60;

    /// <summary>The tick from which <see cref="StoppingDrift"/> writes no motion intent in the scenes below.</summary>
    private const long StopTick = 20;

    /// <summary>
    /// A fast orbit of 5 km at 2 km/s round the point gravity of an <see cref="Scenes.AsteroidSpace"/>, whose pull there is
    /// 800 m/s²: a hull on it lives in a following frame of its own. The frame turns at 0.2 rad/s.
    /// </summary>
    private static readonly KeplerOrbit Fast = KeplerOrbit.Circular(5_000, phase: 0.3, spinRate: 0.2);

    // ---- A hand-off in the step in which the anchor stops --------------------------------------------------------

    /// <summary>The velocity of the hull's drift in space, in m/s: 2 km/s along +X.</summary>
    private static readonly Double3 Drift = new(2_000, 0, 0);

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a root frame with a uniform gravity of
    /// 9.81 m/s² along -Y and a bubble of 100,000 km, and in it a plain kinematic ball at rest <paramref name="distance"/> m
    /// from the origin along +X. No component moves the ball, so it has no motion intent in any step.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Ball) StillBall(double distance, bool numericFrames)
    {
        World world = NewWorld(backend: out _,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space =
            world.CreateRootFrame(new(FrameBounds.Sphere(1e8f), GravityField.Uniform(new(0, -9.81f, 0))));
        Entity ball = world.Spawn(new()
            { Frame = space, Pose = At(distance), Body = Ball() with { Motion = BodyMotion.Kinematic } });
        return (world, space, ball);
    }

    /// <summary>
    /// The ball holds still for ten seconds, near the origin, where it lives in its frame's own system, and 10,000 km out,
    /// past 2^23 m, where it anchors a following frame of its own. Before ter-zsku, the far ball fell with its frame, 490 m
    /// in ten seconds.
    /// </summary>
    [Theory]
    [InlineData(10.0)]
    [InlineData(1e7)]
    public void AStillPlainKinematicBodyInAGravityFieldHoldsStillAtAnyDistanceFromItsFramesOrigin(double distance)
    {
        (World world, FrameHandle space, Entity ball) = StillBall(distance, numericFrames: true);
        using (world)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(ball);
            world.Frames.Anchors(home, ball).Should().Be(distance >= FollowDistance,
                "only a far kinematic body anchors a following frame");

            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                world.PoseIn(ball, space).Should()
                    .Be(At(distance), "the ball holds still at tick {0}", world.TickNumber);
                world.VelocityIn(ball, space).Should()
                    .Be(Double3.Zero, "the ball has no velocity at tick {0}", world.TickNumber);
            }

            world.PhysicsHomeOf(ball).Should().Be(home, "the far ball keeps its following frame by its distance");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Writes in every tick the sample of <see cref="Orbit"/> for the step that applies the tick's intents, as
    /// <see cref="KeplerMotion"/> does, but with no teleport, and writes nothing in the tick <see cref="SkippedTick"/>.
    /// </summary>
    private sealed class SkippingKepler(KeplerOrbit orbit) : IComponent
    {
        public KeplerOrbit Orbit => orbit;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick != SkippedTick)
                self.MoveOnRails(orbit.Sample(Mu, self.TimeAt(self.Tick + 2), self.DeltaTime));
        }
    }

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, an <see cref="Scenes.AsteroidSpace"/>, and in it
    /// the owner of a frame with a bubble of 1 cm, whose kinematic hull, a ball of 0.5 m, <see cref="SkippingKepler"/> moves
    /// on <see cref="Fast"/>. The spawn takes the orbit's state at the world's time and the sample of its first step as its
    /// first motion intent, so the hull stands on its orbit from its first sync point.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner) SkippingHull(bool numericFrames) =>
        SkippingHull(numericFrames, out _);

    /// <summary>The scene of <see cref="SkippingHull(bool)"/>, whose backend is <paramref name="backend"/>.</summary>
    private static (World World, FrameHandle Space, Entity Owner) SkippingHull(bool numericFrames,
        out JoltPhysicsBackend backend)
    {
        World world = NewWorld(backend: out backend,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Scenes.AsteroidSpace(world);
        double time = world.Time;
        (Double3 position, Double3 velocity) = Fast.StateAt(time, Mu);
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Pose = new(position, Fast.RotationAt(time, Mu)),
            LinearVelocity = velocity,
            AngularVelocity = Fast.AngularVelocityAt(time, Mu).ToVector3(),
            FirstMove = new(Fast.Sample(Mu, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            Body = Ball() with { Motion = BodyMotion.Kinematic },
            AsFrame = new(FrameBounds.Sphere(0.01f)),
            Components = [new SkippingKepler(Fast)]
        });
        return (world, space, owner);
    }

    /// <summary>The pose of <see cref="Fast"/> at <paramref name="time"/> in its parent.</summary>
    private static DPose RailsPose(double time) => new(Fast.StateAt(time, Mu).Position, Fast.RotationAt(time, Mu));

    /// <summary>
    /// The hull stands on its orbit at every sync point, to the last bit, except the one after the step without a sample:
    /// in that step the hull holds still where the step started, with no velocity and no turn, and the sample of the next
    /// step puts it back on its orbit, as it does without numeric frames. Before ter-zsku, the hull fell on in its following
    /// frame for that step, about along its orbit.
    /// </summary>
    [Fact]
    public void
        AKinematicHullOnAKeplerOrbitWhoseComponentSkipsATickHoldsStillForThatStepAndIsBackOnItsOrbitAfterItsNextSample()
    {
        (World world, FrameHandle space, Entity owner) = SkippingHull(numericFrames: true);
        using (world)
        {
            world.Tick();
            world.Frames.Anchors(world.PhysicsHomeOf(owner), owner).Should()
                .BeTrue("the hull moves at 2 km/s, so it anchors a following frame");
            // The tick SkippedTick writes no sample, so the step that ends at the time of tick SkippedTick + 2 applies none.
            double heldStepEnd = world.TimeAt(SkippedTick + 2);
            int held = 0;
            for (int tick = 0; tick < 120; tick++)
            {
                world.Tick();
                DPose pose = world.PoseIn(owner, space);
                if (world.Time == heldStepEnd)
                {
                    held++;
                    DPose start = RailsPose(world.TimeAt(SkippedTick + 1));
                    pose.Position.Should().Be(start.Position, "the hull holds still where the step started");
                    // The still hull is slow and near the origin, so the numeric update lets it go into the system of space, and
                    // its frame's rotation passes through the following frame's, which rounds its last bit.
                    DQuaternion turn = pose.Rotation.Conjugate() * start.Rotation;
                    new Double3(turn.X, turn.Y, turn.Z).Length.Should().BeLessThan(1e-15, "the hull does not turn");
                    world.VelocityIn(owner, space).Should().Be(Double3.Zero,
                        "the hull has no velocity in the step without a sample");
                    world.AngularVelocityIn(owner, space).Should().Be(Vector3.Zero,
                        "the hull does not turn in the step without a sample");
                    continue;
                }

                pose.Should().Be(RailsPose(world.Time), "the hull stands on its orbit at sync point {0}",
                    world.TickNumber);
            }

            held.Should().Be(1);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Drives its kinematic body along a straight line at <see cref="Drift"/> with end poses, as <see cref="KinematicDrift"/>
    /// does, and writes no motion intent from the tick <see cref="StopTick"/> on, so the body holds still from the step that
    /// applies that tick's intents.
    /// </summary>
    private sealed class StoppingDrift(long stopTick) : IComponent
    {
        public long StopTick => stopTick;

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (self.Tick >= stopTick)
                return;
            DPose start = self.PoseAtNextStepStartIn(self.Frame);
            self.MoveKinematic(new(start.Position + Drift * self.DeltaTime, start.Rotation));
        }
    }

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a space without gravity, and in it the owner of a
    /// frame with a bubble of 16 m, whose kinematic hull, a box of 4 m, <see cref="StoppingDrift"/> drives at
    /// <see cref="Drift"/> until <paramref name="stopTick"/>. A crate floats in the frame 15 m along +Y from the hull and
    /// drifts on along +Y at 2 m/s, so it leaves the bubble into the frame's tree parent within a second: the hull's following
    /// frame with numeric frames, and space without them. With <paramref name="ship"/> the crate is the dynamic hull of a
    /// ship, a frame with a bubble of 10 cm.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner, Entity Crate) LeavingCrate(bool numericFrames,
        long stopTick, bool ship = false)
    {
        World world = NewWorld(backend: out _,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Ground(world, 0);
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Kinematic, Mass: 1e5f),
            AsFrame = new(FrameBounds.Sphere(16)),
            LinearVelocity = Drift,
            FirstMove = new(new EndPose(new(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new StoppingDrift(stopTick)]
        });
        Entity crate = world.Spawn(new()
        {
            Frame = world.GetOwnedFrame(owner), Pose = At(0, 15), LinearVelocity = new(0, 2, 0),
            Body = new(Shape: new BoxShapeDesc(new(0.25f)), Mass: 2, Restitution: 0, LinearDamping: 0,
                AngularDamping: 0),
            AsFrame = ship ? new FrameSpec(FrameBounds.Sphere(0.1f)) : null
        });
        return (world, space, owner, crate);
    }

    /// <summary>
    /// The hull stops in the step in which the crate leaves its bubble, or in the step before or after. The crate then moves
    /// on in space at the drift's velocity plus its own, as it does without numeric frames, whichever step the hull stopped in.
    /// A ship that leaves in the step in which the hull stops hands off into the following frame while the frame's instant
    /// velocity in Jolt's stepping runs 1 km/s behind the plan's, so its record after the hand-off takes Jolt's chord there
    /// back to the velocity that the hand-off carried (see JoltPhysicsBackend.TryHullRecord).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACrateThatLeavesTheBubbleOfAHullThatStopsMovesOnInSpaceAsWithoutNumericFrames(bool ship)
    {
        long exitTick;
        (World probe, FrameHandle probeSpace, Entity _, Entity probeCrate) =
            LeavingCrate(numericFrames: true, stopTick: long.MaxValue, ship);
        using (probe)
        {
            TickUntil(probe, () => probe.GetFrame(probeCrate) == probeSpace, 120);
            probe.GetFrame(probeCrate).Should().Be(probeSpace, "the crate leaves the bubble within two seconds");
            exitTick = probe.TickNumber;
        }

        for (long stopTick = exitTick - 3; stopTick <= exitTick; stopTick++)
        {
            (World world, FrameHandle space, Entity owner, Entity crate) =
                LeavingCrate(numericFrames: true, stopTick, ship);
            (World plain, FrameHandle plainSpace, Entity plainOwner, Entity plainCrate) =
                LeavingCrate(numericFrames: false, stopTick, ship);
            using (world)
            using (plain)
            {
                for (long tick = 0; tick < exitTick + 30; tick++)
                {
                    world.Tick();
                    plain.Tick();
                    (world.PoseIn(crate, space).Position - plain.PoseIn(plainCrate, plainSpace).Position).Length
                        .Should().BeLessThan(1e-3,
                            "the crate is where it is without numeric frames, stop tick {0}, sync point {1}", stopTick,
                            world.TickNumber);
                    (world.VelocityIn(crate, space) - plain.VelocityIn(plainCrate, plainSpace)).Length
                        .Should().BeLessThan(1e-3,
                            "the crate moves as it does without numeric frames, stop tick {0}, sync point {1}",
                            stopTick, world.TickNumber);
                }

                (world.PoseIn(owner, space).Position - plain.PoseIn(plainOwner, plainSpace).Position).Length
                    .Should().BeLessThan(1e-6, "the hull stopped where it does without numeric frames, stop tick {0}",
                        stopTick);
                world.Errors.Should().BeEmpty();
            }
        }
    }

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a space without gravity, the hull of
    /// <see cref="LeavingCrate"/>, which <see cref="StoppingDrift"/> drives at <see cref="Drift"/> until
    /// <see cref="StopTick"/>,
    /// and a body in space 24 m behind the hull along -X and 5 m beside it along +Y, outside its bubble of 16 m, with the
    /// hull's velocity and 10 m/s more toward the hull: a crate, a box of 0.5 m and 2 kg, or a character. With numeric frames
    /// the merge test links the body to the hull, so it moves in the hull's following frame. It is 20.6 m behind the hull
    /// when the hull stops, and in that step it moves on by 33.5 m relative to the hull, and ends the step 13.9 m from it,
    /// inside the bubble.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner, Entity Crate) EnteringCrate(bool numericFrames,
        bool character,
        out JoltPhysicsBackend backend)
    {
        World world = NewWorld(backend: out backend,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Ground(world, 0);
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Kinematic, Mass: 1e5f),
            AsFrame = new(FrameBounds.Sphere(16)),
            LinearVelocity = Drift,
            FirstMove = new(new EndPose(new(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new StoppingDrift(StopTick)]
        });
        Entity crate = world.Spawn(new()
        {
            Frame = space, Pose = At(-24, 5), LinearVelocity = Drift + new Double3(10, 0, 0),
            Body = character
                ? new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70)
                : new BodyDesc(Shape: new BoxShapeDesc(new(0.25f)), Mass: 2, Restitution: 0, LinearDamping: 0,
                    AngularDamping: 0)
        });
        return (world, space, owner, crate);
    }

    /// <summary>
    /// The body enters the hull's bubble in the step in which the hull stops, and keeps its velocity in space: it enters the
    /// hull's frame at 2 km/s, leaves it again in the next step, and is where it is without numeric frames at every sync
    /// point. Jolt's chord of the body in the following frame, plus half a step of the gravity that physics applied to it, is
    /// relative to the frame's instant velocity in Jolt's stepping, which is half the frame's change of velocity, 1 km/s, past
    /// the instant velocity of the frame's still plan. Before the fix, the hand-off took the body's velocity as relative to
    /// the plan's, and the body entered the hull at 3 km/s. In that step the frame's system shows 120,000 m/s², so the
    /// character has the walking form there, and the rigid form in the gravity-free space before and after.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABodyThatEntersTheBubbleOfAHullInTheStepInWhichTheHullStopsMovesOnInSpaceAsWithoutNumericFrames(
        bool character)
    {
        (World world, FrameHandle space, Entity owner, Entity crate) =
            EnteringCrate(numericFrames: true, character, out JoltPhysicsBackend backend);
        (World plain, FrameHandle plainSpace, Entity plainOwner, Entity plainCrate) =
            EnteringCrate(numericFrames: false, character, out _);
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world.PhysicsHomeOf(crate).Should()
                .Be(world.PhysicsHomeOf(owner), "the crate rests in the hull's following frame");
            int inside = 0;
            for (int tick = 0; tick < 60; tick++)
            {
                world.Tick();
                plain.Tick();
                bool entered = world.GetFrame(crate) == world.GetOwnedFrame(owner);
                entered.Should().Be(plain.GetFrame(plainCrate) == plain.GetOwnedFrame(plainOwner),
                    "the body is in the hull's frame at the same sync points");
                if (entered)
                    inside++;
                if (character)
                    backend.HasRigidForm(crate).Should().Be(!entered,
                        "the character walks only in the step in which the hull stops, at sync point {0}",
                        world.TickNumber);
                (world.PoseIn(crate, space).Position - plain.PoseIn(plainCrate, plainSpace).Position).Length
                    .Should().BeLessThan(1e-3, "the crate is where it is without numeric frames at sync point {0}",
                        world.TickNumber);
                (world.VelocityIn(crate, space) - plain.VelocityIn(plainCrate, plainSpace)).Length
                    .Should().BeLessThan(1e-3, "the crate moves as it does without numeric frames at sync point {0}",
                        world.TickNumber);
            }

            inside.Should().BeGreaterThan(0, "the crate enters the hull's bubble");
            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a space without gravity, the hull of
    /// <see cref="LeavingCrate"/>, which <see cref="StoppingDrift"/> drives at <see cref="Drift"/> until
    /// <see cref="StopTick"/>,
    /// and the owner of a ship, a frame with a bubble of 3 m whose dynamic hull is a box of 2 m and 1 t, in space 40 m from
    /// the hull along +Y, outside its bubble, with the hull's velocity. With numeric frames the ship's hull lives in the
    /// hull's following frame, and its frame hangs there.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner, Entity Ship) ShipBesideAStoppingHull(
        bool numericFrames)
    {
        World world = NewWorld(backend: out _,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Ground(world, 0);
        Entity owner = world.Spawn(new()
        {
            Frame = space,
            Body = new(Shape: new BoxShapeDesc(new(2)), Motion: BodyMotion.Kinematic, Mass: 1e5f),
            AsFrame = new(FrameBounds.Sphere(16)),
            LinearVelocity = Drift,
            FirstMove = new(new EndPose(new(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new StoppingDrift(StopTick)]
        });
        Entity ship = world.Spawn(new()
        {
            Frame = space, Pose = At(-10, 22), LinearVelocity = Drift + new Double3(10, 0, 0),
            AsFrame = new(FrameBounds.Sphere(3)),
            Body = new(Shape: new BoxShapeDesc(new(1)), Mass: 1000, Restitution: 0, LinearDamping: 0, AngularDamping: 0)
        });
        return (world, space, owner, ship);
    }

    /// <summary>
    /// The ship keeps the drift's velocity in space when the hull beside it stops, and its frame reads that velocity in
    /// space at every sync point, the one after the stop step included, as without numeric frames. The record that physics
    /// releases for the ship's frame is relative to the instant velocity of the following frame's plan. Before the fix, it
    /// was Jolt's chord plus half a step of the ship's acceleration, which is relative to the following frame's instant
    /// velocity in Jolt's stepping, and the ship read 3 km/s for one tick.
    /// </summary>
    [Fact]
    public void ADynamicShipBesideAHullThatStopsReadsItsVelocityInSpaceAsWithoutNumericFrames()
    {
        (World world, FrameHandle space, Entity owner, Entity ship) = ShipBesideAStoppingHull(numericFrames: true);
        (World plain, FrameHandle plainSpace, Entity _, Entity plainShip) =
            ShipBesideAStoppingHull(numericFrames: false);
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world.PhysicsHomeOf(ship).Should().Be(world.PhysicsHomeOf(owner),
                "the ship's hull lives in the hull's following frame");
            for (int tick = 0; tick < 60; tick++)
            {
                world.Tick();
                plain.Tick();
                (world.PoseIn(ship, space).Position - plain.PoseIn(plainShip, plainSpace).Position).Length
                    .Should().BeLessThan(1e-3, "the ship is where it is without numeric frames at sync point {0}",
                        world.TickNumber);
                (world.VelocityIn(ship, space) - plain.VelocityIn(plainShip, plainSpace)).Length
                    .Should().BeLessThan(1e-3, "the ship moves as it does without numeric frames at sync point {0}",
                        world.TickNumber);
            }

            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
        }
    }

    // ---- Numeric frames on and off ------------------------------------------------------------------------------

    /// <summary>
    /// The scenes above run beside the same scenes without numeric frames, and their bodies have the same places and
    /// velocities in their semantic frames at every sync point for two seconds: the still ball 10,000 km out, which anchors
    /// a following frame with numeric frames and lives in its frame's own system without them, and the Kepler hull whose
    /// component skips a tick.
    /// </summary>
    [Theory]
    [InlineData("still ball")]
    [InlineData("skipping hull")]
    public void AnAnchorWithoutAMotionIntentMovesAsItDoesWithoutNumericFrames(string scene)
    {
        (World world, FrameHandle space, Entity body) = scene == "still ball"
            ? StillBall(1e7, numericFrames: true)
            : SkippingHull(numericFrames: true);
        (World plain, FrameHandle plainSpace, Entity plainBody) = scene == "still ball"
            ? StillBall(1e7, numericFrames: false)
            : SkippingHull(numericFrames: false);
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world.Frames.Anchors(world.PhysicsHomeOf(body), body).Should()
                .BeTrue("the body anchors a following frame with numeric frames");
            plain.PhysicsHomeOf(plainBody).Should().Be(plainSpace,
                "without numeric frames the body lives in its frame's own system");
            for (int tick = 0; tick < 120; tick++)
            {
                world.Tick();
                plain.Tick();
                (world.PoseIn(body, space).Position - plain.PoseIn(plainBody, plainSpace).Position).Length
                    .Should().BeLessThan(1e-6, "the body is where it is without numeric frames at sync point {0}",
                        world.TickNumber);
                // Without numeric frames the row keeps the hull's 2 km/s as a float, to 6.1e-5 m/s.
                (world.VelocityIn(body, space) - plain.VelocityIn(plainBody, plainSpace)).Length
                    .Should().BeLessThan(1e-4, "the body moves as it does without numeric frames at sync point {0}",
                        world.TickNumber);
            }

            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The scene of <see cref="SkippingHull(bool)"/>, and in space beside it a character at rest relative to the hull, 3.5 m from it
    /// along -Y, with the orbit's velocity, which lives in the hull's following frame with numeric frames.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Person) PersonBesideASkippingHull(bool numericFrames,
        out JoltPhysicsBackend backend)
    {
        (World world, FrameHandle space, Entity owner) = SkippingHull(numericFrames, out backend);
        (Double3 position, Double3 velocity) = Fast.StateAt(world.Time, Mu);
        Entity person = world.Spawn(new()
        {
            Frame = space, Pose = new(position + new Double3(0, -3.5, 0), DQuaternion.Identity),
            LinearVelocity = velocity,
            Body = new(Shape: new CapsuleShapeDesc(0.6f, 0.3f), Motion: BodyMotion.Character, Mass: 70)
        });
        world.Tick();
        if (numericFrames)
            world.PhysicsHomeOf(person).Should().Be(world.PhysicsHomeOf(owner),
                "the character lives in the hull's following frame");
        return (world, space, person);
    }

    /// <summary>
    /// The character falls freely beside the Kepler hull whose component skips a tick, and has the place and velocity in
    /// space that it has without numeric frames at every sync point, the one after the step in which the hull's following
    /// frame stops included. In that step the frame's system shows the orbit's speed lost in one step, about 120,000 m/s²,
    /// so the character has the walking form, and Jolt moves it at 2 km/s in the frame. Without numeric frames the
    /// character's velocity of 2 km/s is a float in the asteroid's own system, whose step is 2.4e-4 m/s, and each step's pull
    /// of 13 m/s rounds to it, so the two worlds differ by a few of those steps, and the places by a fraction of a millimeter.
    /// </summary>
    [Fact]
    public void ACharacterBesideAKeplerHullWhoseComponentSkipsATickMovesAsItDoesWithoutNumericFrames()
    {
        (World world, FrameHandle space, Entity person) =
            PersonBesideASkippingHull(numericFrames: true, out JoltPhysicsBackend backend);
        (World plain, FrameHandle plainSpace, Entity plainPerson) =
            PersonBesideASkippingHull(numericFrames: false, out _);
        using (world)
        using (plain)
        {
            double heldStepEnd = world.TimeAt(SkippedTick + 2);
            double worstPlace = 0, worstVelocity = 0;
            for (int tick = 0; tick < 120; tick++)
            {
                world.Tick();
                plain.Tick();
                if (world.Time == heldStepEnd)
                    backend.HasRigidForm(person).Should()
                        .BeFalse("the frame's system shows the orbit's speed lost in one step");
                worstPlace = Math.Max(worstPlace,
                    (world.PoseIn(person, space).Position - plain.PoseIn(plainPerson, plainSpace).Position).Length);
                worstVelocity = Math.Max(worstVelocity,
                    (world.VelocityIn(person, space) - plain.VelocityIn(plainPerson, plainSpace)).Length);
            }

            worstPlace.Should().BeLessThan(1e-3, "the character is where it is without numeric frames");
            worstVelocity.Should().BeLessThan(2e-3, "the character moves as it does without numeric frames");
            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
        }
    }
}

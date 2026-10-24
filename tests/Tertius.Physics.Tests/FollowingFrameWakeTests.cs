using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The wake rule of a following frame (ticket ter-zg77, docs/plans/numeric-frames.md, "Following frames"). A body that sleeps
/// has no local velocity, so it moves with its frame. The members of a frame feel the frame's push, reversed: what an
/// accelerometer at its origin reads. Physics wakes the members of a frame when its push moves more than
/// <see cref="JoltBackendOptions.WakeAccelerationThreshold"/> from its value at their last wake. The push of a following frame
/// is its acceleration in its semantic frame less the field there, so the members wake when the anchor speeds up, and an
/// awake member keeps its motion in the semantic frame. The same rule gives a resting frame the push of its semantic frame,
/// whose proper acceleration the field takes in, so its members wake when that proper acceleration moves, as they do in the
/// semantic frame's own system. The world runs on the real backend, in a frame without gravity, or round the point gravity
/// of earth.
/// </summary>
public class FollowingFrameWakeTests
{
    /// <summary>The velocity of the anchor's drift in space, in m/s: 2 km/s along +X, above the 1,000 m/s from which a free body gets a numeric frame.</summary>
    private static readonly Double3 Drift = new(2_000, 0, 0);

    /// <summary>The acceleration of the anchor's speed-up, in m/s², along the drift: far above the 0.02 m/s² that wakes a frame's members.</summary>
    private static readonly Double3 SpeedUp = new(2, 0, 0);

    /// <summary>A crate, a box of 50 cm and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Crate = new(Shape: new BoxShapeDesc(new(0.25f)), Mass: 2, Restitution: 0,
        LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// The hull of the hull scene: a core, a box of 4 m at the frame's origin, and a plate of 16 m by 0.5 m by 16 m outside
    /// the bubble of 16 m, across the +Y axis, 20 m from the core. Its bounding sphere reaches past the bubble, so a crate
    /// outside the bubble lies within the merge test's reach of the hull.
    /// </summary>
    private static readonly BodyDesc Hull = new(Shape: new CompoundShapeDesc([
        new(new BoxShapeDesc(new(2)), new(Vector3.Zero, Quaternion.Identity)),
        new(new BoxShapeDesc(new(8, 0.25f, 8)), new(new(0, 20, 0), Quaternion.Identity))
    ]), Motion: BodyMotion.Kinematic, Mass: 1e5f, Restitution: 0);

    /// <summary>
    /// Drives its kinematic body along a straight line with end poses, as <see cref="KinematicDrift"/> does, at a velocity
    /// that gains <see cref="Acceleration"/> times the step in every tick. The test sets the acceleration between two ticks,
    /// and can stop the drive.
    /// </summary>
    private sealed class SpeedingDrift(Double3 velocity) : IComponent
    {
        private Double3 velocity = velocity;

        /// <summary>How fast the drift speeds up, in m/s², in the axes of the body's frame. Zero keeps the velocity steady.</summary>
        public Double3 Acceleration { get; set; }

        /// <summary>True when the drive writes no motion intent, so its body holds still in the steps that follow.</summary>
        public bool Stopped { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            if (Stopped)
                return;
            velocity += Acceleration * self.DeltaTime;
            DPose start = self.PoseAtNextStepStartIn(self.Frame);
            self.MoveKinematic(new(start.Position + velocity * self.DeltaTime, start.Rotation));
        }
    }

    /// <summary>
    /// A world on the real backend, a frame without gravity, and in it an anchor that <see cref="SpeedingDrift"/> drives at
    /// <see cref="Drift"/> from its first step on: a plain kinematic ball of radius 0.5 m, or the kinematic <see cref="Hull"/>
    /// of a frame with a bubble of 16 m. Beside it along +Z, 3 m from the ball or 30 m from the hull, outside its bubble, a
    /// crate moves at the drift's velocity, so the merge test links it to the anchor and it joins the anchor's following
    /// frame at rest there. The scene ticks until the crate sleeps.
    /// </summary>
    private static (World World, JoltPhysicsBackend Backend, FrameHandle Space, Entity Anchor, Entity Crate,
        SpeedingDrift Drive) CrateAsleepBeside(string anchorKind)
    {
        (World world, JoltPhysicsBackend backend, FrameHandle space, Entity anchor, Entity crate, SpeedingDrift drive) =
            CrateBeside(anchorKind, numericFrames: true);
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(crate);
        world.Frames.Numeric(home).Should().Be(NumericMode.Following, "the crate joins the anchor's following frame");
        world.Frames.Anchors(home, anchor).Should().BeTrue();
        TickUntil(world, () => !IsAwake(backend, home, crate), 600);
        IsAwake(backend, home, crate).Should().BeFalse("the crate rests in the following frame, so it falls asleep");
        return (world, backend, space, anchor, crate, drive);
    }

    /// <summary>
    /// The scene of <see cref="CrateAsleepBeside"/> as it is spawned, before any tick, with the default numeric frames or
    /// none.
    /// </summary>
    private static (World World, JoltPhysicsBackend Backend, FrameHandle Space, Entity Anchor, Entity Crate,
        SpeedingDrift Drive) CrateBeside(string anchorKind,
            bool numericFrames)
    {
        World world = NewWorld(backend: out JoltPhysicsBackend backend,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Ground(world, 0);
        var drive = new SpeedingDrift(Drift);
        var firstMove = new MotionIntent(new EndPose(new(Drift * world.DeltaTime, DQuaternion.Identity)));
        Entity anchor = anchorKind == "hull"
            ? world.Spawn(new()
            {
                Frame = space,
                Body = Hull,
                AsFrame = new(FrameBounds.Sphere(16)),
                LinearVelocity = Drift,
                FirstMove = firstMove,
                Components = [drive]
            })
            : world.Spawn(new()
            {
                Frame = space, Body = Ball() with { Motion = BodyMotion.Kinematic }, FirstMove = firstMove,
                Components = [drive]
            });
        Entity crate = world.Spawn(new()
            { Frame = space, Pose = At(0, 0, anchorKind == "hull" ? 30 : 3), LinearVelocity = Drift, Body = Crate });
        return (world, backend, space, anchor, crate, drive);
    }

    /// <summary>
    /// The anchor speeds up at 2 m/s² for one second. The crate wakes in the first step of the speed-up and keeps its motion
    /// in space: it goes on along its line at the drift's velocity, and falls 1 m behind the anchor. Without the wake it
    /// would move with the frame and keep up with the anchor.
    /// </summary>
    [Theory]
    [InlineData("plain body")]
    [InlineData("hull")]
    public void ACrateAsleepInTheFollowingFrameOfAnAnchorThatSpeedsUpWakesAndKeepsItsMotionInSpace(string anchorKind)
    {
        (World world, JoltPhysicsBackend backend, FrameHandle space, Entity anchor, Entity crate, SpeedingDrift drive) =
            CrateAsleepBeside(anchorKind);
        using (world)
        {
            FrameHandle home = world.PhysicsHomeOf(crate);
            DPose start = world.PoseIn(crate, space);
            double startTime = world.Time;
            drive.Acceleration = SpeedUp;
            // The tick writes the first end pose of the speed-up, and the step of the next tick applies it.
            world.Tick();
            world.Tick();
            IsAwake(backend, home, crate).Should().BeTrue("the frame's push changed by 2 m/s², so its members wake");

            Tick(world, 58);
            world.PhysicsHomeOf(crate).Should().Be(home, "the crate stays in the anchor's following frame");
            (world.PoseIn(crate, space).Position - (start.Position + Drift * (world.Time - startTime))).Length
                .Should().BeLessThan(1e-3, "the crate goes on along its line at the drift's velocity");
            (world.VelocityIn(crate, space) - Drift).Length.Should()
                .BeLessThan(1e-3, "the crate keeps the drift's velocity in space");
            (world.VelocityIn(anchor, space) - Drift).Length.Should().BeGreaterThan(1.9, "the anchor sped up");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The anchor stops: its drive writes no more motion intents, so in each step that follows the anchor's plan holds it
    /// still, and its following frame holds still in space (ticket ter-zsku). The frame's push changes by the 2 km/s that the
    /// frame loses in one step, so the crate wakes in that step and keeps its motion in space: it goes on along its line at
    /// the drift's velocity, to the places of the same crate in the same scene without numeric frames, and the anchor holds
    /// still as it does there. Before ter-zsku, the frame fell on at the drift's velocity with its anchor in it, and the crate
    /// slept on.
    /// </summary>
    [Theory]
    [InlineData("plain body")]
    [InlineData("hull")]
    public void ACrateAsleepInTheFollowingFrameOfAnAnchorThatStopsWakesAndMovesOnInSpaceAsWithoutNumericFrames(
        string anchorKind)
    {
        (World world, JoltPhysicsBackend backend, FrameHandle space, Entity anchor, Entity crate, SpeedingDrift drive) =
            CrateAsleepBeside(anchorKind);
        (World plain, JoltPhysicsBackend _, FrameHandle plainSpace, Entity plainAnchor, Entity plainCrate,
                SpeedingDrift plainDrive) =
            CrateBeside(anchorKind, numericFrames: false);
        using (world)
        using (plain)
        {
            Tick(plain, (int)world.TickNumber);
            DPose anchorAtStop = world.PoseIn(anchor, space);
            drive.Stopped = true;
            plainDrive.Stopped = true;
            // The tick writes no motion intent, and the step of the next tick applies none.
            world.Tick();
            plain.Tick();
            world.Tick();
            plain.Tick();
            // The sync point after the step may move the crate out of the frame, which keeps its sleep when the two homes'
            // velocities agree, as the still frame's and space's do.
            IsAwake(backend, world.PhysicsHomeOf(crate), crate).Should()
                .BeTrue("the frame's push changed by 2 km/s in one step, so its members wake");

            for (int tick = 0; tick < 58; tick++)
            {
                world.Tick();
                plain.Tick();
                (world.PoseIn(crate, space).Position - plain.PoseIn(plainCrate, plainSpace).Position).Length
                    .Should().BeLessThan(1e-3, "the crate is where it is without numeric frames at sync point {0}",
                        world.TickNumber);
            }

            (world.VelocityIn(crate, space) - Drift).Length.Should()
                .BeLessThan(1e-3, "the crate keeps the drift's velocity in space");
            (world.VelocityIn(crate, space) - plain.VelocityIn(plainCrate, plainSpace)).Length.Should()
                .BeLessThan(1e-3);
            world.VelocityIn(anchor, space).Should().Be(Double3.Zero, "the anchor holds still");
            plain.VelocityIn(plainAnchor, plainSpace).Should().Be(Double3.Zero);
            (world.PoseIn(anchor, space).Position - plain.PoseIn(plainAnchor, plainSpace).Position).Length
                .Should().BeLessThan(1e-6, "the anchor holds still where it does without numeric frames");
            (world.PoseIn(anchor, space).Position - anchorAtStop.Position).Length.Should().BeLessThan(
                Drift.Length * world.DeltaTime * 1.5,
                "the anchor stopped within one step of its drift");
            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The anchor keeps its velocity for ten seconds. The frame's push stays at zero, so the crate sleeps on.
    /// </summary>
    [Theory]
    [InlineData("plain body")]
    [InlineData("hull")]
    public void ACrateAsleepInTheFollowingFrameOfAnAnchorAtASteadyVelocityStaysAsleep(string anchorKind)
    {
        (World world, JoltPhysicsBackend backend, FrameHandle space, Entity anchor, Entity crate, SpeedingDrift _) =
            CrateAsleepBeside(anchorKind);
        using (world)
        {
            FrameHandle home = world.PhysicsHomeOf(crate);
            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                IsAwake(backend, home, crate).Should().BeFalse("nothing wakes the crate at tick {0}", world.TickNumber);
            }

            world.PhysicsHomeOf(crate).Should().Be(home);
            world.Frames.Anchors(home, anchor).Should().BeTrue();
            (world.VelocityIn(crate, space) - Drift).Length.Should()
                .BeLessThan(1e-3, "the crate moves with the anchor");
            world.Errors.Should().BeEmpty();
        }
    }
    // ---- A resting frame ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes in every tick a sample at the origin with no velocity and the acceleration <see cref="Acceleration"/>, so its
    /// frame holds still and has that proper acceleration in free space.
    /// </summary>
    private sealed class Engine : IComponent
    {
        /// <summary>The proper acceleration that the frame gets, in m/s², in its parent's axes. The test sets it between two ticks.</summary>
        public Double3 Acceleration { get; set; }

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveOnRails(new(Pose: At(0), ChordVelocity: Double3.Zero, Velocity: Double3.Zero, Acceleration: Acceleration, AngularVelocity: Double3.Zero, AngularAcceleration: Double3.Zero));
    }

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a space without gravity, and at its origin a
    /// ship, a frame without a body or gravity, with a bubble of radius 10,000 km, which an <see cref="Engine"/> holds still.
    /// A crate floats at rest 9,000 km from the ship's origin, past the 2^23 m (8,389 km) from which a slow body gets a
    /// resting frame. The scene ticks until the crate sleeps.
    /// </summary>
    private static (World World, JoltPhysicsBackend Backend, FrameHandle Ship, Entity Crate, Engine Engine)
        CrateAsleepFarOutInAShip(bool numericFrames)
    {
        World world = NewWorld(backend: out JoltPhysicsBackend backend,
            configure: numericFrames
                ? null
                : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f)));
        var engine = new Engine();
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
            { Frame = space, AsFrame = new(FrameBounds.Sphere(1e7f)), Components = [engine] }));
        Entity crate = world.Spawn(new() { Frame = ship, Pose = At(9_000_000), Body = Crate });
        world.Tick();
        FrameHandle home = world.PhysicsHomeOf(crate);
        world.Frames.Numeric(home).Should().Be(numericFrames ? NumericMode.Resting : NumericMode.None,
            "the crate rests 9,000 km out in the ship");
        TickUntil(world, () => !IsAwake(backend, home, crate), 600);
        IsAwake(backend, home, crate).Should().BeFalse("the crate is at rest, so it falls asleep");
        return (world, backend, ship, crate, engine);
    }

    /// <summary>
    /// The ship's engine steps the ship's proper acceleration from zero to 0.05 m/s², past the 0.02 m/s² that wakes a frame's
    /// members. The crate wakes at the same tick with numeric frames as without them, and after one second it has fallen
    /// against the push, 2.5 cm along -Y, as without them. Before ter-zg77, the push of a numeric frame was zero, so the
    /// crate slept on in its resting frame and rode along with the ship.
    /// </summary>
    [Fact]
    public void ACrateAsleepInARestingFrameWakesWhenItsSemanticFramesProperAccelerationStepsAsWithoutNumericFrames()
    {
        var woke = new List<long>();
        var fell = new List<double>();
        foreach (bool numericFrames in new[] { true, false })
        {
            (World world, JoltPhysicsBackend backend, FrameHandle ship, Entity crate, Engine engine) =
                CrateAsleepFarOutInAShip(numericFrames);
            using (world)
            {
                double startY = world.PoseIn(crate, ship).Position.Y;
                engine.Acceleration = new(0, 0.05, 0);
                long wokeAt = -1;
                for (int tick = 0; tick < 60; tick++)
                {
                    world.Tick();
                    if (wokeAt < 0 && IsAwake(backend, world.PhysicsHomeOf(crate), crate))
                        wokeAt = world.TickNumber;
                }

                world.Frames.Numeric(world.PhysicsHomeOf(crate)).Should()
                    .Be(numericFrames ? NumericMode.Resting : NumericMode.None);
                world.Errors.Should().BeEmpty();
                woke.Add(wokeAt);
                fell.Add(startY - world.PoseIn(crate, ship).Position.Y);
            }
        }

        woke[1].Should().BeGreaterThan(0, "the push of the ship's own system moved, so its members wake");
        woke[0].Should().Be(woke[1],
            "the members of the resting frame wake when the members of the ship's own system do");
        fell[1].Should().BeInRange(0.015, 0.03, "the crate fell against the push of 0.05 m/s² for nearly one second");
        fell[0].Should()
            .BeApproximately(fell[1], 1e-6, "the crate falls in the resting frame as in the ship's own system");
    }

    /// <summary>The ship's proper acceleration holds at zero for ten seconds, so the resting frame's push holds, and the crate sleeps on.</summary>
    [Fact]
    public void ACrateAsleepInARestingFrameWhoseSemanticFramesProperAccelerationHoldsSteadyStaysAsleep()
    {
        (World world, JoltPhysicsBackend backend, FrameHandle _, Entity crate, Engine _) =
            CrateAsleepFarOutInAShip(numericFrames: true);
        using (world)
        {
            FrameHandle home = world.PhysicsHomeOf(crate);
            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                IsAwake(backend, home, crate).Should().BeFalse("nothing wakes the crate at tick {0}", world.TickNumber);
            }

            world.PhysicsHomeOf(crate).Should().Be(home);
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A plain kinematic ball on a low orbit of earth at 7.66 km/s, which its <see cref="KeplerMotion"/> moves, rests in a
    /// following frame, and a crate floats 15 cm beside it, out of the orbit's plane, at the ball's velocity. The frame's
    /// acceleration is the field at its origin, so its push stays near zero, though that field of 8.6 m/s² turns with the
    /// orbit by 0.3 m/s² in 30 s. The crate falls asleep and sleeps on for 30 s.
    /// </summary>
    [Fact]
    public void ACrateAsleepInTheFollowingFrameOfAnAnchorOnALowKeplerOrbitStaysAsleep()
    {
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        FrameHandle earth = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(Scenes.EarthMu)));
        KeplerOrbit orbit = KeplerOrbit.Circular(6.8e6, phase: 0.4);
        Entity ball =
            world.Spawn(
                new SpawnDesc { Frame = earth, Body = Ball() with { Motion = BodyMotion.Kinematic } }.OnKeplerOrbit(
                    world, orbit, firstMove: true));
        Tick(world, 2);
        // The crate's bounding sphere touches the ball's, so the merge test links the two, and a spawn that joins a numeric
        // frame is created in it: the crate's velocity relative to the frame narrows to a float only as a small difference,
        // and the crate starts at rest beside the ball, 15 cm from it.
        Entity crate = world.Spawn(new()
        {
            Frame = earth,
            Pose = new(world.PoseIn(ball, earth).Position + new Double3(0, 0.9, 0), DQuaternion.Identity),
            LinearVelocity = world.VelocityIn(ball, earth), Body = Crate
        });
        world.Tick();
        FrameHandle home = world.PhysicsHomeOf(crate);
        world.Frames.Anchors(home, ball).Should().BeTrue("the crate joins the ball's following frame");
        TickUntil(world, () => !IsAwake(backend, home, crate), 600);
        IsAwake(backend, home, crate).Should().BeFalse("the crate rests beside the ball, so it falls asleep");

        for (int tick = 0; tick < 1800; tick++)
        {
            world.Tick();
            IsAwake(backend, home, crate).Should().BeFalse("nothing wakes the crate at tick {0}", world.TickNumber);
        }

        world.PhysicsHomeOf(crate).Should().Be(home);
        world.Errors.Should().BeEmpty();
    }
}

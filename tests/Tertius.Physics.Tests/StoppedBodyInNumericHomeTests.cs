using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A body that physics stops holds still in its semantic frame, whatever numeric frame holds it, as it does without numeric
/// frames (docs/plans/numeric-frames.md, "Limits", and ticket ter-unpc). These tests pin it for a plain kinematic ball that
/// lives as a member, not as an anchor, in the falling frame of a crate that thrusts: after its end poses stop, after a
/// teleport, and after a move out of that frame or into one, where <see cref="Self.PoseAtNextStepStartIn"/> gives its pose
/// now. They pin it for a dynamic box and a dynamic hull in such a frame after their steer targets stop. The world runs on
/// the real backend.
/// </summary>
public sealed class StoppedBodyInNumericHomeTests
{
    /// <summary>
    /// The velocity of the group along +X in the semantic frame, in m/s, from the spawn on. It is below the drain limit of
    /// 1 m/s, so no drain moves a member's velocity in the home into the home's velocity, and the home keeps this velocity.
    /// </summary>
    private static readonly Double3 Drift = new(0.5, 0, 0);

    /// <summary>
    /// Membership rules under which a group that thrusts falls at any speed and never stops falling for being slow, and a
    /// group that does not thrust never gets a numeric frame by its speed. So no kinematic body follows in a frame of its
    /// own, and a plain kinematic body that the merge test links to a body that thrusts lives in that body's falling frame
    /// as a member.
    /// </summary>
    private static readonly NumericFrameOptions ThrustingGroupsFall = new()
    {
        CreateSpeed = double.PositiveInfinity,
        LeaveSpeed = double.PositiveInfinity,
        ThrustCreateSpeed = 0,
        ThrustLeaveSpeed = 0,
    };

    /// <summary>What a reader saw in one tick: the read of the next step's start, and the pose now, both in the entity's frame.</summary>
    private readonly record struct Seen(DPose NextStepStart, DPose Now);

    /// <summary>Writes nothing. In every tick it keeps the two reads of its entity, in the frame that the entity is in.</summary>
    private sealed class Reader : IComponent
    {
        public List<Seen> Seen { get; } = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            Seen.Add(new(self.PoseAtNextStepStartIn(self.Frame), self.PoseIn(self.Frame)));
    }

    /// <summary>
    /// Steers its dynamic body along at <paramref name="velocity"/>, in m/s in the axes of the body's frame, in every tick:
    /// the target is two steps of that velocity from the pose now, because the step beside the tick runs first from there.
    /// </summary>
    private sealed class SteerAlong(Double3 velocity) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            DPose now = self.PoseIn(self.Frame);
            self.Body.SteerTo(new(now.Position + velocity * (2 * self.DeltaTime), now.Rotation));
        }
    }

    /// <summary>The scene of these tests: the ball, its pilot and the crate.</summary>
    private readonly record struct Group(Entity Ball, Pilot Pilot, Entity Crate);

    /// <summary>
    /// A plain kinematic ball that end poses carry at <see cref="Drift"/> through space without gravity, and a 10 kg crate
    /// 3 m away that flies with it and thrusts gently, after 40 ticks: the two live in one falling frame that moves at
    /// <see cref="Drift"/> (<see cref="ThrustingGroupsFall"/>).
    /// </summary>
    private static Group DriftingGroup(World world, FrameHandle space)
    {
        world.Frames.NumericOptions = ThrustingGroupsFall;
        var pilot = new Pilot();
        Entity ball = world.Spawn(
            KinematicDrift.Drifting(
                new()
                {
                    Frame = space,
                    Body = Ball() with { Motion = BodyMotion.Kinematic },
                    Components = [pilot],
                },
                world,
                Drift
            )
        );
        Entity crate = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 0, 3),
                Body = Cube(),
                LinearVelocity = Drift.ToVector3(),
                Components = [new Thruster(10)],
            }
        );
        Tick(world, 40);
        FrameHandle home = world.PhysicsHomeOf(ball);
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling);
        world.PhysicsHomeOf(crate).Should().Be(home);
        return new(ball, pilot, crate);
    }

    /// <summary>Removes the ball's <see cref="KinematicDrift"/>, so that it writes no more end poses from the next tick on.</summary>
    private static void StopDrift(World world, Entity ball) =>
        world.RemoveComponent(ball, world.GetComponent<KinematicDrift>(ball)!).Should().BeTrue();

    /// <summary>
    /// The ball's end poses stop. In the steps after, it has no motion intent, so it holds still in space, its semantic
    /// frame, while its falling home flies on with the crate at 0.5 m/s. Before ter-unpc the stop held it still in its home,
    /// so it flew on with the home at its last speed for as long as it lived there.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyWhoseEndPosesStopHoldsStillInSpaceAndNotInItsFallingHome()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        Group group = DriftingGroup(world, space);
        FrameHandle home = world.PhysicsHomeOf(group.Ball);

        StopDrift(world, group.Ball);
        // The step beside this tick applies the last end pose.
        world.Tick();
        DPose stopped = world.PoseIn(group.Ball, space);
        Tick(world, 10);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(group.Ball).Should().Be(home, "the ball still lives in the falling frame with the crate");
        (world.PoseIn(group.Ball, space).Position - stopped.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the ball holds still in space over ten steps");
        world.VelocityIn(group.Ball, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// After its end poses stop, the ball teleports in place without a motion intent, and the step after has neither. The
    /// read of the next step's start in that step's tick gives the place where the ball stands when the step ends: where it
    /// stood, because it holds still in space. Before ter-unpc the teleport left the ball still in space and the read moved
    /// it with its home, one step of the home's motion, 8.33 mm, away from where the step left it.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyThatTeleportsInAFallingHomeStartsTheNextStepWhereItStands()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        Group group = DriftingGroup(world, space);
        FrameHandle home = world.PhysicsHomeOf(group.Ball);

        StopDrift(world, group.Ball);
        Tick(world, 2);
        DPose stand = world.PoseIn(group.Ball, space);
        group.Pilot.Once = body => body.Teleport(stand);
        // The step beside the second tick teleports the ball without a motion intent.
        Tick(world, 2);
        var reader = new Reader();
        world.AddComponent(group.Ball, reader);
        // The step beside the first tick has neither a motion intent nor a teleport.
        Tick(world, 2);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(group.Ball).Should().Be(home, "the ball still lives in the falling frame with the crate");
        (Seen gap, Seen after) = (reader.Seen[0], reader.Seen[1]);
        (after.Now.Position - stand.Position).Length.Should().BeLessThan(1e-6, "the ball holds still in space");
        (gap.NextStepStart.Position - after.Now.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the ball ends the step where the read of the next step's start said");
        world.VelocityIn(group.Ball, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// The ball teleports 1 m to the side in the step right after its last end pose. The step stops the ball before it
    /// applies the teleport, so the ball rests in space at the teleport's pose when the step ends, and holds still there.
    /// Before ter-unpc the stop came after the teleport and held the ball still in its home, which carried it 8.33 mm on.
    /// </summary>
    [Fact]
    public void ATeleportInTheStepRightAfterTheLastEndPoseLeavesTheBodyAtRestInSpaceAtTheTeleportPose()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        Group group = DriftingGroup(world, space);

        StopDrift(world, group.Ball);
        DPose aside = world.PoseIn(group.Ball, space);
        aside = new(aside.Position + new Double3(0, 1, 0), aside.Rotation);
        group.Pilot.Once = body => body.Teleport(aside);
        // The step beside the first tick applies the last end pose, and the step beside the second the teleport.
        Tick(world, 2);
        DPose landed = world.PoseIn(group.Ball, space);
        Tick(world, 3);

        world.Errors.Should().BeEmpty();
        (landed.Position - aside.Position).Length.Should().BeLessThan(1e-6, "the teleport's step ends at its pose");
        (world.PoseIn(group.Ball, space).Position - aside.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the ball holds still in space after the teleport");
    }

    /// <summary>
    /// After the ball's end poses stop, the crate goes, so the ball's group no longer thrusts, and the ball moves out of
    /// the falling frame into space, its semantic frame, at a sync point. It holds still in space before and after the move.
    /// Before ter-unpc the ball rested in its home, and the move gave it the home's velocity, so it flew on at 0.5 m/s in
    /// space for good.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyThatLeavesAFallingHomeWithoutAMotionIntentHoldsStillInSpace()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle space = Ground(world, 0);
        Group group = DriftingGroup(world, space);

        StopDrift(world, group.Ball);
        Tick(world, 2);
        DPose stopped = world.PoseIn(group.Ball, space);
        world.Destroy(group.Crate).Should().BeTrue();
        TickUntil(world, () => world.PhysicsHomeOf(group.Ball) == space, limit: 60);
        Tick(world, 10);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(group.Ball).Should().Be(space, "a group that does not thrust gets no numeric frame");
        (world.PoseIn(group.Ball, space).Position - stopped.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the ball holds still in space across the move");
        world.VelocityIn(group.Ball, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// A still plain kinematic ball and a crate that thrusts beside it, in a weak uniform gravity of 0.1 m/s², live in one
    /// falling frame (see <see cref="AStillPlainKinematicBodyHoldsStillInSpaceInAHomeThatFallsWithGravity"/>). The ball
    /// teleports 1 m up without a motion intent, and rests in space at the teleport's pose at the end of the step and after
    /// it, although its home falls ever faster. Before ter-unpc the teleport's velocity left out half a step of the home's
    /// acceleration, so the ball fell 14 µm in the step.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyThatTeleportsInAHomeThatFallsWithGravityRestsAtTheTeleportPose()
    {
        using World world = NewWorld(backend: out _);
        world.Frames.NumericOptions = ThrustingGroupsFall;
        FrameHandle space = Ground(world, 0.1f);
        var pilot = new Pilot();
        Entity ball = world.Spawn(
            new()
            {
                Frame = space,
                Body = Ball() with { Motion = BodyMotion.Kinematic },
                Components = [pilot],
            }
        );
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 0, 3),
                Body = Cube(),
                Components = [new Thruster(10)],
            }
        );
        TickUntil(world, () => world.PhysicsHomeOf(ball) != space, limit: 120);
        FrameHandle home = world.PhysicsHomeOf(ball);
        Tick(world, 10);
        DPose up = At(0, 1);
        pilot.Once = body => body.Teleport(up);
        // The step beside the second tick teleports the ball.
        Tick(world, 2);
        DPose landed = world.PoseIn(ball, space);
        Tick(world, 5);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(ball).Should().Be(home, "the ball still lives in the falling frame with the crate");
        (landed.Position - up.Position).Length.Should().BeLessThan(1e-6, "the teleport's step ends at its pose");
        (world.PoseIn(ball, space).Position - up.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the ball holds still in space after the teleport");
    }

    /// <summary>
    /// A still plain kinematic ball in a weak uniform gravity of 0.1 m/s², and a crate 3 m away that starts at rest and
    /// thrusts gently. The crate's group gets a falling frame, which falls with the gravity, and the ball moves into it
    /// without a motion intent. The ball holds still in space all along, which in its accelerating home is a velocity that
    /// changes in every step: the home's velocity at the end of the step, reversed. Before ter-unpc the move gave the ball
    /// the home's velocity once, and the home then fell away from it.
    /// </summary>
    [Fact]
    public void AStillPlainKinematicBodyHoldsStillInSpaceInAHomeThatFallsWithGravity()
    {
        using World world = NewWorld(backend: out _);
        world.Frames.NumericOptions = ThrustingGroupsFall;
        FrameHandle space = Ground(world, 0.1f);
        Entity ball = world.Spawn(new() { Frame = space, Body = Ball() with { Motion = BodyMotion.Kinematic } });
        Entity crate = world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 0, 3),
                Body = Cube(),
                Components = [new Thruster(10)],
            }
        );
        TickUntil(world, () => world.PhysicsHomeOf(ball) != space, limit: 120);
        FrameHandle home = world.PhysicsHomeOf(ball);
        Tick(world, 30);

        world.Errors.Should().BeEmpty();
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling);
        world.PhysicsHomeOf(ball).Should().Be(home, "the ball still lives in the falling frame with the crate");
        world.PhysicsHomeOf(crate).Should().Be(home);
        world.Frames.HomeVelocity(home, space).Y.Should().BeLessThan(-0.04, "the home falls with the gravity");
        world.PoseIn(ball, space).Position.Length.Should().BeLessThan(1e-6, "the ball holds still in space");
        world.VelocityIn(ball, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>A dynamic body without damping, which keeps any velocity that physics gives it in its home.</summary>
    private static BodyDesc Undamped(BodyDesc body) => body with { LinearDamping = 0, AngularDamping = 0 };

    /// <summary>Removes the steering of <paramref name="entity"/>, so that it writes no more steer targets from the next tick on.</summary>
    private static void StopSteering(World world, Entity entity) =>
        world.RemoveComponent(entity, world.GetComponent<SteerAlong>(entity)!).Should().BeTrue();

    /// <summary>
    /// A dynamic box without damping that steer targets carry at <see cref="Drift"/> through space without gravity, beside
    /// a crate that thrusts, as in <see cref="DriftingGroup"/>. Its steer targets stop. In the step after the last one physics
    /// stops it, and it holds still in space, its semantic frame, while its falling home flies on with the crate at 0.5 m/s.
    /// Before the stop gave a dynamic body the rest of its semantic frame, the box stopped in its home and flew on with it.
    /// </summary>
    [Fact]
    public void ADynamicBodyWhoseSteerTargetsStopHoldsStillInSpaceAndNotInItsFallingHome()
    {
        using World world = NewWorld(backend: out _);
        world.Frames.NumericOptions = ThrustingGroupsFall;
        FrameHandle space = Ground(world, 0);
        Entity box = world.Spawn(
            new()
            {
                Frame = space,
                Body = Undamped(Cube()),
                LinearVelocity = Drift.ToVector3(),
                Components = [new SteerAlong(Drift)],
            }
        );
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 0, 3),
                Body = Cube(),
                LinearVelocity = Drift.ToVector3(),
                Components = [new Thruster(10)],
            }
        );
        Tick(world, 40);
        FrameHandle home = world.PhysicsHomeOf(box);
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling);

        StopSteering(world, box);
        // The step beside this tick applies the last steer target.
        world.Tick();
        DPose stopped = world.PoseIn(box, space);
        Tick(world, 10);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(box).Should().Be(home, "the box still lives in the falling frame with the crate");
        (world.PoseIn(box, space).Position - stopped.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the box holds still in space over ten steps");
        world.VelocityIn(box, space).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>What a stopped box did over the ten steps after the step that stopped it, in space.</summary>
    private readonly record struct StoppedFall(Double3 Move, Double3 Velocity);

    /// <summary>
    /// A dynamic box without damping that steer targets hold in place in a weak uniform gravity of 0.1 m/s², beside a crate
    /// that thrusts, under the membership rules <paramref name="options"/>, which put it in a home of <paramref name="homeMode"/>.
    /// Then its steer targets stop, and the method gives how the box moves in space over the ten steps after
    /// the last steer target's step.
    /// </summary>
    private static StoppedFall StopInGravity(NumericFrameOptions options, NumericMode homeMode)
    {
        using World world = NewWorld(backend: out _);
        world.Frames.NumericOptions = options;
        FrameHandle space = Ground(world, 0.1f);
        Entity box = world.Spawn(
            new()
            {
                Frame = space,
                Body = Undamped(Cube()),
                Components = [new SteerAlong(Double3.Zero)],
            }
        );
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 0, 3),
                Body = Cube(),
                Components = [new Thruster(10)],
            }
        );
        Tick(world, 60);
        world.Frames.Numeric(world.PhysicsHomeOf(box)).Should().Be(homeMode);

        StopSteering(world, box);
        world.Tick();
        DPose stopped = world.PoseIn(box, space);
        Tick(world, 10);

        world.Errors.Should().BeEmpty();
        return new(world.PoseIn(box, space).Position - stopped.Position, world.VelocityIn(box, space));
    }

    /// <summary>
    /// A dynamic box that physics stops in a home that falls with gravity starts to fall in space from rest, as it does
    /// without numeric frames: the stop is Jolt's velocity of zero in the system of its semantic frame, carried into its
    /// home. Before the stop gave a dynamic body the rest of its semantic frame, the box stopped in its home and fell on
    /// with it at the home's speed.
    /// </summary>
    [Fact]
    public void ADynamicBodyThatStopsInAHomeThatFallsWithGravityFallsAsWithoutNumericFrames()
    {
        StoppedFall numeric = StopInGravity(ThrustingGroupsFall, NumericMode.Falling);
        StoppedFall plain = StopInGravity(NumericFrameTesting.WithoutNumericFrames, NumericMode.None);

        plain.Move.Y.Should().BeLessThan(-1e-3, "the box falls 55 steps of g dt² in ten steps after the stop");
        (numeric.Move - plain.Move).Length.Should().BeLessThan(1e-7);
        (numeric.Velocity - plain.Velocity).Length.Should().BeLessThan(1e-6);
    }

    /// <summary>
    /// A ship with a dynamic hull without damping that steer targets carry at <see cref="Drift"/> beside a crate that
    /// thrusts, so the hull lives in the crate's falling frame. Its steer targets stop. The ship holds still in space, and
    /// the place of its frame agrees with the place where physics put the hull.
    /// </summary>
    [Fact]
    public void ADynamicHullWhoseSteerTargetsStopHoldsStillInSpaceWithItsFrame()
    {
        using World world = NewWorldWithFaults(faults: out FaultInjectingBackend faults);
        world.Frames.NumericOptions = ThrustingGroupsFall;
        FrameHandle space = Ground(world, 0);
        Entity ship = world.Spawn(
            new()
            {
                Frame = space,
                Body = Undamped(Ball()),
                AsFrame = new(FrameBounds.Sphere(1)),
                LinearVelocity = Drift.ToVector3(),
                Components = [new SteerAlong(Drift)],
            }
        );
        world.Spawn(
            new()
            {
                Frame = space,
                Pose = At(0, 0, 4),
                Body = Cube(),
                LinearVelocity = Drift.ToVector3(),
                Components = [new Thruster(10)],
            }
        );
        Tick(world, 40);
        FrameHandle home = world.PhysicsHomeOf(ship);
        world.Frames.Numeric(home).Should().Be(NumericMode.Falling);

        StopSteering(world, ship);
        // The step beside this tick applies the last steer target.
        world.Tick();
        DPose stopped = world.PoseIn(ship, space);
        Tick(world, 10);

        world.Errors.Should().BeEmpty();
        world.PhysicsHomeOf(ship).Should().Be(home, "the hull still lives in the falling frame with the crate");
        DPose frame = world.PoseIn(ship, space);
        (frame.Position - stopped.Position).Length.Should().BeLessThan(1e-6, "the ship holds still in space");
        world.VelocityIn(ship, space).Length.Should().BeLessThan(1e-6);
        BodyState hull = faults.Outputs[^1].Bodies.Single(state => state.Entity == ship);
        hull.Frame.Should().Be(home, "physics reports the hull in its home");
        (hull.Pose.Position + world.Frames.HomeOffset(home, space) - frame.Position)
            .Length.Should()
            .BeLessThan(1e-6, "the frame stands where physics put the hull");
    }
}

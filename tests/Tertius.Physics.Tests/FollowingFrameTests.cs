using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Following frames for motion that is no orbit (test 8 of docs/plans/rails-components.md, ticket ter-zg77): a numeric frame
/// that holds a fast or far kinematic body at its origin, its anchor, and lands at every sync point on the step record of
/// the anchor's motion intent (docs/plans/numeric-frames.md, "Following frames"). A test component drives the anchor along a
/// straight line at 2 km/s with end poses, in a frame without gravity, so the line is the anchor's true path. The world runs
/// on the real backend.
/// </summary>
public class FollowingFrameTests
{
    /// <summary>The velocity of the anchor's drift in space, in m/s: 2 km/s along +X, above the 1,000 m/s from which a free body gets a numeric frame.</summary>
    private static readonly Double3 Drift = new(2_000, 0, 0);

    /// <summary>The radius of the hull's bubble, in meters.</summary>
    private const float BubbleRadius = 16;

    /// <summary>
    /// The hull: a core, a box of 4 m at the frame's origin, and a plate of 16 m by 0.5 m by 16 m outside the bubble, across
    /// the +Y axis, whose face toward the core is 19.75 m from it. Its bounding sphere reaches past the bubble, so a crate that
    /// leaves the bubble along +Y stays within the merge test's reach of the hull. It is kinematic, so only its motion intents
    /// move it.
    /// </summary>
    private static readonly BodyDesc Hull = new(new CompoundShapeDesc([
        new CompoundChild(new BoxShapeDesc(new Vector3(2)), new Pose(Vector3.Zero, Quaternion.Identity)),
        new CompoundChild(new BoxShapeDesc(new Vector3(8, 0.25f, 8)), new Pose(new Vector3(0, 20, 0), Quaternion.Identity)),
    ]), BodyMotion.Kinematic, Mass: 1e5f, Restitution: 0);

    /// <summary>A crate, a box of 50 cm and 2 kg that nothing slows down and that does not bounce.</summary>
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.25f)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>
    /// A world on the real backend, a frame without gravity, and in it at the origin the owner of a frame whose kinematic
    /// hull <see cref="KinematicDrift"/> moves at <see cref="Drift"/>. The hull keeps its spawn velocity as its frame's
    /// velocity, and its first motion intent is the end pose of its first step, so it moves from its first step on.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner, FrameHandle Frame) DriftingHull()
    {
        World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = Hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(BubbleRadius)),
            LinearVelocity = Drift,
            FirstMove = new MotionIntent(new EndPose(new DPose(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new KinematicDrift(Drift)],
        });
        return (world, space, owner, world.GetOwnedFrame(owner));
    }

    /// <summary>
    /// The hull gets a following frame at its first sync point and rests at its origin, with its frame hung there, for ten
    /// seconds of the drift: the frame's place in the following frame is zero and so is its velocity there. Every read of
    /// the hull in space is on the line of the drift, with the drift's velocity.
    /// </summary>
    [Fact]
    public void AHullThatEndPosesDriveAt2KmPerSecondAlongALineRestsAtTheOriginOfAFollowingFrame()
    {
        (World world, FrameHandle space, Entity owner, FrameHandle frame) = DriftingHull();
        using (world)
        {
            world.Tick();
            FrameHandle home = world.PhysicsHomeOf(owner);
            world.Frames.Numeric(home).Should().Be(NumericMode.Orbiting, "the hull moves at 2 km/s, so it lives in a following frame");
            world.Frames.TreeParent(frame).Should().Be(home, "the hull's frame hangs at the origin of the following frame");

            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                world.PhysicsHomeOf(owner).Should().Be(home, "the hull keeps its following frame at tick {0}", world.TickNumber);
                world.Frames.TreeLocalPose(frame).Position.Should().Be(Double3.Zero, "the hull rests at the origin at tick {0}", world.TickNumber);
                world.Frames.TreeLinearVelocity(frame).Should().Be(Double3.Zero, "an end pose's record has no velocity past its chord");
                DPose pose = world.PoseIn(owner, space);
                (pose.Position - Drift * world.Time).Length.Should().BeLessThan(1e-6, "the hull is on the line of the drift at tick {0}", world.TickNumber);
                (world.VelocityIn(owner, space) - Drift).Length.Should().BeLessThan(1e-9, "the hull moves at the drift's velocity");
            }
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A crate floats in the hull's frame 10 m along +Y from its core and drifts along +Y at 1 m/s relative to it. It leaves
    /// the bubble of 16 m into the frame's tree parent, the hull's following frame, and stays there, linked to the hull, at
    /// a local velocity of about 1 m/s: a numeric frame keeps a body slow when the body moves with the anchor. Its place
    /// relative to the hull stays on the line of its drift.
    /// </summary>
    [Fact]
    public void ACrateThatDriftsOutOfTheHullsFrameJoinsItsFollowingFrame()
    {
        (World world, FrameHandle space, Entity owner, FrameHandle frame) = DriftingHull();
        using (world)
        {
            Entity crate = world.Spawn(new SpawnDesc { Frame = frame, Pose = At(0, 10), LinearVelocity = new Double3(0, 1, 0), Body = Crate });
            var homes = new List<string>();
            double fastest = 0;
            for (int tick = 0; tick < 480; tick++)
            {
                world.Tick();
                FrameHandle home = world.PhysicsHomeOf(crate);
                FrameHandle hullHome = world.PhysicsHomeOf(owner);
                string kind = home == frame ? "hull's frame" : home == hullHome ? "following frame" : world.Frames.Numeric(home).ToString();
                if (homes.Count == 0 || homes[^1] != kind)
                    homes.Add(kind);
                if (home == hullHome)
                    fastest = Math.Max(fastest, (world.VelocityIn(crate, space) - world.Frames.LinearVelocity(home)).Length);
            }
            world.Frames.Numeric(world.PhysicsHomeOf(owner)).Should().Be(NumericMode.Orbiting);
            homes.Should().Equal(["hull's frame", "following frame"], "the crate leaves into the frame's tree parent and stays linked to the hull");
            world.GetFrame(crate).Should().Be(space, "gameplay sees the crate in space");
            fastest.Should().BeInRange(0.9, 1.1, "the crate is slow in the following frame");
            (world.PoseIn(crate, space).Position - world.PoseIn(owner, space).Position - new Double3(0, 10 + 480.0 / 60, 0)).Length
                .Should().BeLessThan(0.01, "the crate drifts on in a straight line relative to the hull, 1 m/s for 480 ticks");
            world.Errors.Should().BeEmpty();
        }
    }

    // ---- A plain kinematic body as the anchor ------------------------------------------------------------------------

    /// <summary>A plain kinematic ball of radius 0.5 m and 1 kg, the body of an entity that owns no frame.</summary>
    private static BodyDesc KinematicBall => Ball() with { Motion = BodyMotion.Kinematic };

    /// <summary>
    /// A world on the real backend, a frame without gravity, and in it at the origin a plain kinematic ball that
    /// <see cref="KinematicDrift"/> moves at <see cref="Drift"/> from its first step on. A plain kinematic body takes no spawn
    /// velocity, so it is at rest at the sync point of its spawn.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Ball) DriftingBall()
    {
        World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        Entity ball = world.Spawn(KinematicDrift.Drifting(new SpawnDesc { Frame = space, Body = KinematicBall }, world, Drift));
        return (world, space, ball);
    }

    /// <summary>
    /// The ball gets a following frame at the sync point after its first step, the first at which it moves, and it is that
    /// frame's anchor. It rests at the frame's origin for ten seconds of the drift: every read of the ball in space is the
    /// frame's place, which is on the line of the drift, and the ball moves at the drift's velocity.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyThatEndPosesDriveAt2KmPerSecondRestsAtTheOriginOfAFollowingFrame()
    {
        (World world, FrameHandle space, Entity ball) = DriftingBall();
        using (world)
        {
            Tick(world, 2);
            FrameHandle home = world.PhysicsHomeOf(ball);
            world.Frames.Numeric(home).Should().Be(NumericMode.Orbiting, "the ball moves at 2 km/s, so it lives in a following frame");
            world.Frames.Anchors(home, ball).Should().BeTrue("the ball is the anchor of its following frame");
            world.Frames.TreeParent(home).Should().Be(space);

            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                world.PhysicsHomeOf(ball).Should().Be(home, "the ball keeps its following frame at tick {0}", world.TickNumber);
                DPose pose = world.PoseIn(ball, space);
                pose.Position.Should().Be(world.Frames.TreeLocalPose(home).Position, "the ball rests at the frame's origin at tick {0}", world.TickNumber);
                (pose.Position - Drift * world.Time).Length.Should().BeLessThan(1e-6, "the ball is on the line of the drift at tick {0}", world.TickNumber);
                (world.VelocityIn(ball, space) - Drift).Length.Should().BeLessThan(1e-9, "the ball moves at the drift's velocity");
            }
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A crate floats 2 m beside the ball and drifts away from it at 0.5 m/s. The merge test links the two, so the crate's
    /// group takes the ball's following frame, where the crate is slow, and it drifts on there in a straight line relative
    /// to the ball.
    /// </summary>
    [Fact]
    public void ACrateBesideAPlainKinematicBodyJoinsItsFollowingFrameAndDriftsOnThereRelativeToIt()
    {
        (World world, FrameHandle space, Entity ball) = DriftingBall();
        using (world)
        {
            Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 2), LinearVelocity = Drift + new Double3(0, 0.5, 0), Body = Crate });
            Tick(world, 2);
            FrameHandle home = world.PhysicsHomeOf(ball);
            world.Frames.Anchors(home, ball).Should().BeTrue("the ball lives in a following frame of its own");
            world.PhysicsHomeOf(crate).Should().Be(home, "the crate's group takes the ball's following frame");

            double fastest = 0;
            for (int tick = 0; tick < 240; tick++)
            {
                world.Tick();
                world.PhysicsHomeOf(crate).Should().Be(home, "the crate stays linked to the ball at tick {0}", world.TickNumber);
                fastest = Math.Max(fastest, (world.VelocityIn(crate, space) - world.Frames.LinearVelocity(home)).Length);
            }
            fastest.Should().BeInRange(0.45, 0.55, "the crate is slow in the following frame");
            (world.PoseIn(crate, space).Position - world.PoseIn(ball, space).Position - new Double3(0, 2 + 0.5 * world.Time, 0)).Length
                .Should().BeLessThan(0.01, "the crate drifts on in a straight line relative to the ball");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// A plain kinematic ball on a low orbit of earth at 7.66 km/s, which its <see cref="KeplerMotion"/> moves, rests in a
    /// following frame, and every read of it in earth is the orbit's place and rotation to the last bit, at every sync point
    /// for ten seconds.
    /// </summary>
    [Fact]
    public void APlainKinematicBodyOnALowKeplerOrbitStandsOnItsOrbitToTheLastBitInAFollowingFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), GravityField.Point(Scenes.EarthMu)));
        KeplerOrbit orbit = KeplerOrbit.Circular(6.8e6, phase: 0.4);
        Entity ball = world.Spawn(new SpawnDesc { Frame = earth, Body = KinematicBall }.OnKeplerOrbit(world, orbit, firstMove: true));
        Tick(world, 2);
        FrameHandle home = world.PhysicsHomeOf(ball);
        world.Frames.Anchors(home, ball).Should().BeTrue();

        for (int tick = 0; tick < 600; tick++)
        {
            world.Tick();
            world.PhysicsHomeOf(ball).Should().Be(home);
            world.PoseIn(ball, earth).Should().Be(new DPose(orbit.StateAt(world.Time, Scenes.EarthMu).Position, orbit.RotationAt(world.Time, Scenes.EarthMu)),
                "the ball stands on its orbit at sync point {0}", world.TickNumber);
            (world.VelocityIn(ball, earth) - orbit.StateAt(world.Time, Scenes.EarthMu).Velocity).Length.Should().BeLessThan(1e-6);
        }
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ball of <see cref="DriftingBall"/>, with the crate of the test above in its following frame, dies. Its following
    /// frame turns into a falling frame that falls on from its place at its velocity, and the crate keeps it, with its
    /// place and velocity in space.
    /// </summary>
    [Fact]
    public void TheFollowingFrameOfAPlainAnchorThatDiesFallsOnAndTheCrateInItKeepsItsMotion()
    {
        (World world, FrameHandle space, Entity ball) = DriftingBall();
        using (world)
        {
            Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 2), LinearVelocity = Drift + new Double3(0, 0.5, 0), Body = Crate });
            Tick(world, 60);
            FrameHandle home = world.PhysicsHomeOf(crate);
            world.Frames.Anchors(home, ball).Should().BeTrue();
            Double3 velocity = world.VelocityIn(crate, space);

            world.Destroy(ball);
            Tick(world, 60);

            world.PhysicsHomeOf(crate).Should().Be(home, "the crate keeps the frame");
            world.Frames.Numeric(home).Should().Be(NumericMode.Falling, "the frame lost its anchor and falls on");
            (world.VelocityIn(crate, space) - velocity).Length.Should().BeLessThan(1e-6, "the crate keeps its velocity in space");
            world.Errors.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Drives its kinematic body along a straight line at <paramref name="velocity"/>, as <see cref="KinematicDrift"/> does,
    /// and in the tick <paramref name="jumpTick"/> teleports it <paramref name="jump"/> ahead, from where it would start
    /// the next step, and drives it on from there in the same step.
    /// </summary>
    private sealed class JumpingDrift(Double3 velocity, long jumpTick, Double3 jump) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            DPose start = self.PoseAtNextStepStartIn(self.Frame);
            if (self.Tick == jumpTick)
            {
                start = new DPose(start.Position + jump, start.Rotation);
                self.Body.Teleport(start);
            }
            self.MoveKinematic(new DPose(start.Position + velocity * self.DeltaTime, start.Rotation));
        }
    }

    /// <summary>
    /// The ball, with the crate in its following frame, teleports 1 km ahead in one tick and drives on. In that step physics
    /// aims it at its plan's place, away from the origin of its frame, and at the sync point the frame jumps to it, so it
    /// rests at the origin again. The crate keeps its place and velocity in space: it does not jump with the ball.
    /// </summary>
    [Fact]
    public void TheFollowingFrameOfAPlainAnchorThatTeleportsJumpsWithItAndTheCrateInItStaysWhereItWas()
    {
        using World world = NewWorld(out _);
        FrameHandle space = Ground(world, 0);
        var jump = new Double3(1_000, 0, 0);
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = KinematicBall,
            FirstMove = new MotionIntent(new EndPose(new DPose(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new JumpingDrift(Drift, 60, jump)],
        });
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(0, 2), LinearVelocity = Drift + new Double3(0, 0.5, 0), Body = Crate });
        // Tick 60 writes the teleport, and the step of tick 61 applies it.
        Tick(world, 61);
        FrameHandle home = world.PhysicsHomeOf(ball);
        world.PhysicsHomeOf(crate).Should().Be(home);
        DPose crateBefore = world.PoseIn(crate, space);
        Double3 crateVelocity = world.VelocityIn(crate, space);

        world.Tick();

        world.PhysicsHomeOf(ball).Should().Be(home, "the ball keeps its following frame");
        world.PoseIn(ball, space).Position.Should().Be(world.Frames.TreeLocalPose(home).Position, "the ball rests at the frame's origin again");
        (world.PoseIn(ball, space).Position - (Drift * world.Time + jump)).Length.Should().BeLessThan(1e-6, "the ball is 1 km ahead on its line");
        (world.PoseIn(crate, space).Position - (crateBefore.Position + crateVelocity * world.DeltaTime)).Length.Should().BeLessThan(1e-6,
            "the crate does not jump with the ball");
        (world.VelocityIn(crate, space) - crateVelocity).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The far orbit of the scene of the orbit tests: 10,000 km round a point gravity of 4e10 m³/s², at 63 m/s, beyond the
    /// 2^23 m (8,389 km) from which a kinematic body gets a following frame by its distance, and a slow static body a resting
    /// frame. It starts at (10,000 km, 0, 0) and passes along -Z.
    /// </summary>
    private static readonly KeplerOrbit FarOrbit = KeplerOrbit.Circular(1e7);

    /// <summary>
    /// A root frame with the point gravity of <see cref="FarOrbit"/>, a plain kinematic ball of radius 2 m on that orbit with
    /// its first motion intent, and 100 m ahead on its path a crate at rest with <paramref name="log"/>, and 8 m to the
    /// orbit's outer side a static rock of radius 1 m, which the ball misses.
    /// </summary>
    private static (FrameHandle Space, Entity Ball, Entity Rock, Entity Crate) BallAndRock(World world, CollisionLog log)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(2e7f), GravityField.Point(4e10)));
        Entity ball = world.Spawn(new SpawnDesc
        {
            Frame = space, Body = new BodyDesc(new SphereShapeDesc(2), BodyMotion.Kinematic, Mass: 1e4f, Restitution: 0),
        }.OnKeplerOrbit(world, FarOrbit, firstMove: true));
        Entity rock = world.Spawn(new SpawnDesc { Frame = space, Pose = At(1e7 + 8, 0, -100), Body = new BodyDesc(new SphereShapeDesc(1), BodyMotion.Static) });
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(1e7, 0, -100), Body = Crate, Components = [log] });
        return (space, ball, rock, crate);
    }

    /// <summary>
    /// The ball and the static rock are anchors that move differently, so no home holds both at rest. The group that links
    /// them and the crate lives in the rock's resting frame, the ball stays in its following frame, and the rock's home
    /// keeps a ghost of the ball, with the ball's own layer, which meets the crate. The ghost hits the crate as the ball does
    /// without numeric frames.
    /// </summary>
    [Fact]
    public void AGroupThatLinksAFastPlainKinematicBodyAndAStaticRockLivesInTheRocksHomeAndKeepsAGhostOfTheBody()
    {
        var withLog = new CollisionLog();
        var withoutLog = new CollisionLog();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        using World plain = NewWorld(out _, configure: options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        (FrameHandle space, Entity ball, Entity rock, Entity crate) = BallAndRock(world, withLog);
        BallAndRock(plain, withoutLog);
        world.Tick();
        plain.Tick();
        FrameHandle home = world.PhysicsHomeOf(ball);
        world.Frames.Anchors(home, ball).Should().BeTrue("the ball lies beyond the distance of a following frame");
        FrameHandle rockHome = world.PhysicsHomeOf(rock);
        world.Frames.Numeric(rockHome).Should().Be(NumericMode.Resting, "the rock rests 10,000 km from the origin");
        world.PhysicsHomeOf(crate).Should().Be(rockHome, "the group of the ball, the rock and the crate lives in the rock's home");
        world.Frames.AnchorGhosts.ToArray().Should().Equal(new NumericGhost(ball, rockHome));
        world.Tick();
        plain.Tick();
        faults.Jolt.HasGhostIn(ball, rockHome).Should().BeTrue("the rock's home keeps a ghost of the ball");

        Tick(world, 200);
        Tick(plain, 200);

        withLog.Seen.Should().NotBeEmpty("the ghost hits the crate");
        withLog.Seen.Select(seen => (seen.Tick, seen.Other.Index)).Should().Equal(withoutLog.Seen.Select(seen => (seen.Tick, seen.Other.Index)),
            "the ghost hits the crate at the tick at which the ball does without numeric frames");
        world.Errors.Should().BeEmpty();
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
        space.IsNull.Should().BeFalse();
    }
}

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

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a root frame with a uniform gravity of
    /// 9.81 m/s² along -Y and a bubble of 100,000 km, and in it a plain kinematic ball at rest <paramref name="distance"/> m
    /// from the origin along +X. No component moves the ball, so it has no motion intent in any step.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Ball) StillBall(double distance, bool numericFrames)
    {
        World world = NewWorld(out _, configure: numericFrames ? null : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e8f), GravityField.Uniform(new Vector3(0, -9.81f, 0))));
        Entity ball = world.Spawn(new SpawnDesc { Frame = space, Pose = At(distance), Body = Ball() with { Motion = BodyMotion.Kinematic } });
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
            world.Frames.Anchors(home, ball).Should().Be(distance >= FollowDistance, "only a far kinematic body anchors a following frame");

            for (int tick = 0; tick < 600; tick++)
            {
                world.Tick();
                world.PoseIn(ball, space).Should().Be(At(distance), "the ball holds still at tick {0}", world.TickNumber);
                world.VelocityIn(ball, space).Should().Be(Double3.Zero, "the ball has no velocity at tick {0}", world.TickNumber);
            }
            world.PhysicsHomeOf(ball).Should().Be(home, "the far ball keeps its following frame by its distance");
            world.Errors.Should().BeEmpty();
        }
    }

    // ---- A Kepler hull whose component skips a tick ---------------------------------------------------------------

    private const double Mu = Scenes.AsteroidMu;

    /// <summary>
    /// A fast orbit of 5 km at 2 km/s round the point gravity of an <see cref="Scenes.AsteroidSpace"/>, whose pull there is
    /// 800 m/s²: a hull on it lives in a following frame of its own. The frame turns at 0.2 rad/s.
    /// </summary>
    private static readonly KeplerOrbit Fast = KeplerOrbit.Circular(5_000, phase: 0.3, spinRate: 0.2);

    /// <summary>The tick whose sample <see cref="SkippingKepler"/> does not write.</summary>
    private const long SkippedTick = 60;

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
    private static (World World, FrameHandle Space, Entity Owner) SkippingHull(bool numericFrames)
    {
        World world = NewWorld(out _, configure: numericFrames ? null : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Scenes.AsteroidSpace(world);
        double time = world.Time;
        (Double3 position, Double3 velocity) = Fast.StateAt(time, Mu);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = new DPose(position, Fast.RotationAt(time, Mu)),
            LinearVelocity = velocity,
            AngularVelocity = Fast.AngularVelocityAt(time, Mu).ToVector3(),
            FirstMove = new MotionIntent(Fast.Sample(Mu, world.TimeAt(world.TickNumber + 1), world.DeltaTime)),
            Body = Ball() with { Motion = BodyMotion.Kinematic },
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.01f)),
            Components = [new SkippingKepler(Fast)],
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
    public void AKinematicHullOnAKeplerOrbitWhoseComponentSkipsATickHoldsStillForThatStepAndIsBackOnItsOrbitAfterItsNextSample()
    {
        (World world, FrameHandle space, Entity owner) = SkippingHull(numericFrames: true);
        using (world)
        {
            world.Tick();
            world.Frames.Anchors(world.PhysicsHomeOf(owner), owner).Should().BeTrue("the hull moves at 2 km/s, so it anchors a following frame");
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
                    world.VelocityIn(owner, space).Should().Be(Double3.Zero, "the hull has no velocity in the step without a sample");
                    world.AngularVelocityIn(owner, space).Should().Be(Vector3.Zero, "the hull does not turn in the step without a sample");
                    continue;
                }
                pose.Should().Be(RailsPose(world.Time), "the hull stands on its orbit at sync point {0}", world.TickNumber);
            }
            held.Should().Be(1);
            world.Errors.Should().BeEmpty();
        }
    }

    // ---- A hand-off in the step in which the anchor stops --------------------------------------------------------

    /// <summary>The velocity of the hull's drift in space, in m/s: 2 km/s along +X.</summary>
    private static readonly Double3 Drift = new(2_000, 0, 0);

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
            self.MoveKinematic(new DPose(start.Position + Drift * self.DeltaTime, start.Rotation));
        }
    }

    /// <summary>
    /// A world on the real backend, with the default numeric frames or none, a space without gravity, and in it the owner of a
    /// frame with a bubble of 16 m, whose kinematic hull, a box of 4 m, <see cref="StoppingDrift"/> drives at
    /// <see cref="Drift"/> until <paramref name="stopTick"/>. A crate floats in the frame 15 m along +Y from the hull and
    /// drifts on along +Y at 2 m/s, so it leaves the bubble into the frame's tree parent within a second: the hull's following
    /// frame with numeric frames, and space without them.
    /// </summary>
    private static (World World, FrameHandle Space, Entity Owner, Entity Crate) LeavingCrate(bool numericFrames, long stopTick)
    {
        World world = NewWorld(out _, configure: numericFrames ? null : options => options.NumericFrames = NumericFrameTesting.WithoutNumericFrames);
        FrameHandle space = Ground(world, 0);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Kinematic, Mass: 1e5f),
            AsFrame = new FrameSpec(FrameBounds.Sphere(16)),
            LinearVelocity = Drift,
            FirstMove = new MotionIntent(new EndPose(new DPose(Drift * world.DeltaTime, DQuaternion.Identity))),
            Components = [new StoppingDrift(stopTick)],
        });
        Entity crate = world.Spawn(new SpawnDesc
        {
            Frame = world.GetOwnedFrame(owner), Pose = At(0, 15), LinearVelocity = new Double3(0, 2, 0),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.25f)), Mass: 2, Restitution: 0, LinearDamping: 0, AngularDamping: 0),
        });
        return (world, space, owner, crate);
    }

    /// <summary>
    /// The hull stops in the step in which the crate leaves its bubble, or in the step before or after. The crate then moves
    /// on in space at the drift's velocity plus its own, as it does without numeric frames, whichever step the hull stopped in.
    /// </summary>
    [Fact]
    public void ACrateThatLeavesTheBubbleOfAHullThatStopsMovesOnInSpaceAsWithoutNumericFrames()
    {
        long exitTick;
        (World probe, FrameHandle probeSpace, Entity _, Entity probeCrate) = LeavingCrate(numericFrames: true, stopTick: long.MaxValue);
        using (probe)
        {
            TickUntil(probe, () => probe.GetFrame(probeCrate) == probeSpace, 120);
            probe.GetFrame(probeCrate).Should().Be(probeSpace, "the crate leaves the bubble within two seconds");
            exitTick = probe.TickNumber;
        }

        for (long stopTick = exitTick - 3; stopTick <= exitTick; stopTick++)
        {
            (World world, FrameHandle space, Entity owner, Entity crate) = LeavingCrate(numericFrames: true, stopTick);
            (World plain, FrameHandle plainSpace, Entity plainOwner, Entity plainCrate) = LeavingCrate(numericFrames: false, stopTick);
            using (world)
            using (plain)
            {
                Tick(world, (int)exitTick + 30);
                Tick(plain, (int)exitTick + 30);
                (world.PoseIn(owner, space).Position - plain.PoseIn(plainOwner, plainSpace).Position).Length
                    .Should().BeLessThan(1e-6, "the hull stopped where it does without numeric frames, stop tick {0}", stopTick);
                (world.PoseIn(crate, space).Position - plain.PoseIn(plainCrate, plainSpace).Position).Length
                    .Should().BeLessThan(1e-3, "the crate is where it is without numeric frames, stop tick {0}", stopTick);
                (world.VelocityIn(crate, space) - plain.VelocityIn(plainCrate, plainSpace)).Length
                    .Should().BeLessThan(1e-3, "the crate moves as it does without numeric frames, stop tick {0}", stopTick);
                world.Errors.Should().BeEmpty();
            }
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
        (World world, FrameHandle space, Entity body) = scene == "still ball" ? StillBall(1e7, numericFrames: true) : SkippingHull(numericFrames: true);
        (World plain, FrameHandle plainSpace, Entity plainBody) = scene == "still ball" ? StillBall(1e7, numericFrames: false) : SkippingHull(numericFrames: false);
        using (world)
        using (plain)
        {
            world.Tick();
            plain.Tick();
            world.Frames.Anchors(world.PhysicsHomeOf(body), body).Should().BeTrue("the body anchors a following frame with numeric frames");
            plain.PhysicsHomeOf(plainBody).Should().Be(plainSpace, "without numeric frames the body lives in its frame's own system");
            for (int tick = 0; tick < 120; tick++)
            {
                world.Tick();
                plain.Tick();
                (world.PoseIn(body, space).Position - plain.PoseIn(plainBody, plainSpace).Position).Length
                    .Should().BeLessThan(1e-6, "the body is where it is without numeric frames at sync point {0}", world.TickNumber);
                // Without numeric frames the row keeps the hull's 2 km/s as a float, to 6.1e-5 m/s.
                (world.VelocityIn(body, space) - plain.VelocityIn(plainBody, plainSpace)).Length
                    .Should().BeLessThan(1e-4, "the body moves as it does without numeric frames at sync point {0}", world.TickNumber);
            }
            world.Errors.Should().BeEmpty();
            plain.Errors.Should().BeEmpty();
        }
    }
}

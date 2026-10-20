using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The wake of sleeping bodies on a change of a frame's turn, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "The wake" and "V8, the turn at the last wake"): a change of the frame's angular velocity or
/// angular acceleration wakes its sleepers when the change of the centrifugal and the Euler acceleration that it bounds at
/// any place of the system's bodies, added to the change of the frame's push, passes
/// <see cref="JoltBackendOptions.WakeAccelerationThreshold"/>.
/// </summary>
public class TurningFrameWakeTests
{
    private static readonly BodyDesc UndampedCrate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10);

    [Fact]
    public void SleepersWakeWhenTheStationSpinsUpAndStayAtRestInInertialSpace()
    {
        // The station holds still for 2 s, in which the crates fall asleep, and then spins up at 0.01 rad/s² about its +Y,
        // the design's example: the Euler term at once passes the threshold, at 0.5 m/s² for the crate 50 m from the axis.
        // A crate that slept on would turn with the station, and in 10 s the station turns by 0.5 rad. An awake crate stays
        // at rest in inertial space, where the update holds the Euler term, and later the centrifugal and Coriolis terms.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        const double spinUp = 0.01;
        double start = world.Time + 2;
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(150)),
            Components = [new SpinUp(spinUp, start)],
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        Double3[] places = [new(50, 0, 0), new(-20, 3, 30)];
        Entity[] crates = places.Select(place => world.Spawn(new SpawnDesc
        {
            Frame = station,
            Pose = new DPose(place, DQuaternion.Identity),
            Body = UndampedCrate,
        })).ToArray();
        while (world.Time < start - 1e-9)
            world.Tick();
        crates.Should().OnlyContain(crate => !IsAwake(backend, station, crate), "the crates rested in a station that held still");
        Double3[] inertial = crates.Select(crate => world.PoseIn(crate, space).Position).ToArray();

        world.Tick();
        crates.Should().OnlyContain(crate => IsAwake(backend, station, crate), "the first step of the spin-up woke them");
        double worst = 0;
        for (int tick = 0; tick < 10 * world.Options.TickRate; tick++)
        {
            world.Tick();
            for (int i = 0; i < crates.Length; i++)
                worst = Math.Max(worst, Double3.Distance(world.PoseIn(crates[i], space).Position, inertial[i]));
        }

        worst.Should().BeLessThan(1e-3);
        double turned = 2 * Math.Acos(Math.Min(1, Math.Abs(world.Frames.LocalPose(station).Rotation.W)));
        turned.Should().BeApproximately(0.5 * spinUp * 10 * 10, 0.02, "the station spun up under the crates");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFrameThatFacesItsPlanetOnAnOvalOrbitLetsItsSleepersSleep()
    {
        // The frame turns at about 1e-3 rad/s, faster near periapsis and slower near apoapsis, so its rate changes a little
        // in every step. With a reach of a few meters, the change of the centrifugal and the Euler acceleration stays far
        // below the threshold of 0.02 m/s², so the crates sleep. A wake on any change of rate, the spike's rule, would wake
        // them in every step.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        // A G·M of 1.25e5 m³/s² gives a mean motion of sqrt(mu / a³) = 1e-3 rad/s at 5 km.
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(1.25e5)));
        var oval = new KeplerOrbit(5000, 0.1, 0, 0, 0, 0, 0, DQuaternion.Identity, 0, 0, KeplerAttitude.FacesParent);
        Entity owner = world.Spawn(new SpawnDesc { Frame = planet, AsFrame = new FrameSpec(FrameBounds.Sphere(50)) }
            .OnKeplerOrbit(world, oval, firstMove: true));
        FrameHandle frame = world.GetOwnedFrame(owner);
        Entity[] crates = new[] { new Double3(2, 0, 0), new Double3(-1, 1.5, 0.5) }.Select(place => world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = new DPose(place, DQuaternion.Identity),
            Body = Crate,
        })).ToArray();
        // The bodies join their systems at the first sync point.
        world.Tick();

        int asleepAfter = TickUntil(world, () => crates.All(crate => !IsAwake(backend, frame, crate)), 20 * world.Options.TickRate);

        asleepAfter.Should().BeLessThan(20 * world.Options.TickRate, "nothing wakes the crates, so they fall asleep");
        int woke = 0;
        for (int tick = 0; tick < 10 * world.Options.TickRate; tick++)
        {
            world.Tick();
            if (crates.Any(crate => IsAwake(backend, frame, crate)))
                woke++;
        }
        woke.Should().Be(0, "the frame's small changes of turn do not wake its sleepers");
        world.Errors.Should().BeEmpty();
    }

    // A push and a spin-up that each change what the crate 50 m from the axis feels by 0.013 m/s², below the threshold of
    // 0.02 m/s². The reach of the system is the crate's distance plus its bounding sphere, 50.87 m.
    private const double SmallPush = 0.013;
    private const double SmallSpinUp = SmallPush / 50.87;

    [Fact]
    public void APushAndASpinUpThatTogetherChangeWhatSleepersFeelPastTheThresholdWakeThem()
    {
        // The push and the turn are two parts of one cause, a change of how the frame moves against a free body, and the
        // sleepers feel their sum. One test adds them, so the two small changes wake the crate, which neither does alone.
        WakesWithin(SmallPush, SmallSpinUp).Should().BeTrue("the crate feels 0.026 m/s² more than when it fell asleep");
        WakesWithin(SmallPush, 0).Should().BeFalse("the push alone changes what the crate feels by 0.013 m/s²");
        WakesWithin(0, SmallSpinUp).Should().BeFalse("the spin-up alone changes what the crate feels by 0.013 m/s²");
    }

    /// <summary>
    /// Whether a crate that fell asleep 50 m from the axis of a station that held still wakes in the 2 s after the station
    /// starts to speed up at <paramref name="acceleration"/> along its +X and to spin up at
    /// <paramref name="angularAcceleration"/> about its +Y.
    /// </summary>
    private static bool WakesWithin(double acceleration, double angularAcceleration)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        double start = world.Time + 2;
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            AsFrame = new FrameSpec(FrameBounds.Sphere(150)),
            Components = [new PushAndSpinUp(acceleration, angularAcceleration, start)],
        });
        FrameHandle station = world.GetOwnedFrame(owner);
        Entity crate = world.Spawn(new SpawnDesc { Frame = station, Pose = new DPose(new Double3(50, 0, 0), DQuaternion.Identity), Body = UndampedCrate });
        while (world.Time < start - 1e-9)
            world.Tick();
        IsAwake(backend, station, crate).Should().BeFalse("the crate rested in a station that held still");
        int ticks = TickUntil(world, () => IsAwake(backend, station, crate), 2 * world.Options.TickRate);
        world.Errors.Should().BeEmpty();
        return ticks < 2 * world.Options.TickRate;
    }

    /// <summary>
    /// Moves its entity from rest at its parent's origin, from <paramref name="startTime"/> on, with a constant
    /// <paramref name="acceleration"/> along the parent's +X, in m/s², and turns it about the parent's +Y with a constant
    /// <paramref name="angularAcceleration"/>, in rad/s², as <see cref="SpinUp"/> does: in each tick it writes the full sample
    /// of the step in which the tick's intents act.
    /// </summary>
    private sealed class PushAndSpinUp(double acceleration, double angularAcceleration, double startTime) : IComponent
    {
        public int Priority => -1000;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            self.MoveOnRails(SampleAt(self.TimeAt(self.Tick + 2), self.DeltaTime));

        private MotionSample SampleAt(double end, double step)
        {
            double s = Since(end), before = Since(end - step);
            Double3 velocity = new(acceleration * s, 0, 0), velocityBefore = new(acceleration * before, 0, 0);
            Double3 angularVelocity = new(0, angularAcceleration * s, 0), angularVelocityBefore = new(0, angularAcceleration * before, 0);
            var pose = new DPose(new Double3(0.5 * acceleration * s * s, 0, 0), DQuaternion.FromAxisAngle(Double3.UnitY, 0.5 * angularAcceleration * s * s));
            Double3 chord = new Double3(0.5 * acceleration * (s * s - before * before), 0, 0) / step;
            return new MotionSample(pose, chord, velocity, (velocity - velocityBefore) / step, angularVelocity,
                (angularVelocity - angularVelocityBefore) / step);
        }

        private double Since(double time) => Math.Max(0, time - startTime);
    }
}

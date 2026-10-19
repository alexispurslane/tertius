using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The wake of sleeping bodies on a change of a frame's turn, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "The wake" and "V8, the turn at the last wake"): a change of the frame's angular velocity or
/// angular acceleration wakes its sleepers when the change of the centrifugal and the Euler acceleration that it bounds at
/// any place of the system's bodies passes <see cref="JoltBackendOptions.WakeAccelerationThreshold"/>.
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
}

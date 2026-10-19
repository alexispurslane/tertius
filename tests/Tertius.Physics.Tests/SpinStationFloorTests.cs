using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Bodies that rest on the floor of a turning frame, on the World and the real Jolt backend (docs/plans/spin-gravity.md,
/// "V4, the acceleration that the frame gave a body" and "V6, the velocity shadow"). The station of the tests is a frame
/// without a body and without a field, which turns in place in a root frame without a field about its +Y at 0.31321
/// rad/s, the rate that gives 9.81 m/s² at 100 m, from its first step: its spawn carries its first motion intent, so the
/// bodies spawned at rest on its floor turn with it from their first step. Its floor is a static slab whose face toward
/// the axis lies at 100 m along the station's +X, so the effective gravity on a body at rest there points along +X.
/// </summary>
public class SpinStationFloorTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;
    private static readonly BodyDesc Crate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10);

    private sealed record Station(FrameHandle Space, FrameHandle Frame);

    private static Station SpawnStation(World world)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(150)) }
            .Spinning(world, new FrameSpin(DQuaternion.Identity, StationRate), firstMove: true));
        FrameHandle frame = world.GetOwnedFrame(owner);
        world.Spawn(new SpawnDesc
        {
            Frame = frame,
            Pose = At(FloorRadius + 0.5),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f, 10, 10)), BodyMotion.Static),
        });
        return new Station(space, frame);
    }

    [Fact]
    public void AStackOnTheFloorOfTheStationRestsThereAndFallsAsleep()
    {
        // Each crate presses on the one below with the centrifugal term, about 9.8 m/s² here, and in every step the contact
        // takes back what the update gave it. A shadow that took that contact for rounding would start the next update from
        // the free path, and its take-back would pull the crates into the floor step after step.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        Station station = SpawnStation(world);
        Entity[] crates = Enumerable.Range(0, 3).Select(i => world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 0.5 - i),
            Body = Crate,
        })).ToArray();
        Double3[] spawned = crates.Select(crate => world.PoseIn(crate, station.Frame).Position).ToArray();
        // The bodies join their systems at the first sync point.
        world.Tick();

        double worstRadial = 0, worstAcross = 0;
        int asleepAfter = TickUntil(world, () =>
        {
            for (int i = 0; i < crates.Length; i++)
            {
                Double3 offset = world.PoseIn(crates[i], station.Frame).Position - spawned[i];
                worstRadial = Math.Max(worstRadial, Math.Abs(offset.X));
                worstAcross = Math.Max(worstAcross, Math.Sqrt(offset.Y * offset.Y + offset.Z * offset.Z));
            }
            return crates.All(crate => !IsAwake(backend, station.Frame, crate));
        }, 10 * world.Options.TickRate);

        asleepAfter.Should().BeLessThan(10 * world.Options.TickRate, "the stack rests, so it falls asleep");
        worstAcross.Should().BeLessThan(1e-3, "nothing pushes the crates along the floor");
        worstRadial.Should().BeLessThan(0.05, "the crates settle on each other and on the floor by Jolt's slop at most");
        Double3[] resting = crates.Select(crate => world.PoseIn(crate, station.Frame).Position).ToArray();
        Tick(world, 5 * world.Options.TickRate);
        crates.Should().OnlyContain(crate => !IsAwake(backend, station.Frame, crate), "a station that turns steadily does not wake its sleepers");
        for (int i = 0; i < crates.Length; i++)
            world.PoseIn(crates[i], station.Frame).Position.Should().Be(resting[i]);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipOnTheFloorOfTheStationFeelsTheFloorPushItTowardTheAxisAwakeAndAsleep()
    {
        // The ship is a frame whose hull physics moves. The floor holds it against the effective gravity, ω² R along +X at
        // the distance R of its center of mass from the axis, so its accelerometer reads ω² R toward the axis, in its own
        // axes, which are the station's. The contact turn of each step, about ω³ R dt = 0.051 m/s² across the floor, is no
        // push of the floor: the floor takes it back in the same step.
        using World world = NewWorld(out JoltPhysicsBackend backend, debugChecks: false);
        Station station = SpawnStation(world);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 1),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1, 2, 2)), Mass: 1000),
            // A bubble inside the hull, so that nothing hands into it.
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.5f)),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        // A new frame reads no proper acceleration in its first step, and the floor's contact settles in the next one.
        Tick(world, 3);

        double worstAlong = 0, worstAcross = 0;
        int awake = 0, asleep = 0;
        for (int tick = 0; tick < 5 * world.Options.TickRate; tick++)
        {
            world.Tick();
            double radius = world.PoseIn(owner, station.Frame).Position.X;
            Vector3 proper = world.Frames.ProperAcceleration(ship);
            worstAlong = Math.Max(worstAlong, Math.Abs(proper.X + StationRate * StationRate * radius));
            worstAcross = Math.Max(worstAcross, Math.Sqrt(proper.Y * proper.Y + proper.Z * proper.Z));
            if (IsAwake(backend, station.Frame, owner))
                awake++;
            else
                asleep++;
        }

        worstAlong.Should().BeLessThan(2e-3, "the floor holds the ship against ω² R");
        worstAcross.Should().BeLessThan(1e-3, "the floor does not push the ship along it");
        awake.Should().BeGreaterThan(10, "the ship was awake for a while, and physics measured its push from its change of velocity");
        asleep.Should().BeGreaterThan(10, "then it fell asleep, and a sleeping hull rests against the effective gravity");
        world.Errors.Should().BeEmpty();
    }
}

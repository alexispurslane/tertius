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
    private static readonly BodyDesc UndampedCrate = Crate with { LinearDamping = 0, AngularDamping = 0 };

    private sealed record Station(FrameHandle Space, FrameHandle Frame);

    /// <summary>
    /// The station and its floor. With <paramref name="firstMove"/> false, the station's spawn carries no first motion
    /// intent, so it holds still in its first step and turns at its full rate from its second.
    /// </summary>
    private static Station SpawnStation(World world, bool firstMove = true)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new FrameSpec(FrameBounds.Sphere(150)) }
            .Spinning(world, new FrameSpin(DQuaternion.Identity, StationRate), firstMove));
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

    /// <summary>
    /// A crate on the floor whose accelerometer reads how hard the floor pushes it: a frame whose hull is a crate of 1 m
    /// and 10 kg, with a bubble inside the hull, so that nothing hands into it. Its proper acceleration times its mass is
    /// its press on the floor.
    /// </summary>
    private static SpawnDesc CrateWithAccelerometer(FrameHandle frame, Double3 place) => new()
    {
        Frame = frame,
        Pose = new DPose(place, DQuaternion.Identity),
        Body = Crate,
        AsFrame = new FrameSpec(FrameBounds.Sphere(0.1f)),
    };

    [Fact]
    public void CratesSpawnedOnTheFloorOfAStationThatCarriesItsFirstMotionIntentStayOnTheFloorFromTheFirstStepAndPressOnItWithOmegaSquaredR()
    {
        // The station's spawn carries its first motion intent, so in its first step it turns at its full rate, and its
        // first V2 is its first V1 (docs/plans/spin-gravity.md, "Frames without a last turn"). A crate spawned at rest on
        // its floor takes the velocity of the floor under it from its first step, with no spin-up: it stays where it was
        // spawned, and the floor holds it against ω² r, about 9.76 m/s² at 99.5 m from the axis.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Entity[] crates = Enumerable.Range(0, 3).Select(i => world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 0.5, 0, 3 * i - 3),
            Body = Crate,
        })).ToArray();
        var gauged = new Double3(FloorRadius - 0.5, 0, 6);
        Entity gauge = world.Spawn(CrateWithAccelerometer(station.Frame, gauged));
        FrameHandle gaugeFrame = world.GetOwnedFrame(gauge);
        Double3[] spawned = crates.Select(crate => world.PoseIn(crate, station.Frame).Position).ToArray();

        double worstPlace = 0, worstVelocity = 0;
        var presses = new List<Vector3>();
        for (int tick = 0; tick < 2 * world.Options.TickRate; tick++)
        {
            world.Tick();
            for (int i = 0; i < crates.Length; i++)
            {
                worstPlace = Math.Max(worstPlace, Double3.Distance(world.PoseIn(crates[i], station.Frame).Position, spawned[i]));
                worstVelocity = Math.Max(worstVelocity, world.VelocityIn(crates[i], station.Frame).Length);
            }
            presses.Add(world.Frames.ProperAcceleration(gaugeFrame));
        }

        worstPlace.Should().BeLessThan(1e-3, "the crates stay on the floor where they were spawned, from the first step");
        worstVelocity.Should().BeLessThan(0.02, "the crates move with the floor from the first step");
        // Physics measures the press from the crate's change of velocity, which settles in the crate's first three steps,
        // as the floor's contact of a new body does (measured: half of it after the first tick, and the contact turn of
        // the station's step, ω³ r dt / 2 = 0.026 m/s² across the floor, after the second). The floor is flat, so off its
        // middle the floor and its friction push the crate toward the axis along the line through its center: by −ω² r,
        // with r the crate's place from the axis.
        foreach ((Vector3 press, int k) in presses.Skip(3).Select((p, k) => (p, k + 4)))
            Double3.Distance((Double3)press, -StationRate * StationRate * gauged).Should().BeLessThan(2e-3,
                $"the floor pushes the crate toward the axis by ω² r after tick {k}, and read {press}");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CratesSpawnedOnTheFloorOfAStationWithoutItsFirstMotionIntentKeepTheirPlaceInInertialSpaceWhileTheFloorSlidesUnderThem()
    {
        // Without a first motion intent the station holds still in its first step, and in its second it turns at its full
        // rate against a V2 that did not turn: it spins up in one step (docs/plans/spin-gravity.md, "Frames without a last
        // turn"). The crates were at rest on a floor that held still, so they keep their place in inertial space, as on a
        // real floor that starts to turn at full rate all at once: in the station's axes the floor slides under them at
        // ω r, 31.2 m/s, and nothing presses them on it, so no friction acts. The floor is flat, and the crates sit on its
        // half that turns away from them: on its other half, a crate's outer corner lies farther from the axis than the
        // floor's face, and the turning floor sweeps into it.
        // The crates have no damping, which would slow them relative to the station's axes (docs/plans/spin-gravity.md,
        // "Jolt's damping in the shadow"), so that their expected path is their place in inertial space.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world, firstMove: false);
        Entity[] crates = Enumerable.Range(0, 3).Select(i => world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 0.5, 0, 3 * i),
            Body = UndampedCrate,
        })).ToArray();
        DPose stationAtSpawn = world.Frames.LocalPose(station.Frame);
        Double3[] inSpace = crates.Select(crate => stationAtSpawn.TransformPoint(world.PoseIn(crate, station.Frame).Position)).ToArray();
        Double3[] inStation = crates.Select(crate => world.PoseIn(crate, station.Frame).Position).ToArray();

        world.Tick();
        DQuaternion afterFirst = world.Frames.LocalPose(station.Frame).Rotation;
        double worst = 0;
        for (int tick = 1; tick < 10; tick++)
        {
            world.Tick();
            for (int i = 0; i < crates.Length; i++)
                worst = Math.Max(worst, Double3.Distance(world.PoseIn(crates[i], station.Space).Position, inSpace[i]));
        }

        afterFirst.Should().Be(stationAtSpawn.Rotation, "the station held still in its first step");
        worst.Should().BeLessThan(1e-4, "the crates keep their place in inertial space from the station's second step on");
        // Nine steps of turning at ω r = 31.2 m/s, the first of them a turn of two steps' worth, which takes the station from
        // the rotation of its spawn onto its rails, slide the floor 5.2 m under each crate.
        for (int i = 0; i < crates.Length; i++)
            Double3.Distance(world.PoseIn(crates[i], station.Frame).Position, inStation[i]).Should().BeInRange(5, 5.4, "the floor slid under the crate");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheCargoOfAShipOnTheFloorOfTheStationWeighsOmegaSquaredTimesRPlusR()
    {
        // The ship rests on the floor, and the floor pushes it toward the axis by ω² R, which its contents feel reversed as
        // their linear gravity. The ship turns with the station, so its contents also feel the station's rotation terms at
        // their place in the ship. Cargo r farther from the axis than the ship's center weighs ω² (R + r) per kilogram, as
        // it would at rest in the station there. The read is the effective gravity that physics gives a body there.
        const double r = 0.5;
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 1),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1, 2, 2)), Mass: 1000),
            // A bubble inside the hull, so that nothing hands into it.
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.9f)),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        // A new frame reads no proper acceleration in its first step, and the floor's contact settles in the next one.
        Tick(world, 3);

        double worstAlong = 0, worstAcross = 0;
        for (int tick = 0; tick < 2 * world.Options.TickRate; tick++)
        {
            world.Tick();
            double radius = world.PoseIn(owner, station.Frame).Position.X;
            Double3 weight = world.Frames.EffectiveGravity(ship, new Double3(r, 0, 0));
            worstAlong = Math.Max(worstAlong, Math.Abs(weight.X - StationRate * StationRate * (radius + r)));
            worstAcross = Math.Max(worstAcross, Math.Sqrt(weight.Y * weight.Y + weight.Z * weight.Z));
        }

        worstAlong.Should().BeLessThan(2e-3, "the cargo weighs ω² (R + r) away from the axis");
        worstAcross.Should().BeLessThan(1e-3, "the cargo weighs nothing along the floor");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The radius at which the thrusting ship hovers, in meters, 50 m short of the floor.</summary>
    private const double HoverRadius = 50;

    /// <summary>What a run of <see cref="Hover"/> measured, the worst of each over 3 s.</summary>
    private sealed record HoverRun(double FromPath, double FromRadius, double ReadAlong, double ReadAcross);

    /// <summary>
    /// A ship that hovers at <see cref="HoverRadius"/> from the axis, away from the floor, and thrusts toward the axis with
    /// ω² R = 4.91 m/s²: the push that holds a body on a circle of that radius in inertial space. The thruster's first
    /// force acts in the ship's second step, one tick after its spawn, so in its first step the ship moves on its inertial
    /// line and leaves the circle at the half step of ω² R, 0.041 m/s. From then on its expected path is the path of a body
    /// that the thrust pulls toward the axis, worked out here in the station's axes with the centrifugal and Coriolis terms,
    /// from its place and velocity after its second step. The hull has no damping, so that this path is exact. The run
    /// measures, over 3 s, how far the ship lies from that path and from its radius, and how far its accelerometer's read
    /// lies from its thrust, along it and across it.
    /// </summary>
    private static HoverRun Hover()
    {
        double thrust = StationRate * StationRate * HoverRadius;
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        var hull = new BodyDesc(new BoxShapeDesc(new Vector3(1, 2, 2)), Mass: 1000, LinearDamping: 0, AngularDamping: 0);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(HoverRadius),
            Body = hull,
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.9f)),
            Components = [new Thruster(hull.Mass) { Acceleration = (float)thrust, Direction = -Vector3.UnitX }],
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        // The ship's first step ends at the second sync point, and its second, the first with thrust, at the third.
        Tick(world, 2);
        Double3 previous = world.PoseIn(owner, station.Frame).Position;
        world.Tick();
        Double3 place = world.PoseIn(owner, station.Frame).Position;
        var omega = new Double3(0, StationRate, 0);
        double dt = world.DeltaTime;
        Double3 Acceleration(Double3 x, Double3 v) =>
            new Double3(-thrust, 0, 0) - Double3.Cross(omega, Double3.Cross(omega, x)) - 2 * Double3.Cross(omega, v);
        // The velocity at the end of the ship's second step, from the chord of that step, which lies half a step behind it.
        Double3 chord = (place - previous) / dt;
        Double3 velocity = chord + Acceleration(place, chord) * (dt / 2);

        double fromPath = 0, fromRadius = 0, readAlong = 0, readAcross = 0;
        for (int tick = 0; tick < 3 * world.Options.TickRate; tick++)
        {
            // Ten steps of RK4 per tick, in doubles.
            for (int k = 0; k < 10; k++)
            {
                double h = dt / 10;
                Double3 k1x = velocity, k1v = Acceleration(place, velocity);
                Double3 k2x = velocity + k1v * (h / 2), k2v = Acceleration(place + k1x * (h / 2), velocity + k1v * (h / 2));
                Double3 k3x = velocity + k2v * (h / 2), k3v = Acceleration(place + k2x * (h / 2), velocity + k2v * (h / 2));
                Double3 k4x = velocity + k3v * h, k4v = Acceleration(place + k3x * h, velocity + k3v * h);
                place += (k1x + 2 * k2x + 2 * k3x + k4x) * (h / 6);
                velocity += (k1v + 2 * k2v + 2 * k3v + k4v) * (h / 6);
            }
            world.Tick();
            Double3 actual = world.PoseIn(owner, station.Frame).Position;
            fromPath = Math.Max(fromPath, Double3.Distance(actual, place));
            fromRadius = Math.Max(fromRadius, Double3.Distance(actual, At(HoverRadius).Position));
            Vector3 proper = world.Frames.ProperAcceleration(ship);
            readAlong = Math.Max(readAlong, Math.Abs(proper.X + thrust));
            readAcross = Math.Max(readAcross, Math.Sqrt(proper.Y * proper.Y + proper.Z * proper.Z));
        }
        world.Errors.Should().BeEmpty();
        return new HoverRun(fromPath, fromRadius, readAlong, readAcross);
    }

    [Fact]
    public void AShipThatHoldsItsRadiusInTheStationByThrustReadsItsThrust()
    {
        // The ship's accelerometer reads its thrust, in its axes, which are the station's: the rotation terms of the update
        // are no push on the ship (docs/plans/spin-gravity.md, "V4, the acceleration that the frame gave a body").
        HoverRun run = Hover();

        run.FromRadius.Should().BeLessThan(0.25, "the thrust holds the ship near its radius, where without it the ship would fly 47 m off in 3 s");
        run.ReadAlong.Should().BeLessThan(1e-3, "the ship's accelerometer reads its thrust toward the axis");
        run.ReadAcross.Should().BeLessThan(1e-3, "the ship's accelerometer reads no push across its thrust");
    }

    [Fact]
    public void AShipThatThrustsInTheStationFollowsThePathOfItsThrust()
    {
        // Jolt adds the intent's force in the frame's axes, unturned, while the update turns the field part of the linear
        // gravity by the step's turn, R_n g dt², to keep a body in a field fixed in the frame's axes on its exact path. The
        // thrust is fixed in the frame's axes too, so the same rule keeps the ship on the path of its thrust. Unturned,
        // the cross term ω |a| dt = 0.026 m/s² pushes the ship sideways, and it leaves its path by 0.11 m in 3 s
        // (measured). With the intent's force turned by R_n in the update's chord, the ship kept to its path within 5e-5 m
        // in an experiment. The design does not say which rule an intent's force takes in a turning frame, so this test
        // fails until that is decided.
        Hover().FromPath.Should().BeLessThan(1e-3, "the ship follows the path of its thrust in the turning station");
    }
}

using System.Numerics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// Frames that physics does not move, at rest in a turning frame, on the World and the real Jolt backend
/// (docs/plans/spin-gravity.md, "The proper acceleration of a frame"): the accelerometer rule gives such a frame its
/// acceleration minus the gravity at its place, and the gravity at its place holds the parent's rotation terms. So a room at
/// rest in a spinning station is pushed toward the axis by ω² R, and its contents feel that reversed, plus the room's own
/// rotation terms, ω² (R + r) in all.
/// <para>
/// The station of the tests is a frame without a body and without a field, which turns in place in a root frame without a
/// field about its +Y at 0.31321 rad/s, the rate that gives 9.81 m/s² at 100 m, from its first step: its spawn carries its
/// first motion intent. Its floor is a static slab whose face toward the axis lies at 100 m along the station's +X, so the
/// effective gravity on a body at rest there points along +X. A room is a frame without a body, without a motion intent and
/// without a turn of its own, so its axes are the station's: a place at r along its +X lies at R + r from the station's axis.
/// </para>
/// </summary>
public class SpinStationRoomTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;
    private const double RoomRadius = 50;

    private static readonly BodyDesc UndampedCrate =
        new(new BoxShapeDesc(new(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The station's rate as a vector in its axes, which are the axes of every room in it, in rad/s.</summary>
    private static readonly Double3 Omega = new(0, StationRate, 0);

    private sealed record Station(FrameHandle Space, FrameHandle Frame);

    private static Station SpawnStation(World world)
    {
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity owner = world.Spawn(new SpawnDesc { Frame = space, AsFrame = new(FrameBounds.Sphere(150)) }
            .Spinning(world, new(DQuaternion.Identity, StationRate), firstMove: true));
        FrameHandle frame = world.GetOwnedFrame(owner);
        world.Spawn(new()
        {
            Frame = frame,
            Pose = At(FloorRadius + 0.5),
            Body = new(new BoxShapeDesc(new(0.5f, 10, 10)), BodyMotion.Static)
        });
        return new(space, frame);
    }

    /// <summary>A room at <paramref name="place"/> in <paramref name="parent"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle SpawnRoom(World world, FrameHandle parent, Double3 place, float radius) =>
        world.GetOwnedFrame(world.Spawn(new()
            { Frame = parent, Pose = new(place, DQuaternion.Identity), AsFrame = new(FrameBounds.Sphere(radius)) }));

    /// <summary>
    /// Reads, in every tick, the effective gravity that physics reads in the step beside it at <paramref name="place"/> in
    /// <paramref name="room"/>, after the World fixed the turns and the accelerations of that step.
    /// </summary>
    private sealed class GravityProbe(World world, FrameHandle room, Double3 place) : IComponent
    {
        public List<Double3> Read { get; } = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
            Read.Add(world.Frames.EffectiveGravity(room, place));
    }

    [Fact]
    public void
        AStaticRoomInTheStationGivesItsContentsOnlyItsOwnRotationTermsInItsFirstStepAndOmegaSquaredTimesRPlusRFromItsSecond()
    {
        // A new frame gives no proper acceleration in its first step, and the first step 2 after its spawn derives it from
        // its rest in the turning station: ω² R toward the axis. The room's own turns are the station's from its first step.
        const double r = 2;
        using World world = NewWorld(out _);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = SpawnRoom(world, station.Frame, new(RoomRadius, 0, 0), 30);
        var probe = new GravityProbe(world, room, new(r, 0, 0));
        world.Spawn(new() { Frame = room, Components = [probe] });

        Tick(world, 3);

        double rate2 = StationRate * StationRate;
        probe.Read.Should().HaveCount(3);
        Double3.Distance(probe.Read[0], new(rate2 * r, 0, 0)).Should()
            .BeLessThan(1e-9, $"the first step reads {probe.Read[0]}");
        foreach (Double3 read in probe.Read.Skip(1))
            Double3.Distance(read, new(rate2 * (RoomRadius + r), 0, 0)).Should()
                .BeLessThan(1e-5, $"a later step reads {read}");
        Vector3 proper = world.Frames.ProperAcceleration(room);
        proper.X.Should().BeApproximately((float)(-rate2 * RoomRadius), 1e-5f, "the room is pushed toward the axis");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFreeCrateInAStaticRoomOfTheStationKeepsItsInertialLineFromItsSecondStep()
    {
        // From its second step on, the room gives the crate the room's push toward the axis reversed, ω² R outward, and the
        // crate's update in the room's turning axes keeps it on its inertial line. The two steps that end the room's first
        // and second step fix that line in inertial space. The bound holds when the update adds that push turned by the turn
        // of the step, R_n g dt², which keeps a body in a field fixed in the frame's axes on its exact path: measured 1.2e-5 m
        // over the 90 steps. The design adds the linear gravity in the axes at the end of the step and accepts the cross term
        // ω |g| dt, which is ω³ R dt = 0.026 m/s² here, and the crate then leaves its line by 2.9 cm.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = SpawnRoom(world, station.Frame, new(RoomRadius, 0, 0), 30);
        Entity crate = world.Spawn(new() { Frame = room, Pose = At(2), Body = UndampedCrate });
        world.Tick();
        Double3 first = world.PoseIn(crate, station.Space).Position;
        world.Tick();
        Double3 second = world.PoseIn(crate, station.Space).Position;
        Double3 move = second - first;

        double worst = 0;
        for (int k = 1; k <= 90; k++)
        {
            world.Tick();
            worst = Math.Max(worst, Double3.Distance(world.PoseIn(crate, station.Space).Position, second + move * k));
        }

        world.GetFrame(crate).Should().Be(room, "the crate stayed in the room's bubble");
        Double3.Distance(world.PoseIn(crate, room).Position, At(2).Position).Should()
            .BeGreaterThan(5, "the station turned under the crate");
        worst.Should().BeLessThan(1e-4, "the crate keeps its inertial line");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Reads the place of <paramref name="crate"/> in <paramref name="space"/> after each of <paramref name="ticks"/> ticks.
    /// </summary>
    private static List<Double3> PlacesInSpace(World world, Entity crate, FrameHandle space, int ticks)
    {
        var places = new List<Double3>();
        for (int k = 0; k < ticks; k++)
        {
            world.Tick();
            places.Add(world.PoseIn(crate, space).Position);
        }

        return places;
    }

    [Fact]
    public void ACrateSpawnedInAStaticRoomOfTheStationAtRestInInertialSpaceDriftsOnlyByTheThirdOrderTermOfTheChordRule()
    {
        // The room has had its first two steps, so it gives its contents its push toward the axis reversed, g = ω² R along
        // +X. The crate's instant velocity relative to the room is −ω × (R + r), the reverse of the velocity of the
        // station's point where it is. The chord rule gives Jolt the chord behind it on its line, with half a step of g
        // taken off in the axes of the step that ended, where the chord lies: R⁻¹ g dt / 2. The rule holds the room's
        // velocity and acceleration but not its jerk ω³ R, which leaves the chord off by ω³ R dt² / 6 = 7.11e-5 m/s, and the
        // crate drifts at that speed (measured 7.15e-5 m/s). The half step of g left in the axes now is off by a further
        // ω dt |g| dt / 2 = 2.1e-4 m/s, against the jerk's term: the crate then drifts at 1.4e-4 m/s.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = SpawnRoom(world, station.Frame, new(RoomRadius, 0, 0), 30);
        Tick(world, 2);
        var place = new Double3(2, 0, 0);
        Entity crate = world.Spawn(new()
        {
            Frame = room, Pose = new(place, DQuaternion.Identity), Body = UndampedCrate,
            LinearVelocity = -Double3.Cross(Omega, place + new Double3(RoomRadius, 0, 0))
        });
        Double3 start = world.PoseIn(crate, station.Space).Position;

        List<Double3> places = PlacesInSpace(world, crate, station.Space, 90);

        double dt = 1.0 / 60;
        double drift = Double3.Distance(places[89], places[14]) / (75 * dt);
        double jerkTerm = Math.Pow(StationRate, 3) * RoomRadius * dt * dt / 6;
        world.GetFrame(crate).Should().Be(room, "the crate stayed in the room's bubble");
        drift.Should().BeApproximately(jerkTerm, 2e-6, "the crate drifts only by the chord rule's third-order term");
        Double3.Distance(places[0], start).Should().BeLessThan(2e-6, "the first step starts the crate at rest");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACrateAtRestInInertialSpaceThatAStaticRoomOfTheStationSweepsOverKeepsItsRestThroughBothHandOffs()
    {
        // The crate rests in inertial space in the station, ahead of a room 50 m from the axis, whose bubble of 5 m sweeps
        // over it at 15.7 m/s. Its hand-off into the room gives it the chord behind its instant velocity there, with half a
        // step of the room's push reversed, g = ω² R, turned back into the axes of the step that ended: left in the axes now,
        // it is off by ω dt |g| dt / 2 = 2.1e-4 m/s. While the room holds it, the chord rule's third-order term of the room's
        // jerk, ω³ R dt² / 6 = 7.1e-5 m/s, moves it (measured 7.0e-5 m/s). Its hand-off out reads its instant velocity at the
        // end of the step from its chord, plus half a step of the room's field at that moment in the axes then, which is g
        // itself: g turned on by the step's turn is off by 2.1e-4 m/s again. The read takes the jerk's term back but for the
        // room's turn of 0.24 rad in between, so the crate keeps a drift of 8.8e-6 m/s in the station.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = SpawnRoom(world, station.Frame, new(RoomRadius, 0, 0), 5);
        Tick(world, 2);
        var place = new Double3(RoomRadius, 0, -8);
        Entity crate = world.Spawn(new()
        {
            Frame = station.Frame, Pose = new(place, DQuaternion.Identity), Body = UndampedCrate,
            LinearVelocity = -Double3.Cross(Omega, place)
        });

        var frames = new List<FrameHandle>();
        var places = new List<Double3>();
        for (int k = 0; k < 150; k++)
        {
            world.Tick();
            frames.Add(world.GetFrame(crate));
            places.Add(world.PoseIn(crate, station.Space).Position);
        }

        double dt = 1.0 / 60;
        int entry = frames.IndexOf(room), exit = frames.LastIndexOf(room) + 1;
        entry.Should().BeInRange(5, 12, "the room's bubble reaches the crate after about 0.15 s");
        exit.Should().BeInRange(50, 60, "the room's bubble leaves the crate after about 0.9 s");
        frames.Skip(exit).Should().AllSatisfy(f => f.Should().Be(station.Frame), "the crate stays in the station");
        double inRoom = Double3.Distance(places[exit - 2], places[entry + 2]) / ((exit - entry - 4) * dt);
        double jerkTerm = Math.Pow(StationRate, 3) * RoomRadius * dt * dt / 6;
        inRoom.Should().BeApproximately(jerkTerm, 4e-6,
            "in the room the crate drifts only by the chord rule's third-order term");
        double afterExit = Double3.Distance(places[149], places[exit + 2]) / ((147 - exit) * dt);
        afterExit.Should()
            .BeLessThan(2e-5, "the crate rests in inertial space after both hand-offs, to the jerk's term");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ARoomAtRestInAShipOnTheStationsFloorReadsTheShipsPushOneSyncPointAfterTheShip()
    {
        // The ship is a frame whose hull physics keeps on the floor, so physics measures its proper acceleration, about ω² R
        // toward the axis. The room rests at the ship's origin, where the ship's rotation terms are zero, so the rule gives it
        // the ship's value as the step found it: the value of one sync point before.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Entity owner = world.Spawn(new()
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 1),
            Body = new(new BoxShapeDesc(new(1, 2, 2)), Mass: 1000),
            // A bubble inside the hull, so that nothing hands into it.
            AsFrame = new(FrameBounds.Sphere(0.5f))
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        FrameHandle room = SpawnRoom(world, ship, Double3.Zero, 0.1f);
        Tick(world, 3);

        var shipReads = new List<Vector3>();
        var roomReads = new List<Vector3>();
        for (int tick = 0; tick < 60; tick++)
        {
            world.Tick();
            shipReads.Add(world.Frames.ProperAcceleration(ship));
            roomReads.Add(world.Frames.ProperAcceleration(room));
        }

        for (int k = 1; k < roomReads.Count; k++)
            roomReads[k].Should().Be(shipReads[k - 1],
                $"at tick {k} the room reads the ship's value of the sync point before");
        Enumerable.Range(1, roomReads.Count - 1).Should().Contain(k => roomReads[k] != shipReads[k],
            "the ship's value changes while the floor settles");
        roomReads[^1].X.Should().BeApproximately((float)(-StationRate * StationRate * (FloorRadius - 1)), 2e-3f,
            "the floor pushes the ship toward the axis");
        world.Errors.Should().BeEmpty();
    }
}

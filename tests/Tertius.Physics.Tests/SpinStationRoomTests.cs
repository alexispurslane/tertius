using System.Numerics;
using Tertius.Frames.Rails;
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
    private static readonly BodyDesc UndampedCrate = new(new BoxShapeDesc(new Vector3(0.5f)), Mass: 10, LinearDamping: 0, AngularDamping: 0);

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

    /// <summary>A room at <paramref name="place"/> in <paramref name="parent"/>, with a bubble of <paramref name="radius"/> m.</summary>
    private static FrameHandle SpawnRoom(World world, FrameHandle parent, Double3 place, float radius) =>
        world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = parent, Pose = new DPose(place, DQuaternion.Identity), AsFrame = new FrameSpec(FrameBounds.Sphere(radius)) }));

    /// <summary>
    /// Reads, in every tick, the effective gravity that physics reads in the step beside it at <paramref name="place"/> in
    /// <paramref name="room"/>, after the World fixed the turns and the accelerations of that step.
    /// </summary>
    private sealed class GravityProbe(World world, FrameHandle room, Double3 place) : IComponent
    {
        public List<Double3> Read { get; } = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => Read.Add(world.Frames.EffectiveGravity(room, place));
    }

    [Fact]
    public void AStaticRoomInTheStationGivesItsContentsOnlyItsOwnRotationTermsInItsFirstStepAndOmegaSquaredTimesRPlusRFromItsSecond()
    {
        // A new frame gives no proper acceleration in its first step, and the first step 2 after its spawn derives it from
        // its rest in the turning station: ω² R toward the axis. The room's own turns are the station's from its first step.
        const double r = 2;
        using World world = NewWorld(out _);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = SpawnRoom(world, station.Frame, new Double3(RoomRadius, 0, 0), 30);
        var probe = new GravityProbe(world, room, new Double3(r, 0, 0));
        world.Spawn(new SpawnDesc { Frame = room, Components = [probe] });

        Tick(world, 3);

        double rate2 = StationRate * StationRate;
        probe.Read.Should().HaveCount(3);
        Double3.Distance(probe.Read[0], new Double3(rate2 * r, 0, 0)).Should().BeLessThan(1e-9, $"the first step reads {probe.Read[0]}");
        foreach (Double3 read in probe.Read.Skip(1))
            Double3.Distance(read, new Double3(rate2 * (RoomRadius + r), 0, 0)).Should().BeLessThan(1e-5, $"a later step reads {read}");
        Vector3 proper = world.Frames.ProperAcceleration(room);
        proper.X.Should().BeApproximately((float)(-rate2 * RoomRadius), 1e-5f, "the room is pushed toward the axis");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AFreeCrateInAStaticRoomOfTheStationKeepsItsInertialLineFromItsSecondStep()
    {
        // From its second step on, the room gives the crate the room's push toward the axis reversed, ω² R outward, and the
        // crate's update in the room's turning axes keeps it on its inertial line. The two steps that end the room's first
        // and second step fix that line in inertial space.
        using World world = NewWorld(out _, debugChecks: false);
        Station station = SpawnStation(world);
        Tick(world, 3);
        FrameHandle room = SpawnRoom(world, station.Frame, new Double3(RoomRadius, 0, 0), 30);
        Entity crate = world.Spawn(new SpawnDesc { Frame = room, Pose = At(2), Body = UndampedCrate });
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
        Double3.Distance(world.PoseIn(crate, room).Position, At(2).Position).Should().BeGreaterThan(5, "the station turned under the crate");
        worst.Should().BeLessThan(1e-4, "the crate keeps its inertial line");
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
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = station.Frame,
            Pose = At(FloorRadius - 1),
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1, 2, 2)), Mass: 1000),
            // A bubble inside the hull, so that nothing hands into it.
            AsFrame = new FrameSpec(FrameBounds.Sphere(0.5f)),
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
            roomReads[k].Should().Be(shipReads[k - 1], $"at tick {k} the room reads the ship's value of the sync point before");
        Enumerable.Range(1, roomReads.Count - 1).Should().Contain(k => roomReads[k] != shipReads[k], "the ship's value changes while the floor settles");
        roomReads[^1].X.Should().BeApproximately((float)(-StationRate * StationRate * (FloorRadius - 1)), 2e-3f, "the floor pushes the ship toward the axis");
        world.Errors.Should().BeEmpty();
    }
}

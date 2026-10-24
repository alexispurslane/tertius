using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The public read of the gravity that a body feels in a frame over the last finished step,
/// <see cref="FrameTree.FeltGravity"/>,
/// on the World and the real Jolt backend (docs/plans/spin-gravity.md, "V5, the effective gravity"): the frame's field at
/// the place, less the frame's proper acceleration, plus the centrifugal term −ω × (ω × r) and the Euler term −α × r of the
/// frame's inertial turn, in the frame's axes. The stations of the tests are frames without a body and without a field,
/// which turn about their parent's +Y. A rate ω about +Y gives a body at (x, y, z) the centrifugal acceleration
/// (ω² x, 0, ω² z), away from the axis, and an angular acceleration α about +Y gives a body at (r, 0, 0) the Euler
/// acceleration (0, 0, α r), against the turn.
/// </summary>
public sealed class WorldFeltGravityTests
{
    private const double StationRate = 0.31321;
    private const double FloorRadius = 100;
    private static readonly FrameSpec StationSpec = new(FrameBounds.Sphere(150));
    private static readonly Double3 Rim = new(100, 0, 0);

    private static FrameHandle Space(World world) => world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));

    /// <summary>
    /// A frame without a body that holds still in <paramref name="space"/>, turned a quarter turn about +Z, so that its +Y,
    /// the axis of a station that turns in it, is the root's −X. A read that left the inertial turn in the root's axes would
    /// put the station's ω along its own −X, and miss both terms at its rim.
    /// </summary>
    private static FrameHandle TiltedHolder(World world, FrameHandle space) =>
        world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = space,
            Pose = new(Double3.Zero, DQuaternion.FromAxisAngle(Double3.UnitZ, Math.PI / 2)),
            AsFrame = new(FrameBounds.Sphere(1000))
        }));

    [Fact]
    public void AShipAtRestOnTheFloorOfASpinningStationFeelsOmegaSquaredTimesTheDistanceFromTheAxisAwayFromIt()
    {
        // The ship is a frame whose hull rests on the station's floor, with a bubble inside the hull, so that nothing hands
        // into it. Its proper acceleration is the floor's push, ω² R toward the axis at the distance R of its origin from
        // the axis, and a place r in it adds its own centrifugal term. So a body at rest at r feels ω² (R + r) away from the
        // axis: (ω² (R + rx), 0, ω² rz) in the station's axes. The floor's push comes from physics, to about 2e-3 m/s².
        using World world = NewWorld(backend: out _, debugChecks: false);
        FrameHandle space = Space(world);
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = space, AsFrame = StationSpec }
            .Spinning(world, new(Tilt: DQuaternion.Identity, Rate: StationRate), firstMove: true)));
        world.Spawn(new()
        {
            Frame = station,
            Pose = At(FloorRadius + 0.5),
            Body = new(Shape: new BoxShapeDesc(new(0.5f, 10, 10)), Motion: BodyMotion.Static)
        });
        FrameHandle ship = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = station,
            Pose = At(FloorRadius - 1),
            Body = new(Shape: new BoxShapeDesc(new(1, 2, 2)), Mass: 1000),
            AsFrame = new(FrameBounds.Sphere(0.5f))
        }));
        var place = new Double3(-0.5, 0.6, 1.5);
        // A new frame reads no proper acceleration in its first step, and the floor's contact settles in the next one.
        Tick(world, 3);

        double worst = 0;
        for (int tick = 0; tick < 2 * world.Options.TickRate; tick++)
        {
            world.Tick();
            DPose shipInStation = world.Frames.Relative(ship, station);
            Double3 inStation = shipInStation.TransformPoint(place);
            Double3 expected =
                shipInStation.InverseTransformDirection(StationRate * StationRate *
                                                        new Double3(inStation.X, 0, inStation.Z));
            worst = Math.Max(worst, Double3.Distance(world.Frames.FeltGravity(ship, place), expected));
        }

        worst.Should().BeLessThan(2e-3, "the floor's push and the ship's own centrifugal term add to ω² (R + r)");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AtTheRimOfAStationThatSpinsUpInATiltedFrameABodyFeelsTheCentrifugalAndTheEulerTermInTheStationsAxes()
    {
        // The station spins up at 0.5 rad/s² from its spawn about the +Y of a holder that is turned a quarter turn about the
        // root's +Z. After a tick, the stored values describe the step that ended at the world's time, when the station turns
        // at α t. A body at the rim feels ω² r outward along the station's +X and α r = 50 m/s² against the turn along its +Z.
        // The station's angular velocity is stored in floats, so the centrifugal term is good to about 1e-8 m/s² here.
        const double spinUp = 0.5;
        using World world = NewWorld(backend: out _);
        FrameHandle holder = TiltedHolder(world, Space(world));
        double start = world.Time;
        FrameHandle station =
            world.GetOwnedFrame(world.Spawn(SpinUp.Spawning(new() { Frame = holder, AsFrame = StationSpec }, world,
                spinUp)));
        Tick(world, 3);

        double rate = spinUp * (world.Time - start);
        Double3.Distance(world.Frames.FeltGravity(station, Rim), new(rate * rate * 100, 0, spinUp * 100)).Should()
            .BeLessThan(1e-6);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void InAFrameThatDoesNotTurnABodyFeelsTheFieldLessTheProperAccelerationToTheBit()
    {
        // A frame without a body holds still in a root frame with a uniform gravity of 9.81 m/s², so its proper acceleration
        // is that gravity reversed, and it holds a point field of its own. Neither frame turns, so the read adds no rotation
        // term, not even a zero one.
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        FrameHandle frame = world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = ground,
            Pose = At(3, 20, -7),
            AsFrame = new(FrameBounds.Sphere(50), GravityField.Point(1234.5))
        }));
        var place = new Double3(1.1, -2.3, 0.7);
        Tick(world, 3);

        FrameTree frames = world.Frames;
        frames.ProperAcceleration(frame).Y.Should()
            .BeApproximately(9.81f, 1e-4f, "the frame holds still in the ground's field");
        frames.FeltGravity(frame, place).Should()
            .Be(frames.Gravity(frame).AtPrecise(place) - frames.ProperAcceleration(frame));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>Reads the felt gravity and the effective gravity of the step in flight at a place in a frame, in every tick.</summary>
    private sealed class GravityProbe(FrameHandle frame, Double3 place) : IComponent
    {
        public readonly List<Double3> Felt = [];
        public readonly List<Double3> InFlight = [];

        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
        {
            FrameTree frames = self.Snapshot.Frames;
            Felt.Add(frames.FeltGravity(frame, place));
            InFlight.Add(frames.EffectiveGravity(frame, place));
        }
    }

    [Fact]
    public void ATickReadsTheFeltGravityOfTheLastFinishedStepAndNotThatOfTheStepInFlight()
    {
        // The station spins up, so its centrifugal term grows in every step, by 2 ω α dt r, about 0.04 m/s² at 0.025 rad/s.
        // A component that reads the felt gravity in a tick reads what a read after the last tick read, the step that
        // ended, and not the effective gravity of the step in flight beside the tick. A read after that tick reads the
        // effective gravity that the step in flight had.
        const double spinUp = 0.5;
        using World world = NewWorld(backend: out _);
        FrameHandle space = Space(world);
        FrameHandle station =
            world.GetOwnedFrame(world.Spawn(SpinUp.Spawning(new() { Frame = space, AsFrame = StationSpec }, world,
                spinUp)));
        var probe = new GravityProbe(station, Rim);
        world.Spawn(new() { Frame = space, Components = [probe] });
        Tick(world, 3);

        var after = new List<Double3>();
        for (int tick = 0; tick < 5; tick++)
        {
            after.Add(world.Frames.FeltGravity(station, Rim));
            world.Tick();
            Double3 inTick = probe.Felt[^1], inFlight = probe.InFlight[^1];
            inTick.Should().Be(after[^1], $"tick {tick} reads the step that ended before it");
            Double3.Distance(inTick, inFlight).Should()
                .BeGreaterThan(0.01, $"tick {tick} does not read the step in flight");
            Double3.Distance(world.Frames.FeltGravity(station, Rim), inFlight).Should().BeLessThan(1e-6,
                $"after tick {tick} the read describes the step that was in flight");
        }

        world.Errors.Should().BeEmpty();
    }
}

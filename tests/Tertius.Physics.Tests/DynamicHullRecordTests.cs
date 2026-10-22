using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;
using static Tertius.Testing.NumericFrameTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The step record of a frame with a dynamic hull (docs/plans/rails-components.md, "One motion record per frame"), on the
/// World and the real backend. The record holds the frame's instant velocity at the end of the step, relative to the
/// frame's tree parent. Under a numeric parent the tree keeps a velocity relative to the parent's chord, the velocity of
/// the numeric frame over the step (see FrameTree.TakeStepRecords), so a read in the semantic parent gives the instant velocity, and
/// a hull that physics places from the tree again takes the velocity that it had. A hull that hands off in a step has its
/// record in the frame that it entered, in that frame's axes.
/// </summary>
public class DynamicHullRecordTests
{
    /// <summary>Earth's G·M, in m³/s², as in the demo scenes.</summary>
    private const double EarthMu = 3.986004418e14;

    /// <summary>The radius of the ship's orbit in the demo scenes, 400 km above Earth's mean radius, in meters.</summary>
    private const double OrbitRadius = 6.371e6 + 400e3;

    private static readonly double OrbitSpeed = Math.Sqrt(EarthMu / OrbitRadius);

    /// <summary>A hull of 10 m and 10 t that nothing slows down.</summary>
    private static readonly BodyDesc Hull = new(new BoxShapeDesc(new(5)), Mass: 1e4f, LinearDamping: 0,
        AngularDamping: 0);

    /// <summary>The gravity of the station in its own axes, in m/s², which acts on what is inside it.</summary>
    private static readonly Vector3 StationGravity = new(0, -3, 0);

    /// <summary>How far the station is turned relative to the frame that it falls in: 0.9 rad about Z.</summary>
    private static readonly DQuaternion StationTurn = DQuaternion.FromAxisAngle(Double3.UnitZ, 0.9);

    /// <summary>The velocity of the ship relative to the station before it enters, in the frame that both fall in, in m/s.</summary>
    private static readonly Double3 Approach = new(-60, 0, 0);

    private static FrameHandle Earth(World world) =>
        world.CreateRootFrame(new(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu)));

    private static bool InFallingFrame(World world, Entity entity) =>
        world.Frames.Numeric(world.PhysicsHomeOf(entity)) == NumericMode.Falling;

    /// <summary>
    /// A ship whose frame physics drives, at <paramref name="start"/> in <paramref name="frame"/>, with the instant velocity
    /// <paramref name="velocity"/> and the angular velocity <paramref name="spin"/>, in rad/s.
    /// </summary>
    private static Entity Ship(World world, FrameHandle frame, Double3 start, Double3 velocity, BodyDesc hull,
        float bubble,
        Vector3 spin = default) => world.Spawn(new()
    {
        Frame = frame,
        Pose = new(start, DQuaternion.Identity),
        LinearVelocity = velocity,
        AngularVelocity = spin,
        Body = hull,
        AsFrame = new(FrameBounds.Sphere(bubble))
    });

    /// <summary>A ship in low orbit, 400 km above Earth, which falls in a numeric frame of its own within its first ticks.</summary>
    private static Entity ShipInOrbit(World world, FrameHandle earth) =>
        Ship(world, earth, new(OrbitRadius, 0, 0), new(0, 0, OrbitSpeed), Hull, 20);

    /// <summary>
    /// A station of 10 m whose frame physics drives, with a bubble of 200 m and the gravity <paramref name="stationGravity"/>
    /// inside, in its own axes, at the origin of <paramref name="frame"/> and at rest there, turned by
    /// <see cref="StationTurn"/>. Nothing slows it down.
    /// </summary>
    private static FrameHandle TurnedStation(World world, FrameHandle frame, Vector3 stationGravity) =>
        world.GetOwnedFrame(world.Spawn(new()
        {
            Frame = frame,
            Pose = new(Double3.Zero, StationTurn),
            Body = new(new BoxShapeDesc(new(5)), Mass: 1e6f, LinearDamping: 0, AngularDamping: 0),
            AsFrame = new(FrameBounds.Sphere(200), GravityField.Uniform(stationGravity))
        }));

    /// <summary>The hull of a ship of 8 m by 4 m by 2 m and 1 t, with the linear damping <paramref name="damping"/> and the angular damping
    /// <paramref name="angularDamping"/>.</summary>
    private static BodyDesc ShipHull(float damping, float angularDamping) =>
        new(new BoxShapeDesc(new(2, 1, 4)), Mass: 1000, LinearDamping: damping, AngularDamping: angularDamping);

    /// <summary>
    /// A <see cref="TurnedStation"/> in <paramref name="frame"/>, and a ship with a bubble of 10 m that starts 260 m out
    /// along X and flies at it with <see cref="Approach"/>, and spins with <paramref name="shipSpin"/>, in rad/s. The ship
    /// has the linear damping <paramref name="shipDamping"/> and the angular damping <paramref name="shipAngularDamping"/>.
    /// In a frame with gravity both fall alike, so the ship flies straight at the station, and it enters the station's
    /// bubble after about 55 ticks. In a world without numeric frames both hulls stay in the system of
    /// <paramref name="frame"/> until the hand-off.
    /// </summary>
    private static (FrameHandle Station, Entity Ship, FrameHandle ShipFrame) ShipThatFliesIntoAStation(World world,
        FrameHandle frame,
        Vector3 stationGravity, float shipDamping = 0, float shipAngularDamping = 0, Vector3 shipSpin = default)
    {
        FrameHandle station = TurnedStation(world, frame, stationGravity);
        Entity ship = Ship(world, frame, new(260, 0, 0), Approach, ShipHull(shipDamping, shipAngularDamping), 10,
            shipSpin);
        return (station, ship, world.GetOwnedFrame(ship));
    }

    private static World NewWorldWithoutNumericFrames(out FaultInjectingBackend faults) =>
        NewWorldWithFaults(out faults, configure: options => options.NumericFrames = WithoutNumericFrames);

    [Fact]
    public void AShipThatFallsInANumericFrameReadsTheInstantVelocityOfItsOrbitInEarth()
    {
        using World world = NewWorld(out _);
        FrameHandle earth = Earth(world);
        var start = new Double3(OrbitRadius, 0, 0);
        var startVelocity = new Double3(0, 0, OrbitSpeed);
        Entity ship = ShipInOrbit(world, earth);

        // The orbit of the spawn's state, stepped in doubles with the scheme that Jolt uses for every body, from the chord of
        // the spawn's instant velocity. The instant velocity at the end of each step is half a step of the step's gravity
        // past the chord. The ship's numeric frame falls at Earth's field at its origin, about 8.7 m/s², so a velocity that
        // missed half a step of it would be 7 cm/s off.
        GravityField field = GravityField.Point(EarthMu);
        double dt = world.DeltaTime;
        Double3 position = start, chord = startVelocity - field.AtPrecise(start) * (dt / 2);
        for (int tick = 1; tick <= 10; tick++)
        {
            Double3 gravity = field.AtPrecise(position);
            chord += gravity * dt;
            position += chord * dt;
            world.Tick();

            Double3 instant = chord + gravity * (dt / 2);
            (world.VelocityIn(ship, earth) - instant).Length.Should().BeLessThan(1e-5,
                "the ship reads the instant velocity of its orbit in Earth after {0} ticks", tick);
        }

        InFallingFrame(world, ship).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Two ships on the same circular orbit of Earth: one with a dynamic hull from its spawn on, and one with a kinematic
    /// hull on a <see cref="KeplerMotion"/>, which rests in a following frame of its own. The engines of the second come on: in
    /// one tick its owner removes the component and makes the hull dynamic. Physics then places the hull from the tree, with
    /// the tree's velocity relative to the following frame, which falls on, and that velocity becomes Jolt's chord at the start
    /// of the next step. So the second ship coasts on the orbit of the first.
    /// </summary>
    [Fact]
    public void AShipInANumericFrameThatPhysicsPlacesFromTheTreeKeepsToTheOrbitOfAShipLeftAlone()
    {
        using World placed = NewWorld(out _);
        using World alone = NewWorld(out _);
        FrameHandle placedEarth = Earth(placed), aloneEarth = Earth(alone);
        KeplerOrbit orbit = KeplerOrbit.Circular(OrbitRadius);
        Entity placedShip = placed.Spawn(new SpawnDesc
        {
            Frame = placedEarth,
            Body = Hull with { Motion = BodyMotion.Kinematic },
            AsFrame = new(FrameBounds.Sphere(20))
        }.OnKeplerOrbit(placed, orbit));
        (Double3 start, Double3 startVelocity) = orbit.StateAt(alone.Time, EarthMu);
        Entity aloneShip = Ship(alone, aloneEarth, start, startVelocity, Hull, 20);
        Tick(placed, 3);
        Tick(alone, 3);
        placed.Frames.Numeric(placed.PhysicsHomeOf(placedShip)).Should().Be(NumericMode.Following);

        placed.SetMotionNextTick(placedShip, BodyMotion.Dynamic, remove: placed.GetComponent<KeplerMotion>(placedShip));
        Tick(placed, 60);
        Tick(alone, 60);
        InFallingFrame(placed, placedShip).Should().BeTrue();

        // A chord that half a step of the numeric frame's acceleration put off would move the ship 7 cm away in the second.
        (placed.PoseIn(placedShip, placedEarth).Position - alone.PoseIn(aloneShip, aloneEarth).Position).Length.Should()
            .BeLessThan(1e-4);
        placed.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AShipThatEntersATurnedStationFallingBesideItHasTheVelocityInTheStationThatItsFlightGives()
    {
        using World world = NewWorldWithoutNumericFrames(out _);
        FrameHandle ground = Ground(world, 9.81f);
        (FrameHandle station, Entity ship, FrameHandle shipFrame) =
            ShipThatFliesIntoAStation(world, ground, StationGravity);

        // Both fall alike in the ground, so the ship flies at the station with one velocity until the end of the step in which
        // it enters, and then it falls in the station's gravity. A hand-off that added half a step of the ground's gravity,
        // in the ground's axes, to the velocity in the station would put it 6 cm/s off in that tick.
        Double3 before = StationTurn.Conjugate().Rotate(Approach);
        double dt = world.DeltaTime;
        long entered = -1;
        for (int tick = 1; tick <= 70; tick++)
        {
            world.Tick();
            if (entered < 0 && world.Frames.Parent(shipFrame) == station)
                entered = tick;
            Double3 expected = entered < 0 ? before : before + (Double3)StationGravity * ((tick - entered) * dt);
            (world.VelocityIn(ship, station) - expected).Length.Should().BeLessThan(1e-4,
                "the ship has the velocity of its flight in the station after {0} ticks, and it entered after {1}",
                tick, entered);
        }

        entered.Should().BeGreaterThan(0).And.BeLessThan(65, "the ship entered the station with ticks to spare");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ADampedShipThatDriftsIntoATurnedStationSlowsSmoothlyThroughTheHandOff()
    {
        using World world = NewWorldWithoutNumericFrames(out _);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        const float damping = 0.1f;
        (FrameHandle station, Entity ship, FrameHandle shipFrame) =
            ShipThatFliesIntoAStation(world, space, Vector3.Zero, damping);

        // With no gravity in space or in the station, the station rests, and the damping slows the ship by 0.17 % a tick, the same in space and in the
        // station. So its velocity in the station changes smoothly, by 1.5e-4 m/s from one change to the next. A hand-off
        // that left out the damping, or added half a step of it in the axes of space, would put it 4 cm/s off in that tick.
        var velocities = new List<Double3>();
        long entered = -1;
        for (int tick = 1; tick <= 80; tick++)
        {
            world.Tick();
            if (entered < 0 && world.Frames.Parent(shipFrame) == station)
                entered = tick;
            velocities.Add(world.VelocityIn(ship, station));
        }

        entered.Should().BeGreaterThan(1).And.BeLessThan(80, "the ship entered the station with ticks to spare");
        for (int i = 1; i + 1 < velocities.Count; i++)
            (velocities[i - 1] - velocities[i] * 2 + velocities[i + 1]).Length.Should().BeLessThan(1e-3,
                "the velocity in the station changes smoothly after {0} ticks, and the ship entered after {1}", i + 1,
                entered);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheRecordOfAShipThatEntersATurnedStationHoldsTheStationsGravityInItsAxesAsItsAcceleration()
    {
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        (FrameHandle station, _, FrameHandle shipFrame) = ShipThatFliesIntoAStation(world, ground, StationGravity);

        TickUntil(world, () => world.Frames.Parent(shipFrame) == station, 70);

        // The record of the step of the hand-off is relative to the station, so its acceleration is the one that links its
        // chord, Jolt's velocity for the first step in the station, to its instant velocity: the station's gravity.
        world.Frames.Parent(shipFrame).Should().Be(station);
        MotionSample record = faults.Outputs[^1].Frames.Single(r => r.Frame == shipFrame).Motion;
        (record.Acceleration - StationGravity).Length.Should().BeLessThan(1e-6);
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheRecordOfASpinningShipThatEntersATurnedStationHoldsItsAngularAccelerationInTheStationsAxes()
    {
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        (FrameHandle station, _, FrameHandle shipFrame) = ShipThatFliesIntoAStation(world, space, Vector3.Zero,
            shipAngularDamping: 0.1f, shipSpin: new(0.5f, 0, 0));

        TickUntil(world, () => world.Frames.Parent(shipFrame) == station, 80);

        // The ship spins about X, one of its principal axes, and its angular damping slows the spin by 0.17 % a tick: an
        // angular acceleration of -0.1 times the spin, about -0.046 rad/s² along X in the axes of space when the ship
        // enters, which changes by 1e-4 rad/s² from one step to the next. The station rests in space, turned 0.9 rad about
        // Z, so in the station's axes the same angular acceleration is turned back by 0.9 rad about Z. A record of the step
        // of the hand-off that kept the axes of space would be 0.04 rad/s² off.
        world.Frames.Parent(shipFrame).Should().Be(station);
        MotionSample inSpace = faults.Outputs[^2].Frames.Single(r => r.Frame == shipFrame).Motion;
        MotionSample inStation = faults.Outputs[^1].Frames.Single(r => r.Frame == shipFrame).Motion;
        inSpace.AngularAcceleration.X.Should().BeInRange(-0.05, -0.04, "the damping slows the spin in space");
        new Double3(0, inSpace.AngularAcceleration.Y, inSpace.AngularAcceleration.Z).Length.Should().BeLessThan(1e-4,
            "the spin stays about X");
        (inStation.AngularAcceleration - StationTurn.Conjugate().Rotate(inSpace.AngularAcceleration)).Length.Should()
            .BeLessThan(1e-3,
                "the record in the station holds the angular acceleration in the station's axes");
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void TheRecordOfASpinningShipThatLeavesATurnedStationHoldsItsAngularAccelerationInTheAxesOfSpace()
    {
        using World world = NewWorldWithoutNumericFrames(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        FrameHandle station = TurnedStation(world, space, Vector3.Zero);
        FrameHandle shipFrame = world.GetOwnedFrame(Ship(world, station, new(150, 0, 0), new(60, 0, 0),
            ShipHull(0, 0.1f), 10, new(0.5f, 0, 0)));

        TickUntil(world, () => world.Frames.Parent(shipFrame) == space, 120);

        // The ship flies out of the station along the station's X and spins about that axis, and its angular damping slows
        // the spin: an angular acceleration of about -0.046 rad/s² along X in the station's axes when it leaves. In the axes
        // of space the same angular acceleration is turned by 0.9 rad about Z, as the station is. A record of the step of
        // the hand-off that kept the station's axes would be 0.04 rad/s² off.
        world.Frames.Parent(shipFrame).Should().Be(space);
        MotionSample inStation = faults.Outputs[^2].Frames.Single(r => r.Frame == shipFrame).Motion;
        MotionSample inSpace = faults.Outputs[^1].Frames.Single(r => r.Frame == shipFrame).Motion;
        inStation.AngularAcceleration.X.Should().BeInRange(-0.05, -0.04, "the damping slows the spin in the station");
        (inSpace.AngularAcceleration - StationTurn.Rotate(inStation.AngularAcceleration)).Length.Should().BeLessThan(
            1e-3,
            "the record in space holds the angular acceleration in the axes of space");
        world.Errors.Should().BeEmpty();
    }
}

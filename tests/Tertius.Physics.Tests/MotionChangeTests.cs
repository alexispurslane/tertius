using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// A change of motion is a swap of components and a change of the hull's motion (docs/plans/rails-components.md, "Changes
/// of motion"). The owner of a frame changes its hull's motion in a tick with <c>Self.Body.SetMotion</c>. The hull motion
/// that the frame tree keeps changes at the sync point of that tick, and physics gives the Jolt body the new motion type
/// at the start of the next step. These tests run the World on the real backend, and the owner writes each change with
/// <see cref="MotionChanges.SetMotionNextTick"/>.
/// </summary>
public class MotionChangeTests
{
    private const double EarthMu = Scenes.EarthMu;

    /// <summary>
    /// The G·M of the planet in <see cref="ShipAbovePlanet"/>. At 5 km, a circular orbit round it has an orbital speed of
    /// 10 m/s: √(5e5 / 5000). That is slow and close to the planet's origin, so the engine puts no numeric frame round the
    /// ship, and the hull stays in the planet's own system.
    /// </summary>
    private const double PlanetMu = 5e5;

    private static readonly KeplerOrbit ShipOrbit = KeplerOrbit.Circular(5000, phase: 1.0);

    /// <summary>A dynamic box with sides of 2 · <paramref name="half"/> m that nothing slows down on its own and that does not bounce.</summary>
    private static BodyDesc BoxBody(float half, float mass) =>
        new(new BoxShapeDesc(new Vector3(half)), Mass: mass, Restitution: 0, LinearDamping: 0, AngularDamping: 0);

    /// <summary>The Jolt motion type of a hull with <paramref name="motion"/>.</summary>
    private static MotionType MotionOf(BodyMotion motion) => motion switch
    {
        BodyMotion.Dynamic => MotionType.Dynamic,
        BodyMotion.Kinematic => MotionType.Kinematic,
        _ => MotionType.Static,
    };

    /// <summary>
    /// What Jolt holds for the hull of the frame that <paramref name="owner"/> owns, in the system of the owner's physics
    /// home: the frame's parent, or a numeric frame that the engine made below it.
    /// </summary>
    private static JoltPhysicsBackend.BodyFacts Hull(World world, JoltPhysicsBackend backend, Entity owner) =>
        backend.BodiesIn(world.PhysicsHomeOf(owner))
            .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));

    /// <summary>Every frame state that Jolt sent for <paramref name="frame"/>, from the output at index <paramref name="from"/> on.</summary>
    private static List<FrameStepRecord> FrameStatesSince(FaultInjectingBackend faults, FrameHandle frame, int from) =>
        faults.Outputs.Skip(from).SelectMany(output => output.Frames).Where(state => state.Frame == frame).ToList();

    /// <summary>Pushes its entity's body with the same force, in newtons, in every tick.</summary>
    private sealed class Thrust(Vector3 force) : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.Body.AddForce(force);
    }

    /// <summary>Destroys its entity in the first tick that it runs in. The entity dies at step 5 of that tick's sync point.</summary>
    private sealed class DestroysItself : IComponent
    {
        public int Priority => 0;

        public EventMask Wants => EventMask.Of<Tick>();

        public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) => self.Destroy();
    }

    /// <summary>
    /// A ship of 1000 kg in space, with a dynamic hull, that flies along +X at 10 m/s with no damping, after five ticks.
    /// <paramref name="components"/> run on its owner.
    /// </summary>
    private static (FrameHandle Space, Entity Owner, FrameHandle Ship) FlyingShip(World world, params IComponent[] components)
    {
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            LinearVelocity = new Double3(10, 0, 0),
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
            Components = components,
        });
        Tick(world, 5);
        return (space, owner, world.GetOwnedFrame(owner));
    }

    /// <summary>
    /// A ship of 1000 kg with a dynamic hull, 1 km above the ground in a uniform gravity of 9.81 m/s², that flies along +X at
    /// 10 m/s and falls, after five ticks.
    /// </summary>
    private static (Entity Owner, FrameHandle Ship) FallingShip(World world)
    {
        FrameHandle ground = Ground(world, 9.81f);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 1000),
            LinearVelocity = new Double3(10, 0, 0),
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        Tick(world, 5);
        return (owner, world.GetOwnedFrame(owner));
    }

    /// <summary>
    /// A ship above a planet with a point gravity, 5 km from its origin on <see cref="ShipOrbit"/>, whose hull has
    /// <paramref name="motion"/>. A dynamic ship starts at the orbital speed. A static ship, and a kinematic one, which has no
    /// rails component and so holds still, start at rest.
    /// </summary>
    private static (FrameHandle Planet, Entity Owner, FrameHandle Ship) ShipAbovePlanet(World world, BodyMotion motion)
    {
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(PlanetMu)));
        (Double3 position, Double3 velocity) = ShipOrbit.StateAt(world.Time, PlanetMu);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Pose = new DPose(position, DQuaternion.Identity),
            LinearVelocity = motion == BodyMotion.Dynamic ? velocity : Double3.Zero,
            Body = BoxBody(2, 1000) with { Motion = motion },
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        return (planet, owner, world.GetOwnedFrame(owner));
    }

    // ---- Park and unpark ------------------------------------------------------------------------------------------

    /// <summary>
    /// The engine of the ship pushes it at 1 m/s², which is the proper acceleration of its frame. The ship parks: the hull
    /// holds still where the step of the writing tick left it. The sync point of that tick changes no velocity, so the frame
    /// stops at the next sync point, where it takes no step record. It keeps its proper acceleration, so the cargo keeps its
    /// weight.
    /// </summary>
    [Fact]
    public void AShipThatParksHoldsStillWhereTheStepLeftItAndItsFrameStopsAtTheNextSyncPoint()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        var (_, owner, ship) = FlyingShip(world, new Thrust(new Vector3(1000, 0, 0)));
        world.Frames.ProperAcceleration(ship).X.Should().BeApproximately(1, 1e-3f, "the engine pushes the ship");

        world.SetMotionNextTick(owner, BodyMotion.Static);
        world.Tick();
        DPose parked = world.Frames.LocalPose(ship);
        world.Frames.LinearVelocity(ship).X.Should().BeGreaterThan(10, "the frame moves on until the next sync point");
        int reported = faults.Outputs.Count;
        Tick(world, 3);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Static);
        world.Frames.LocalPose(ship).Should().Be(parked);
        (Hull(world, faults.Jolt, owner).Position - parked.Position).Length.Should().BeLessThan(1e-9);
        world.Frames.LinearVelocity(ship).Should().Be(Double3.Zero);
        world.Frames.AngularVelocity(ship).Should().Be(Vector3.Zero);
        world.Frames.ProperAcceleration(ship).X.Should().BeApproximately(1, 1e-3f, "the frame keeps its proper acceleration");
        FrameStatesSince(faults, ship, reported).Should().BeEmpty("physics no longer moves the ship's frame");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The last change of motion in a tick wins, and a change to the motion that the hull has changes nothing, in the tree
    /// or in Jolt. So a ship that parks and unparks in one tick falls on as before.
    /// </summary>
    [Fact]
    public void AShipThatParksAndUnparksWithinOneTickFallsOnAsBefore()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var (owner, ship) = FallingShip(world);

        world.SetMotionNextTick(owner, BodyMotion.Static);
        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        Tick(world, 30);

        // 35 ticks of free fall from the spawn, which semi-implicit Euler gives to the float rounding of the velocity.
        double t = 35 * world.DeltaTime;
        world.Frames.LinearVelocity(ship).Y.Should().BeApproximately(-9.81 * t, 1e-3);
        world.Frames.LinearVelocity(ship).X.Should().BeApproximately(10, 1e-3);
        Hull(world, backend, owner).Motion.Should().Be(MotionType.Dynamic);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A change to the motion that the hull has changes nothing, as a change to the collision groups that a body has changes
    /// nothing. So a lander that sleeps on the floor sleeps on.
    /// </summary>
    [Fact]
    public void AChangeToTheMotionThatAHullHasLeavesASleepingHullAsleep()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 2),
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, ground, owner), 600);
        IsAwake(backend, ground, owner).Should().BeFalse("the lander came to rest on the floor");

        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        Tick(world, 3);

        IsAwake(backend, ground, owner).Should().BeFalse();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A ship that is parked for one step stops at the sync point after that step, and the unpark gives its hull the frame's
    /// velocity in the tree, which is zero. So it falls from rest, from where it was parked.
    /// </summary>
    [Fact]
    public void AShipThatIsParkedForOneStepFallsFromRest()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var (owner, ship) = FallingShip(world);

        world.SetMotionNextTick(owner, BodyMotion.Static);
        world.Tick();
        DPose parked = world.Frames.LocalPose(ship);
        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        world.Tick();
        world.Frames.LocalPose(ship).Should().Be(parked, "the parked ship held still for one step");
        world.Frames.LinearVelocity(ship).Should().Be(Double3.Zero);
        Tick(world, 30);

        double t = 30 * world.DeltaTime;
        world.Frames.LinearVelocity(ship).Y.Should().BeApproximately(-9.81 * t, 1e-3);
        world.Frames.LinearVelocity(ship).X.Should().BeApproximately(0, 1e-6);
        world.Frames.LocalPose(ship).Position.X.Should().BeApproximately(parked.Position.X, 1e-6);
        Hull(world, backend, owner).Motion.Should().Be(MotionType.Dynamic);
        world.Errors.Should().BeEmpty();
    }

    // ---- Static to dynamic ----------------------------------------------------------------------------------------

    [Fact]
    public void AStaticHullThatBecomesDynamicFallsWithItsFrame()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 100),
            Body = BoxBody(2, 1000) with { Motion = BodyMotion.Static },
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle lander = world.GetOwnedFrame(owner);
        Tick(world, 3);
        world.Frames.LocalPose(lander).Position.Y.Should().Be(100, "a static frame does not fall");

        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        world.Tick();
        int reported = faults.Outputs.Count;
        Tick(world, 30);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Dynamic);
        FrameStatesSince(faults, lander, reported).Should().HaveCount(30);
        // Half a second of free fall: semi-implicit Euler falls a little further than g t² / 2 = 1.226 m.
        double dt = world.DeltaTime;
        double t = 30 * dt;
        (100 - world.Frames.LocalPose(lander).Position.Y).Should().BeInRange(0.5 * 9.81 * t * t, 0.5 * 9.81 * t * (t + dt) + 1e-6);
        world.Frames.LinearVelocity(lander).Y.Should().BeApproximately(-9.81 * t, 1e-3);
        world.Frames.ProperAcceleration(lander).Length().Should().BeLessThan(1e-3f, "the lander is in free fall");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The tree takes the change at the sync point of the writing tick, and Jolt at the start of the next step, before that
    /// step moves anything: the lander falls for the whole step.
    /// </summary>
    [Fact]
    public void PhysicsChangesTheJoltMotionTypeAtTheStartOfTheStepAfterTheSyncPoint()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle ground = Ground(world, 9.81f);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = ground,
            Pose = At(0, 100),
            Body = BoxBody(2, 1000) with { Motion = BodyMotion.Static },
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle lander = world.GetOwnedFrame(owner);
        Tick(world, 3);

        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        world.Tick();

        world.Frames.HullMotion(lander).Should().Be(BodyMotion.Dynamic);
        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Static, "no step has run since the sync point");
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow("the hull's record lags the tree until the next step, by design");
        int reported = faults.Outputs.Count;

        world.Tick();

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Dynamic);
        FrameStatesSince(faults, lander, reported).Should().ContainSingle("the hull fell in that step");
        double dt = world.DeltaTime;
        (100 - world.Frames.LocalPose(lander).Position.Y).Should().BeApproximately(0.5 * 9.81 * dt * dt, 1e-6, "a fall from rest over one whole step");
        world.Errors.Should().BeEmpty();
    }

    // ---- Kepler to dynamic and back -------------------------------------------------------------------------------

    /// <summary>
    /// The sample that the step of tick <paramref name="tick"/> applied to <paramref name="frame"/>, or null when that step
    /// applied none. The component of the frame's owner wrote it during the tick before.
    /// </summary>
    private static SampleRecord? AppliedSample(FaultInjectingBackend faults, FrameHandle frame, long tick) =>
        faults.Samples.Where(record => record.Frame == frame && record.Tick == tick).Cast<SampleRecord?>().SingleOrDefault();

    /// <summary>
    /// The engines come on when the owner removes its <see cref="KeplerMotion"/> and makes its hull dynamic: the hull coasts
    /// on with the velocity of its last sample, and the orbit frame that follows it falls on. The ship goes back into orbit
    /// when the owner adds a <see cref="KeplerMotion"/> and makes its hull kinematic: the hull holds still for one step,
    /// because the new component has written no sample yet, and then every step has a sample, which physics releases as the
    /// step record of the frame.
    /// </summary>
    [Fact]
    public void AKeplerHullThatBecomesDynamicCoastsOnItsOrbitAndFollowsTheOrbitAgainWhenItBecomesKinematic()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), GravityField.Point(EarthMu)));
        // The orbit turns the frame at 0.05 rad/s, so that the rotation and the angular velocity of a sample differ from zero.
        var orbit = KeplerOrbit.Circular(7_000_000, phase: 1.0, spinRate: 0.05);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = BoxBody(10, 1000) with { Motion = BodyMotion.Kinematic },
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        }.OnKeplerOrbit(world, orbit));
        FrameHandle station = world.GetOwnedFrame(owner);
        Tick(world, 10);
        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Kinematic);
        world.Frames.Numeric(world.Frames.TreeParent(station)).Should().Be(NumericMode.Orbiting, "at 7.5 km/s the hull has an orbit frame of its own");

        // Engines on.
        world.SetMotionNextTick(owner, BodyMotion.Dynamic, remove: world.GetComponent<KeplerMotion>(owner)!);
        world.Tick();
        world.Frames.Numeric(world.Frames.TreeParent(station)).Should().Be(NumericMode.Falling, "the orbit frame falls on without its anchor's samples");
        int reported = faults.Outputs.Count;
        Tick(world, 60);

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Dynamic);
        FrameStatesSince(faults, station, reported).Should().HaveCount(60);
        faults.Samples.Where(record => record.Frame == station && record.Tick >= faults.Steps[^60].Tick).Should().BeEmpty("physics moves the frame now");
        // In free fall the hull stays on the orbit of its last sample, up to the float rounding of its velocity.
        (world.Frames.LocalPose(station).Position - orbit.StateAt(world.Time, EarthMu).Position).Length.Should().BeLessThan(0.5);

        // Into orbit.
        world.SetMotionNextTick(owner, BodyMotion.Kinematic, add: new KeplerMotion { Orbit = orbit });
        world.Tick();
        for (int i = 0; i < 10; i++)
        {
            reported = faults.Outputs.Count;
            world.Tick();

            List<FrameStepRecord> records = FrameStatesSince(faults, station, reported);
            SampleRecord? sample = AppliedSample(faults, station, faults.Steps[^1].Tick);
            (sample is null).Should().Be(i == 0, "only the first step after the change has no sample: the new component writes its first one in the tick beside it, and the next step applies it");
            if (sample is not { } applied)
            {
                records.Should().BeEmpty("a kinematic hull holds still in a step without a sample, and its frame with it");
                continue;
            }
            // The step releases the sample that it applied, and the frame stands on it at the sync point.
            MotionSample record = records.Should().ContainSingle().Subject.Motion;
            record.Pose.Rotation.Should().Be(applied.Sample.Pose.Rotation);
            record.AngularVelocity.Should().Be(applied.Sample.AngularVelocity);
            (world.Frames.LocalPose(station).Position - applied.Sample.Pose.Position).Length.Should().BeLessThan(1e-6, "the frame stands on its sample after tick {0}", i);
            (world.Frames.LinearVelocity(station) - applied.Sample.Velocity).Length.Should().BeLessThan(1e-6);
        }

        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Kinematic);
        (world.Frames.LocalPose(station).Position - orbit.StateAt(world.Time, EarthMu).Position).Length.Should().BeLessThan(1e-6);
        // At 7.5 km/s the hull rests at the origin of an orbit frame of its own (ter-dhkf rule 3), which follows the orbit.
        BodyState hull = faults.Outputs[^1].Bodies.Single(state => state.Entity == owner);
        world.Frames.Numeric(hull.Frame).Should().Be(NumericMode.Orbiting);
        world.Frames.TreeParent(station).Should().Be(hull.Frame);
        (hull.Pose.Position - world.Frames.TreeLocalPose(station).Position).Length.Should().BeLessThan(1e-3,
            "the kinematic hull is where the orbit puts the frame, at the orbit frame's origin");
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A slow Kepler hull, at 10 m/s round a planet, lives in the planet's own system, with no numeric frame round it. Its
    /// engines come on: in one tick its owner removes its <see cref="KeplerMotion"/> and makes the hull dynamic. The hull
    /// coasts on with the velocity of its last sample. It does not stop, as a body that a kinematic target moved in the step
    /// before does when no target moves it on.
    /// </summary>
    [Fact]
    public void ASlowKeplerHullThatBecomesDynamicCoastsOnWithTheVelocityOfItsLastSample()
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), GravityField.Point(PlanetMu)));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = BoxBody(2, 1000) with { Motion = BodyMotion.Kinematic },
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        }.OnKeplerOrbit(world, ShipOrbit));
        FrameHandle ship = world.GetOwnedFrame(owner);
        Tick(world, 10);
        world.Frames.TreeParent(ship).Should().Be(planet, "a slow hull near the planet's origin needs no numeric frame");

        world.SetMotionNextTick(owner, BodyMotion.Dynamic, remove: world.GetComponent<KeplerMotion>(owner));
        Tick(world, 2);

        Hull(world, backend, owner).Motion.Should().Be(MotionType.Dynamic);
        (world.Frames.LinearVelocity(ship) - ShipOrbit.StateAt(world.Time, PlanetMu).Velocity).Length.Should()
            .BeLessThan(1e-3, "the hull coasts on with the velocity of its last sample");
        Tick(world, 60);
        (world.Frames.LocalPose(ship).Position - ShipOrbit.StateAt(world.Time, PlanetMu).Position).Length.Should()
            .BeLessThan(0.01, "in free fall the hull stays on the orbit of its last sample");
        world.Errors.Should().BeEmpty();
    }

    // ---- What a change keeps --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(BodyMotion.Dynamic, BodyMotion.Static)]
    [InlineData(BodyMotion.Dynamic, BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Static, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Static, BodyMotion.Kinematic)]
    [InlineData(BodyMotion.Kinematic, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Kinematic, BodyMotion.Static)]
    public void AHullKeepsItsBodyIdAndItsSystemThroughAChangeOfMotion(BodyMotion from, BodyMotion to)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var (planet, owner, _) = ShipAbovePlanet(world, from);
        Tick(world, 5);
        BodyID before = Hull(world, backend, owner).Id;

        world.SetMotionNextTick(owner, to);
        Tick(world, 2);

        world.PhysicsHomeOf(owner).Should().Be(planet);
        Hull(world, backend, owner).Id.Should().Be(before);
        Hull(world, backend, owner).Motion.Should().Be(MotionOf(to));
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The ship's bubble overlaps the bubble of a station beside it, so the station's system holds a ghost of the hull. A
    /// ghost meets what its real body meets, and a static body meets less, so the ghost of a hull that parks stands for a
    /// static body from the step that parks the hull, and the ghost of one that unparks stands for a moving body again.
    /// </summary>
    [Fact]
    public void TheGhostOfAHullStandsForABodyOfItsNewMotion()
    {
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        FrameHandle station = world.GetOwnedFrame(world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(500),
            AsFrame = new FrameSpec(FrameBounds.Sphere(15)),
        }));
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(480),
            Body = BoxBody(2, 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        Tick(world, 2);
        faults.Jolt.HasGhostIn(owner, station).Should().BeTrue("the bubbles overlap");

        world.SetMotionNextTick(owner, BodyMotion.Static);
        Tick(world, 2);

        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow("the ghost stands for a static body now");

        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        Tick(world, 2);

        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow("the ghost stands for a moving body again");
        faults.Jolt.HasGhostIn(owner, station).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Runs a tick whose step throws before Jolt moves any body, so that Jolt holds exactly what the tick's change batch gave
    /// it: the World sends its batch first, and the step is the only call after it. The World records the failed step in its
    /// errors, and the test clears them.
    /// </summary>
    private static void ApplyThePendingChangesWithoutAStep(World world, FaultInjectingBackend faults)
    {
        faults.ThrowFromStep = new InvalidOperationException("The test skips this step on purpose.");
        world.Tick();
        world.Errors.Should().ContainSingle().Which.Should().Contain("the physics step failed");
        world.ClearErrors();
    }

    /// <summary>
    /// A growth rebuilds every body from its spec. The spec of a hull has the motion that the hull has now, and a hull whose
    /// motion changed has the motion quality and the mass of a hull that the backend creates with that motion. So the growth
    /// rebuilds the hull as it is. A step moves the hull, and its velocity with it, so the test compares the hull before and
    /// after the growth with no step between them (see <see cref="ApplyThePendingChangesWithoutAStep"/>).
    /// </summary>
    [Theory]
    [InlineData(BodyMotion.Static, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, BodyMotion.Static)]
    [InlineData(BodyMotion.Kinematic, BodyMotion.Dynamic)]
    [InlineData(BodyMotion.Dynamic, BodyMotion.Kinematic)]
    public void AHullWhoseMotionChangedComesThroughAGrowthOfItsSystemUnchanged(BodyMotion from, BodyMotion to)
    {
        var faults = new FaultInjectingBackend(new JoltPhysicsBackend(new JoltBackendOptions { MaxBodiesPerFrame = 8, WorkerThreads = 2 }));
        using var world = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DebugChecks = true, DisposePhysics = true }, faults);
        var (planet, owner, _) = ShipAbovePlanet(world, from);
        world.Tick();
        world.SetMotionNextTick(owner, to);
        world.Tick();
        world.Tick();
        JoltPhysicsBackend.BodyFacts before = Hull(world, faults.Jolt, owner);
        before.Motion.Should().Be(MotionOf(to), "the step after the change gave the hull its new motion");

        int room = (int)faults.Jolt.CapacityOf(planet) - faults.Jolt.BodiesIn(planet).Count;
        for (int i = 0; i <= room; i++)
            world.Spawn(new SpawnDesc { Frame = planet, Pose = At(3 * i, -100), Body = new BodyDesc(new BoxShapeDesc(new Vector3(0.5f)), BodyMotion.Static) });
        ApplyThePendingChangesWithoutAStep(world, faults);

        faults.Jolt.CapacityOf(planet).Should().Be(16);
        JoltPhysicsBackend.BodyFacts after = Hull(world, faults.Jolt, owner);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-12);
        after.Should().Be(before with { Position = after.Position });
    }

    /// <summary>
    /// A hull that a destroy moves to another home gets a new body there, with the motion that it has now, not the motion of
    /// its spawn description. The hangar that the hull is in destroys itself in the tick in which the hull's owner writes the
    /// change, so that step 5 of the sync point rehomes the hull after step 4 changed the motion in the tree and before
    /// physics has given the Jolt body the new motion. Or the hangar destroys itself a tick later, when no change is left in
    /// the intents and only the body that physics creates can carry the motion. Each case reads the new body before the next
    /// step, which applies the change of motion in the intents.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHullThatADestroyReHomesAfterAChangeOfMotionKeepsItsNewMotion(bool atTheChange)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity hangarOwner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(1)), BodyMotion.Static),
            AsFrame = new FrameSpec(FrameBounds.Sphere(100)),
        });
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = world.GetOwnedFrame(hangarOwner),
            Pose = At(20),
            Body = BoxBody(2, 1000) with { Motion = BodyMotion.Static },
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle ship = world.GetOwnedFrame(owner);
        world.Tick();

        world.SetMotionNextTick(owner, BodyMotion.Dynamic);
        if (!atTheChange)
            world.Tick();
        world.AddComponent(hangarOwner, new DestroysItself());
        world.Tick();

        world.IsAlive(hangarOwner).Should().BeFalse();
        world.Frames.Parent(ship).Should().Be(space);
        world.Frames.HullMotion(ship).Should().Be(BodyMotion.Dynamic);
        Hull(world, backend, owner).Motion.Should().Be(MotionType.Dynamic, "the description says Static, but the hull is dynamic now");
        world.Tick();
        Hull(world, backend, owner).Motion.Should().Be(MotionType.Dynamic);
        world.Errors.Should().BeEmpty();
    }

    // ---- Bugs -----------------------------------------------------------------------------------------------------

    /// <summary>A change of motion that a hull cannot take.</summary>
    public enum ImpossibleChange
    {
        /// <summary>A mesh hull to dynamic. Jolt moves no mesh.</summary>
        MeshToDynamic,
        /// <summary>A mesh hull to kinematic.</summary>
        MeshToKinematic,
        /// <summary>A hull without mass to dynamic. Jolt needs mass for a dynamic body.</summary>
        MasslessToDynamic,
    }

    /// <summary>
    /// <c>SetMotion</c> refuses a motion that the hull cannot take, so such a change in a step's intents comes only from a
    /// bug. A test stands in for that bug: it puts the change in the intents of the next step, past the World. Physics then
    /// breaks a contract in a Debug build. A Release build skips the change: the hull keeps its motion, and physics reports
    /// the bug once in the errors of the step output, not as a warning, and reports no state for the frame.
    /// </summary>
    [Theory]
    [InlineData(ImpossibleChange.MeshToDynamic)]
    [InlineData(ImpossibleChange.MeshToKinematic)]
    [InlineData(ImpossibleChange.MasslessToDynamic)]
    public void AChangeOfMotionThatAHullCannotTakeComesOnlyFromABug(ImpossibleChange change)
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        var ramp = new MeshShapeDesc([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);
        BodyDesc desc = change == ImpossibleChange.MasslessToDynamic
            ? new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static, Mass: 0)
            : new BodyDesc(ramp, BodyMotion.Static);
        Entity owner = world.Spawn(new SpawnDesc
        {
            Frame = space,
            Pose = At(50),
            Body = desc,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10)),
        });
        FrameHandle dock = world.GetOwnedFrame(owner);
        world.Tick();
        int reported = faults.Outputs.Count;
        BodyMotion motion = change == ImpossibleChange.MeshToKinematic ? BodyMotion.Kinematic : BodyMotion.Dynamic;
        faults.EditNextIntents = intents => intents.Add((owner, new PhysicsIntent { Frame = space, Motion = motion, Flags = IntentFlags.SetMotion }));
        Action ticks = () => Tick(world, 3);

#if TERTIUS_CONTRACTS
        ticks.Should().Throw<ContractException>().WithMessage($"*{owner}*Only a bug*");
#else
        ticks.Should().NotThrow();
        world.Errors.Should().ContainSingle().Which.Should().Contain(owner.ToString()).And.Contain("Only a bug");
        warnings.Should().BeEmpty();
        FrameStatesSince(faults, dock, reported).Should().BeEmpty();
        world.Frames.HullMotion(dock).Should().Be(BodyMotion.Static);
        faults.Jolt.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
#endif
        Hull(world, faults.Jolt, owner).Motion.Should().Be(MotionType.Static);
    }

    /// <summary>
    /// Only a hull can change its motion, so a change of motion for a plain body in a step's intents comes only from a bug.
    /// A Debug build breaks a contract. A Release build skips the change: the body keeps its motion, and physics reports the
    /// bug once in the errors of the step output, not as a warning.
    /// </summary>
    [Fact]
    public void AChangeOfMotionForABodyThatIsNoHullComesOnlyFromABug()
    {
        var warnings = new List<string>();
        using World world = NewWorldWithFaults(out FaultInjectingBackend faults, warnings);
        FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f)));
        Entity crate = world.Spawn(new SpawnDesc { Frame = space, Pose = At(5), Body = BoxBody(0.5f, 10) });
        world.Tick();
        faults.EditNextIntents = intents => intents.Add((crate, new PhysicsIntent { Frame = space, Motion = BodyMotion.Static, Flags = IntentFlags.SetMotion }));
        Action ticks = () => Tick(world, 3);

#if TERTIUS_CONTRACTS
        ticks.Should().Throw<ContractException>().WithMessage($"*{crate}*no hull*Only a bug*");
#else
        ticks.Should().NotThrow();
        world.Errors.Should().ContainSingle().Which.Should().Contain(crate.ToString()).And.Contain("no hull").And.Contain("Only a bug");
        warnings.Should().BeEmpty();
#endif
        Hull(world, faults.Jolt, crate).Motion.Should().Be(MotionType.Dynamic);
    }

#if TERTIUS_CONTRACTS
    /// <summary>
    /// The frame tree keeps a hull's motion, and the World changes it only with a change of motion, which physics applies at
    /// the start of the next step. So after that, every hull has the motion that the tree keeps. A test stands in for a bug
    /// that changes the tree past the World, and the step finds it, in a Debug build.
    /// </summary>
    [Fact]
    public void TheStepFindsAHullWhoseMotionDiffersFromTheMotionThatTheTreeKeeps()
    {
        using World world = NewWorld(out _);
        var (_, owner, ship) = FlyingShip(world);

        world.Frames.SetHullMotion(ship, BodyMotion.Static);
        Action tick = () => world.Tick();

        tick.Should().Throw<ContractException>().WithMessage($"*{owner}*");
    }
#endif
}

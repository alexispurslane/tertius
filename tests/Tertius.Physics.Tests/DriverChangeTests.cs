using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics.Tests;

/// <summary>
/// A game can change a frame's motion driver at any time between ticks, for example to park a ship. Physics follows the
/// frame tree, so at the start of its next step it gives the frame's hull the motion that the new driver needs: dynamic
/// for the physics driver, kinematic for Kepler, and static for the static driver.
/// </summary>
public class DriverChangeTests
{
    private const double EarthMu = 3.986004418e14;

    /// <summary>What Jolt holds for the hull of the frame that <paramref name="owner"/> owns, in the system of <paramref name="parent"/>.</summary>
    private static JoltPhysicsBackend.BodyFacts Hull(PhysicsHarness h, FrameHandle parent, Entity owner) =>
        h.Backend.BodiesIn(parent).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));

    /// <summary>Collects the frame states that physics reports for <paramref name="frame"/> from now on.</summary>
    private static List<FrameState> RecordFrameStates(PhysicsHarness h, FrameHandle frame)
    {
        var states = new List<FrameState>();
        h.AfterStep = output => states.AddRange(output.Frames.Where(state => state.Frame == frame));
        return states;
    }

    /// <summary>A ship in space, with the physics driver, that flies along +X at 10 m/s with no damping.</summary>
    private static (FrameHandle Space, FrameHandle Ship) FlyingShip(PhysicsHarness h)
    {
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(10), MotionDriver.Physics,
            Shapes.Box(2, mass: 1000), velocity: new Vector3(10, 0, 0));
        h.Step(5);
        return (space, ship);
    }

    [Fact]
    public void AShipThatTheGameParksGetsAStaticHullThatStaysWhereTheShipStopped()
    {
        using var h = new PhysicsHarness();
        var (space, ship) = FlyingShip(h);
        List<FrameState> states = RecordFrameStates(h, ship);
        DPose parked = h.Frames.LocalPose(ship);

        h.Frames.SetDriver(ship, MotionDriver.Static);
        h.Step(3);

        Hull(h, space, ship.Entity).Motion.Should().Be(MotionType.Static);
        h.Frames.LocalPose(ship).Should().Be(parked);
        (Hull(h, space, ship.Entity).Position - parked.Position).Length.Should().BeLessThan(1e-9);
        states.Should().BeEmpty("physics no longer drives the ship's frame");
    }

    /// <summary>
    /// The reviewer's probe D1. The game parks a ship that flies at 10 m/s, which sets the frame's velocities in the tree to
    /// zero, and then gives it the physics driver again, before or after one step. Physics puts the hull at the frame's pose
    /// and velocity in the tree after every driver call, also when the driver ends where it started. So the ship stays.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShipThatIsParkedAndUnparkedStaysWhereItWasParked(bool stepBetween)
    {
        using var h = new PhysicsHarness();
        var (space, ship) = FlyingShip(h);
        DPose parked = h.Frames.LocalPose(ship);

        h.Frames.SetDriver(ship, MotionDriver.Static);
        if (stepBetween)
            h.Step();
        h.Frames.SetDriver(ship, MotionDriver.Physics);
        h.Step(60);

        (h.Frames.LocalPose(ship).Position - parked.Position).Length.Should().BeLessThan(1e-6, "the parked ship had no velocity left");
        h.Frames.LinearVelocity(ship).Length.Should().BeLessThan(1e-6);
        Hull(h, space, ship.Entity).Motion.Should().Be(MotionType.Dynamic);
    }

    [Fact]
    public void AStaticFrameThatGetsThePhysicsDriverFallsWithItsHull()
    {
        using var h = new PhysicsHarness();
        FrameHandle ground = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static,
            GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        FrameHandle lander = h.AddFrameWithBody(ground, Shapes.At(0, 100, 0), FrameBounds.Sphere(10), MotionDriver.Static,
            Shapes.Box(2, mass: 1000));
        h.Step(3);
        h.Frames.LocalPose(lander).Position.Y.Should().Be(100, "a static frame does not fall");
        List<FrameState> states = RecordFrameStates(h, lander);

        h.Frames.SetDriver(lander, MotionDriver.Physics);
        h.Step(30);

        Hull(h, ground, lander.Entity).Motion.Should().Be(MotionType.Dynamic);
        states.Should().HaveCount(30);
        // Half a second of free fall: semi-implicit Euler falls a little further than g t² / 2 = 1.226 m.
        double t = 30 * PhysicsHarness.Dt;
        (100 - h.Frames.LocalPose(lander).Position.Y).Should().BeInRange(0.5 * 9.81 * t * t, 0.5 * 9.81 * t * (t + PhysicsHarness.Dt) + 1e-6);
        h.Frames.LinearVelocity(lander).Y.Should().BeApproximately(-9.81 * t, 1e-3);
        h.Frames.ProperAcceleration(lander).Length().Should().BeLessThan(1e-3f, "the lander is in free fall");
    }

    [Fact]
    public void AKeplerFrameThatGetsThePhysicsDriverCoastsOnItsOrbitAndFollowsTheOrbitAgainWithTheKeplerDriver()
    {
        using var h = new PhysicsHarness();
        FrameHandle planet = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e9f), MotionDriver.Static, GravityField.Point(EarthMu));
        var orbit = KeplerOrbit.Circular(7_000_000, EarthMu, phase: 1.0);
        FrameHandle station = h.AddFrameWithBody(planet, DPose.Identity, FrameBounds.Sphere(100), MotionDriver.Kepler,
            new BodyDesc(new BoxShapeDesc(new Vector3(10)), Mass: 1000, LinearDamping: 0, AngularDamping: 0), orbit: orbit);
        h.Step(10);
        Hull(h, planet, station.Entity).Motion.Should().Be(MotionType.Kinematic);
        List<FrameState> states = RecordFrameStates(h, station);

        h.Frames.SetDriver(station, MotionDriver.Physics);
        h.Step(60);

        Hull(h, planet, station.Entity).Motion.Should().Be(MotionType.Dynamic);
        states.Should().HaveCount(60);
        // In free fall the hull stays on the orbit that the Kepler driver gave it, up to the float rounding of its velocity.
        (h.Frames.LocalPose(station).Position - orbit.StateAt(h.Time).Position).Length.Should().BeLessThan(0.5);

        states.Clear();
        h.Frames.SetDriver(station, MotionDriver.Kepler);
        h.Step(10);

        Hull(h, planet, station.Entity).Motion.Should().Be(MotionType.Kinematic);
        states.Should().BeEmpty("the Kepler driver moves the frame again");
        (h.Frames.LocalPose(station).Position - orbit.StateAt(h.Time).Position).Length.Should().BeLessThan(1e-6);
        (h[station.Entity].Pose.Position - h.Frames.LocalPose(station).Position).Length.Should().BeLessThan(1e-3,
            "the kinematic hull is where the orbit puts the frame");
    }

    [Theory]
    [InlineData(MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler)]
    public void AHullKeepsItsBodyIdWhenItsFramesDriverChanges(MotionDriver driver)
    {
        using var h = new PhysicsHarness();
        var (space, ship) = FlyingShip(h);
        BodyID before = Hull(h, space, ship.Entity).Id;

        h.Frames.SetDriver(ship, driver);
        h.Step();

        Hull(h, space, ship.Entity).Id.Should().Be(before);
    }

    /// <summary>
    /// A growth rebuilds every body from its spec. The spec of a hull has the motion of its frame's current driver, and a
    /// hull whose motion changed has the motion quality and the mass of a hull that the backend creates with that motion.
    /// So the growth rebuilds the hull as it is.
    /// </summary>
    [Theory]
    [InlineData(MotionDriver.Static, MotionDriver.Physics)]
    [InlineData(MotionDriver.Physics, MotionDriver.Static)]
    [InlineData(MotionDriver.Kepler, MotionDriver.Physics)]
    [InlineData(MotionDriver.Physics, MotionDriver.Kepler)]
    public void AHullWhoseFramesDriverChangedComesThroughAGrowthOfItsSystemUnchanged(MotionDriver from, MotionDriver to)
    {
        using var h = new PhysicsHarness(new JoltBackendOptions { MaxBodiesPerFrame = 8 });
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        FrameHandle ship = h.AddFrameWithBody(space, Shapes.At(0, 0, 0), FrameBounds.Sphere(10), from, Shapes.Box(2, mass: 1000));
        h.Step();
        h.Frames.SetDriver(ship, to);
        h.Reconcile();
        JoltPhysicsBackend.BodyFacts before = Hull(h, space, ship.Entity);

        int room = (int)h.Backend.CapacityOf(space) - h.Backend.BodiesIn(space).Count;
        for (int i = 0; i <= room; i++)
            h.AddBody(space, Shapes.Box(0.5f, motion: BodyMotion.Static), Shapes.At(3 * i, -100, 0));
        h.Sync();

        h.Backend.CapacityOf(space).Should().Be(16);
        JoltPhysicsBackend.BodyFacts after = Hull(h, space, ship.Entity);
        (after.Position - before.Position).Length.Should().BeLessThan(1e-12);
        after.Should().Be(before with { Position = after.Position });
    }

    [Fact]
    public void TheInvariantsFindAHullWhoseMotionDoesNotMatchItsFramesDriverUntilTheNextReconcile()
    {
        using var h = new PhysicsHarness();
        var (_, ship) = FlyingShip(h);
        h.Frames.SetDriver(ship, MotionDriver.Static);

        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().Throw<ContractException>("the hull is still dynamic");

        h.Reconcile();

        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }

    /// <summary>A hull that Jolt cannot move in the way that a driver needs.</summary>
    public enum UnmovableHull
    {
        /// <summary>A mesh hull and the physics driver. Jolt moves no mesh.</summary>
        MeshUnderPhysics,
        /// <summary>A mesh hull and the Kepler driver.</summary>
        MeshUnderKepler,
        /// <summary>A hull without mass and the physics driver. Jolt needs mass for a dynamic body.</summary>
        MasslessUnderPhysics,
    }

    [Theory]
    [InlineData(UnmovableHull.MeshUnderPhysics)]
    [InlineData(UnmovableHull.MeshUnderKepler)]
    [InlineData(UnmovableHull.MasslessUnderPhysics)]
    public void AHullThatCannotMoveTheWayThatItsNewDriverNeedsIsStaticWithAWarning(UnmovableHull hull)
    {
        using var h = new PhysicsHarness();
        FrameHandle space = h.AddFrame(FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1e7f), MotionDriver.Static);
        var ramp = new MeshShapeDesc([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);
        BodyDesc desc = hull == UnmovableHull.MasslessUnderPhysics
            ? new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static, Mass: 0)
            : new BodyDesc(ramp, BodyMotion.Static);
        FrameHandle dock = h.AddFrameWithBody(space, Shapes.At(50, 0, 0), FrameBounds.Sphere(10), MotionDriver.Static, desc);
        h.Step();
        List<FrameState> states = RecordFrameStates(h, dock);

        h.Frames.SetDriver(dock, hull == UnmovableHull.MeshUnderKepler ? MotionDriver.Kepler : MotionDriver.Physics);
        h.Step(3);

        Hull(h, space, dock.Entity).Motion.Should().Be(MotionType.Static);
        h.Warnings.Should().ContainSingle().Which.Should().Contain(dock.Entity.ToString()).And.Contain("hull is static");
        states.Should().BeEmpty();
        h.Backend.Invoking(backend => backend.AssertInvariants()).Should().NotThrow();
    }
}

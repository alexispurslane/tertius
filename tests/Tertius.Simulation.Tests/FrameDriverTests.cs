using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// A game gives a frame a new motion driver between ticks with <see cref="World.SetFrameDriver"/>, for example to park a
/// ship. The owner of the frame mirrors the change at once, so the world's invariants hold. The world refuses a driver
/// that the frame cannot follow, and the frame then keeps its driver.
/// </summary>
public class FrameDriverTests
{
    private static readonly Double3 ShipVelocity = new(10, 0, 0);
    private static readonly Vector3 ShipSpin = new(0, 0.5f, 0);

    /// <summary>A triangle mesh. Jolt never moves a mesh, so a hull of this shape can only be static.</summary>
    private static readonly MeshShapeDesc Ramp = new([new(-5, 0, -5), new(5, 0, -5), new(5, 2, 5), new(-5, 2, 5)], [0, 2, 1, 0, 3, 2]);

    /// <summary>A frame with <paramref name="driver"/>, 100 m along +X in the root, whose owner has the hull <paramref name="hull"/>.</summary>
    private static FrameHandle SpawnFrame(TestWorld test, MotionDriver driver, BodyDesc? hull, KeplerOrbit orbit = default) =>
        test.World.GetOwnedFrame(test.World.Spawn(new SpawnDesc
        {
            Frame = test.Root,
            Pose = TestWorld.At(100),
            LinearVelocity = ShipVelocity.ToVector3(),
            AngularVelocity = ShipSpin,
            AsFrame = new FrameSpec(FrameBounds.Sphere(10), driver, Orbit: orbit),
            Body = hull,
        }));

    /// <summary>A ship with the physics driver and a hull of 1000 kg, which flies at <see cref="ShipVelocity"/> and spins at <see cref="ShipSpin"/>.</summary>
    private static FrameHandle SpawnShip(TestWorld test) =>
        SpawnFrame(test, MotionDriver.Physics, new BodyDesc(new BoxShapeDesc(new Vector3(2)), Mass: 1000));

    // ---- A driver that the frame can follow ---------------------------------------------------------------------

    /// <summary>The reviewer's probe W6: the owner of a ship that the game parks must not keep the ship's old velocity.</summary>
    [Fact]
    public void AShipThatTheGameParksStopsAndItsOwnerStopsWithItAtOnce()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnShip(test);
        test.Run(1);

        test.World.SetFrameDriver(ship, MotionDriver.Static);

        test.World.Frames.Driver(ship).Should().Be(MotionDriver.Static);
        test.World.GetVelocity(ship.Entity).Should().Be(Vector3.Zero);
        test.World.GetAngularVelocity(ship.Entity).Should().Be(Vector3.Zero);
        test.World.GetPose(ship.Entity).Should().Be(test.World.Frames.LocalPose(ship));
        test.World.Invoking(world => world.AssertInvariants()).Should().NotThrow();
    }

    [Fact]
    public void AFrameWithAValidOrbitTakesTheKeplerDriverAndFollowsItsOrbitFromTheNextTick()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnShip(test);
        test.World.Frames.Orbit(ship) = KeplerOrbit.Circular(1000, 1e9);

        test.World.SetFrameDriver(ship, MotionDriver.Kepler);
        test.Run(1);

        test.World.Frames.Driver(ship).Should().Be(MotionDriver.Kepler);
        PoseChecks.AssertNear(test.World.Frames.LocalPose(ship).Position, KeplerOrbit.Circular(1000, 1e9).StateAt(test.World.Time).Position);
    }

    // ---- Refusals -----------------------------------------------------------------------------------------------

    [Fact]
    public void ADriverChangeDuringATickIsRefused()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnShip(test);
        InvalidOperationException? caught = null;
        test.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            try
            {
                test.World.SetFrameDriver(ship, MotionDriver.Static);
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }
        }));

        test.Run(1);

        caught.Should().NotBeNull("physics reads the frame tree on its own thread during the tick");
        caught!.Message.Should().Contain("World.SetFrameDriver");
        test.World.Frames.Driver(ship).Should().Be(MotionDriver.Physics);
    }

    [Fact]
    public void ADriverThatMotionDriverDoesNotDefineIsRefused()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnShip(test);

        Action set = () => test.World.SetFrameDriver(ship, (MotionDriver)7);

        set.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("driver");
        test.World.Frames.Driver(ship).Should().Be(MotionDriver.Physics);
    }

    [Fact]
    public void ADriverForAFrameThatIsGoneIsRefused()
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnShip(test);
        test.World.Destroy(ship.Entity);

        Action set = () => test.World.SetFrameDriver(ship, MotionDriver.Static);

        set.Should().Throw<ArgumentException>().WithMessage($"*{ship}*");
    }

    /// <summary>A hull that cannot be a dynamic body: it has no mass, or it is a mesh, which Jolt never moves.</summary>
    public static TheoryData<string, BodyDesc> HullsThatCannotBeDynamic => new()
    {
        { "no mass", new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static, Mass: 0) },
        { "a mesh", new BodyDesc(Ramp, BodyMotion.Static) },
        { "a mesh in a compound", new BodyDesc(new CompoundShapeDesc([new CompoundChild(Ramp, Pose.Identity)]), BodyMotion.Static) },
    };

    [Theory]
    [MemberData(nameof(HullsThatCannotBeDynamic))]
    public void AFrameWithThePhysicsDriverAndAHullThatCannotBeDynamicIsNotSpawned(string why, BodyDesc hull)
    {
        using var test = new TestWorld();
        int before = test.World.Stats.LiveEntities;

        Action spawn = () => SpawnFrame(test, MotionDriver.Physics, hull);

        spawn.Should().Throw<ArgumentException>(why).WithMessage("*Physics driver*");
        test.World.Stats.LiveEntities.Should().Be(before);
        test.World.Stats.Frames.Should().Be(1, "only the root frame exists");
    }

    [Fact]
    public void AFrameWithTheKeplerDriverAndAMeshHullIsNotSpawned()
    {
        using var test = new TestWorld();
        int before = test.World.Stats.LiveEntities;

        Action spawn = () => SpawnFrame(test, MotionDriver.Kepler, new BodyDesc(Ramp, BodyMotion.Static), KeplerOrbit.Circular(1000, 1e9));

        spawn.Should().Throw<ArgumentException>().WithMessage("*mesh*Kepler driver*");
        test.World.Stats.LiveEntities.Should().Be(before);
    }

    [Fact]
    public void AFrameWithADriverThatMotionDriverDoesNotDefineIsNotSpawned()
    {
        using var test = new TestWorld();
        int before = test.World.Stats.LiveEntities;

        Action spawn = () => SpawnFrame(test, (MotionDriver)7, hull: null);

        spawn.Should().Throw<ArgumentException>().WithMessage("*driver*");
        test.World.Stats.LiveEntities.Should().Be(before);
    }

    /// <summary>
    /// The reviewer's probe S8. A shed is a static frame whose owner has a hull without mass. Physics would keep that hull
    /// static under the physics driver, so it would report no state for the frame, and a check that trusts the driver
    /// alone would take the state of a buggy backend. The world builds that frame neither way, so the check needs no rule
    /// for it: a frame with the physics driver whose owner has a body always has a dynamic hull.
    /// </summary>
    [Fact]
    public void APhysicsFrameWhoseHullCannotBeDynamicCannotBeBuiltBySpawnOrByADriverChange()
    {
        using var test = new TestWorld();
        var massless = new BodyDesc(new BoxShapeDesc(new Vector3(2)), BodyMotion.Static, Mass: 0);
        FrameHandle shed = SpawnFrame(test, MotionDriver.Static, massless);

        Action spawn = () => SpawnFrame(test, MotionDriver.Physics, massless);
        Action change = () => test.World.SetFrameDriver(shed, MotionDriver.Physics);

        spawn.Should().Throw<ArgumentException>();
        change.Should().Throw<ArgumentException>();
        var frames = new List<FrameHandle>();
        test.World.Frames.GetAll(frames);
        frames.Should().NotContain(frame => test.World.Frames.Driver(frame) == MotionDriver.Physics);
    }

    [Theory]
    [MemberData(nameof(HullsThatCannotBeDynamic))]
    public void ThePhysicsDriverIsRefusedForAFrameWhoseHullCannotBeDynamic(string why, BodyDesc hull)
    {
        using var test = new TestWorld();
        FrameHandle dock = SpawnFrame(test, MotionDriver.Static, hull);

        Action set = () => test.World.SetFrameDriver(dock, MotionDriver.Physics);

        set.Should().Throw<ArgumentException>(why).WithMessage($"*{dock.Entity}*");
        test.World.Frames.Driver(dock).Should().Be(MotionDriver.Static);
    }

    [Fact]
    public void TheKeplerDriverIsRefusedForAFrameWhoseHullIsAMesh()
    {
        using var test = new TestWorld();
        FrameHandle dock = SpawnFrame(test, MotionDriver.Static, new BodyDesc(Ramp, BodyMotion.Static), KeplerOrbit.Circular(1000, 1e9));

        Action set = () => test.World.SetFrameDriver(dock, MotionDriver.Kepler);

        set.Should().Throw<ArgumentException>().WithMessage($"*{dock.Entity}*mesh*");
        test.World.Frames.Driver(dock).Should().Be(MotionDriver.Static);
    }

    /// <summary>An orbit that the Kepler driver cannot follow. The default orbit would leave the frame at its parent's origin.</summary>
    public static TheoryData<string, KeplerOrbit> OrbitsThatAreNotValid => new()
    {
        { "the default orbit", default },
        { "no parent mass", KeplerOrbit.Circular(1000, 0) },
        { "an open orbit", KeplerOrbit.Circular(1000, 1e9) with { Eccentricity = 1 } },
        { "a negative eccentricity", KeplerOrbit.Circular(1000, 1e9) with { Eccentricity = -0.1 } },
        { "a semi-major axis that is not a number", KeplerOrbit.Circular(double.NaN, 1e9) },
        { "an infinite spin", KeplerOrbit.Circular(1000, 1e9, spinRate: double.PositiveInfinity) },
    };

    [Theory]
    [MemberData(nameof(OrbitsThatAreNotValid))]
    public void TheKeplerDriverIsRefusedForAFrameWithoutAValidOrbit(string why, KeplerOrbit orbit)
    {
        using var test = new TestWorld();
        FrameHandle ship = SpawnShip(test);
        test.World.Frames.Orbit(ship) = orbit;

        Action set = () => test.World.SetFrameDriver(ship, MotionDriver.Kepler);

        set.Should().Throw<ArgumentException>(why).WithMessage($"*{ship}*orbit*");
        test.World.Frames.Driver(ship).Should().Be(MotionDriver.Physics);
    }
}

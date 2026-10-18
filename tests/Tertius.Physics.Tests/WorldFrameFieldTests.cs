using System.Numerics;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The orbit and the gravity field of a frame, which a game changes through the World. The World refuses a value that
/// the engine cannot use, at spawn and at a change. Before, a hyperbolic or NaN orbit made the frame, its hull and
/// everything inside it NaN, with no error.
/// </summary>
public class WorldFrameFieldTests
{
    private const double PlanetMu = 5e5;

    private static readonly KeplerOrbit StationOrbit = KeplerOrbit.Circular(5000);

    /// <summary>Orbits that the Kepler driver cannot follow, by the names that the theories use.</summary>
    private static KeplerOrbit BadOrbit(string kind) => kind switch
    {
        "hyperbolic e = 1.2" => StationOrbit with { Eccentricity = 1.2 },
        "NaN semi-major axis" => StationOrbit with { SemiMajorAxis = double.NaN },
        "infinite spin" => StationOrbit with { SpinRate = double.PositiveInfinity },
        _ => default,
    };

    /// <summary>Gravity fields that physics cannot apply, by the names that the theories use.</summary>
    private static GravityField BadGravity(string kind) => kind switch
    {
        "NaN acceleration" => GravityField.Uniform(new Vector3(0, float.NaN, 0)),
        "infinite acceleration" => GravityField.Uniform(new Vector3(float.PositiveInfinity, 0, 0)),
        "NaN region" => GravityField.Uniform(new Vector3(0, -9.81f, 0), new BoundingBox(new Vector3(float.NaN), new Vector3(1))),
        "infinite G·M" => GravityField.Point(double.PositiveInfinity),
        "negative G·M" => GravityField.Point(-1e14),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such bad gravity field."),
    };

    private static bool Finite(Double3 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);

    /// <summary>A planet frame with point gravity, a ball that flies in it, and a station with a hull on a circular orbit around it.</summary>
    private sealed class OrbitScene : IDisposable
    {
        public OrbitScene()
        {
            World = NewWorld(out JoltPhysicsBackend backend);
            Backend = backend;
            Planet = World.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(PlanetMu)));
            Ball = World.Spawn(new SpawnDesc
            {
                Frame = Planet,
                Pose = At(100),
                LinearVelocity = new Vector3(1, 0, 0),
                Body = new BodyDesc(new SphereShapeDesc(0.5f), Mass: 1, LinearDamping: 0),
            });
            Owner = World.Spawn(new SpawnDesc
            {
                Frame = Planet,
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1000),
                AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Kepler, Orbit: StationOrbit),
            }.OnKeplerOrbit(World, StationOrbit));
            Station = World.GetOwnedFrame(Owner);
        }

        public World World { get; }
        public JoltPhysicsBackend Backend { get; }
        public FrameHandle Planet { get; }
        public Entity Ball { get; }
        public Entity Owner { get; }
        public FrameHandle Station { get; }

        /// <summary>The station's hull as Jolt has it, in the planet's system.</summary>
        public JoltPhysicsBackend.BodyFacts Hull =>
            Backend.BodiesIn(Planet).Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, Owner));

        public void Dispose() => World.Dispose();
    }

    /// <summary>
    /// The reviewer's probes D10 and D10c. A station follows a valid orbit. The game tries to give it an orbit that the
    /// Kepler driver cannot follow. The World refuses, and the station, its hull and a ball nearby stay on their courses.
    /// </summary>
    [Theory]
    [InlineData("hyperbolic e = 1.2")]
    [InlineData("NaN semi-major axis")]
    [InlineData("infinite spin")]
    [InlineData("default")]
    public void SetFrameOrbitRefusesAnOrbitThatTheKeplerDriverCannotFollow(string kind)
    {
        using var scene = new OrbitScene();
        World world = scene.World;
        Tick(world, 10);

        Action set = () => world.SetFrameOrbit(scene.Station, BadOrbit(kind));

        set.Should().Throw<ArgumentException>().WithMessage("*orbit*not valid*");
        world.Frames.Orbit(scene.Station).Should().Be(StationOrbit);
        Tick(world, 30);
        world.Frames.LocalPose(scene.Station).Position.Length.Should().BeApproximately(5000, 1e-6, "the station stays on its orbit");
        Finite(scene.Hull.Position).Should().BeTrue();
        Finite(world.PoseInOwnFrame(scene.Ball).Position).Should().BeTrue();
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// A station gets a larger orbit. It moves to its place on the new orbit at once, and its owner with it. Physics puts
    /// the hull there before the next step, so the hull moves on at the orbital speed of the new orbit, √(μ / r), instead of
    /// crossing 1 km in one step.
    /// </summary>
    [Fact]
    public void ANewOrbitMovesAKeplerFrameAndItsHullAtOnce()
    {
        using var scene = new OrbitScene();
        World world = scene.World;
        Tick(world, 10);
        KeplerOrbit larger = KeplerOrbit.Circular(6000);

        world.SetFrameOrbit(scene.Station, larger);

        world.Frames.LocalPose(scene.Station).Position.Length.Should().BeApproximately(6000, 1e-6);
        world.PoseInOwnFrame(scene.Owner).Should().Be(world.Frames.LocalPose(scene.Station), "the owner mirrors its frame at once");
        world.Tick();
        double speed = Math.Sqrt(PlanetMu / 6000);
        scene.Hull.LinearVelocity.Length().Should().BeApproximately((float)speed, 0.01f, "the hull follows the new orbit");
        (scene.Hull.Position - world.Frames.LocalPose(scene.Station).Position).Length.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>The reviewer's probe D10b: spawn refuses a Kepler frame whose orbit the driver cannot follow.</summary>
    [Theory]
    [InlineData("hyperbolic e = 1.2")]
    [InlineData("NaN semi-major axis")]
    [InlineData("default")]
    public void SpawnRefusesAKeplerFrameWhoseOrbitTheDriverCannotFollow(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(PlanetMu)));
        int live = world.Stats.LiveEntities;

        Action spawn = () => world.Spawn(new SpawnDesc
        {
            Frame = planet,
            Body = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1000),
            AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Kepler, Orbit: BadOrbit(kind)),
        });

        spawn.Should().Throw<ArgumentException>().WithMessage("*orbit*not valid*");
        world.Stats.LiveEntities.Should().Be(live);
    }

    /// <summary>
    /// The World refuses every call that would leave a Kepler frame without a G·M to orbit, so only a bug can. A bug here
    /// takes the point gravity from the planet behind the World's back, as <c>FrameTree.SetGravity</c> does when something
    /// other than the World calls it. The world's debug checks find the Kepler frame at the end of the tick.
    /// </summary>
    [Fact]
    public void TheDebugChecksOfTheWorldFindAKeplerFrameWhoseParentLostItsPointGravity()
    {
        using var scene = new OrbitScene();
        scene.World.Tick();
        scene.World.Frames.SetGravity(scene.Planet, GravityField.None);

        Action tick = () => scene.World.Tick();

        tick.Should().Throw<ContractException>().WithMessage("*Invariant*");
    }

    /// <summary>
    /// The game destroys the planet that a station orbits. The station moves up to the root frame, which has no point gravity,
    /// so it has no G·M to orbit: the World gives it the static driver. Physics then makes the hull static where the frame
    /// stopped, and nothing in either reports a problem.
    /// </summary>
    [Fact]
    public void TheHullOfAKeplerFrameStopsWhereTheFrameStoppedWhenItsPlanetIsDestroyed()
    {
        var warnings = new List<string>();
        World world = NewWorld(out JoltPhysicsBackend backend, warnings);
        using (world)
        {
            FrameHandle space = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e9f), MotionDriver.Static));
            Entity planetOwner = world.Spawn(new SpawnDesc
            {
                Frame = space,
                AsFrame = new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static, GravityField.Point(PlanetMu)),
            });
            Entity owner = world.Spawn(new SpawnDesc
            {
                Frame = world.GetOwnedFrame(planetOwner),
                Body = new BodyDesc(new BoxShapeDesc(new Vector3(5)), Mass: 1000),
                AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Kepler, Orbit: StationOrbit),
            }.OnKeplerOrbit(world, StationOrbit));
            FrameHandle station = world.GetOwnedFrame(owner);
            Tick(world, 30);

            world.Destroy(planetOwner);
            Tick(world, 30);

            world.Frames.Driver(station).Should().Be(MotionDriver.Static);
            world.Frames.Parent(station).Should().Be(space);
            JoltPhysicsBackend.BodyFacts hull = backend.BodiesIn(space)
                .Single(facts => BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner));
            hull.Motion.Should().Be(JoltPhysicsSharp.MotionType.Static);
            (hull.Position - world.Frames.LocalPose(station).Position).Length.Should().BeLessThan(1e-3);
            world.Errors.Should().BeEmpty();
            warnings.Should().BeEmpty();
        }
    }

    /// <summary>The game tries to give a frame a gravity field that physics cannot apply. The World refuses it.</summary>
    [Theory]
    [InlineData("NaN acceleration")]
    [InlineData("infinite acceleration")]
    [InlineData("NaN region")]
    [InlineData("infinite G·M")]
    [InlineData("negative G·M")]
    public void SetFrameGravityRefusesAFieldThatPhysicsCannotApply(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle ground = Ground(world, 9.81f);
        Entity box = world.Spawn(new SpawnDesc { Frame = ground, Pose = At(0, 100), Body = Cube() });
        world.Tick();

        Action set = () => world.SetFrameGravity(ground, BadGravity(kind));

        set.Should().Throw<ArgumentException>().WithMessage("*gravity*not valid*");
        world.Frames.Gravity(ground).Should().Be(GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Tick(world, 10);
        Finite(world.PoseInOwnFrame(box).Position).Should().BeTrue();
        world.PoseInOwnFrame(box).Position.Y.Should().BeLessThan(100, "the box still falls");
    }

    /// <summary>A box rests in a frame without gravity. The game turns gravity on, and the box falls.</summary>
    [Fact]
    public void ABoxFallsWhenTheGameTurnsOnTheGravityOfItsFrame()
    {
        using World world = NewWorld(out _);
        FrameHandle room = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static));
        Entity box = world.Spawn(new SpawnDesc { Frame = room, Pose = At(0, 100), Body = Cube() });
        Tick(world, 60);

        world.SetFrameGravity(room, GravityField.Uniform(new Vector3(0, -9.81f, 0)));
        Tick(world, 60);

        world.PoseInOwnFrame(box).Position.Y.Should().BeApproximately(100 - 0.5 * 9.81, 0.2, "the box fell for 1 s");
    }

    /// <summary>
    /// A box sleeps on the floor. The game changes the field of its frame to another field of the same case, with another
    /// acceleration, region or G·M. That is not the field that the box slept under, so the box wakes at the next step.
    /// </summary>
    [Theory]
    [InlineData("another acceleration")]
    [InlineData("another region")]
    [InlineData("another G·M")]
    public void ASleepingBoxWakesWhenTheGameChangesTheValuesOfTheFieldOfItsFrame(string change)
    {
        using World world = NewWorld(out JoltPhysicsBackend backend);
        var down = new Vector3(0, -9.81f, 0);
        var cabin = new BoundingBox(new Vector3(-20, -1, -20), new Vector3(20, 50, 20));
        var wideCabin = new BoundingBox(new Vector3(-30, -1, -30), new Vector3(30, 50, 30));
        // The floor of the point field is 100 m above its center, where the field pulls straight down.
        (GravityField before, GravityField after, double top) = change switch
        {
            "another acceleration" => (GravityField.Uniform(down), GravityField.Uniform(new Vector3(0, -5, 0)), 0.0),
            "another region" => (GravityField.Uniform(down, cabin), GravityField.Uniform(down, wideCabin), 0.0),
            "another G·M" => (GravityField.Point(PlanetMu), GravityField.Point(2 * PlanetMu), 100.0),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "No such change."),
        };
        FrameHandle frame = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e6f), MotionDriver.Static, before));
        Floor(world, frame, top);
        Entity box = world.Spawn(new SpawnDesc { Frame = frame, Pose = At(0, top + 0.5), Body = Cube() });
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, frame, box), 600).Should().BeLessThan(600, "the box rests on the floor and falls asleep");

        world.SetFrameGravity(frame, after);
        world.Tick();

        IsAwake(backend, frame, box).Should().BeTrue();
    }

    /// <summary>Spawn refuses a frame with a gravity field that physics cannot apply.</summary>
    [Theory]
    [InlineData("NaN acceleration")]
    [InlineData("infinite G·M")]
    public void SpawnRefusesAFrameWithAGravityFieldThatPhysicsCannotApply(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));

        Action spawn = () => world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static, BadGravity(kind)) });

        spawn.Should().Throw<ArgumentException>().WithMessage("*gravity*not valid*");
    }
}

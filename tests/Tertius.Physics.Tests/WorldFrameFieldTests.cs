using System.Numerics;
using Tertius.Simulation;
using static Tertius.Physics.Tests.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The orbit and the gravity field of a frame, which a game changes through the World. The World refuses a value that
/// the engine cannot use, at spawn and at a change. Before, a hyperbolic or NaN orbit made the frame, its hull and
/// everything inside it NaN, with no error.
/// </summary>
public class WorldFrameFieldTests
{
    private const double PlanetMu = 5e5;

    private static readonly KeplerOrbit StationOrbit = KeplerOrbit.Circular(5000, PlanetMu);

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
        _ => new GravityField((GravityKind)7, Vector3.Zero, null, 0),
    };

    private static bool Finite(Double3 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);

    /// <summary>A planet frame without gravity, a ball that flies in it, and a station with a hull on a circular orbit.</summary>
    private sealed class OrbitScene : IDisposable
    {
        public OrbitScene()
        {
            World = NewWorld(out JoltPhysicsBackend backend);
            Backend = backend;
            Planet = World.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
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
            });
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
        Finite(world.GetPose(scene.Ball).Position).Should().BeTrue();
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
        KeplerOrbit larger = KeplerOrbit.Circular(6000, PlanetMu);

        world.SetFrameOrbit(scene.Station, larger);

        world.Frames.LocalPose(scene.Station).Position.Length.Should().BeApproximately(6000, 1e-6);
        world.GetPose(scene.Owner).Should().Be(world.Frames.LocalPose(scene.Station), "the owner mirrors its frame at once");
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
        FrameHandle planet = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));
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

    /// <summary>The game tries to give a frame a gravity field that physics cannot apply. The World refuses it.</summary>
    [Theory]
    [InlineData("NaN acceleration")]
    [InlineData("infinite acceleration")]
    [InlineData("NaN region")]
    [InlineData("infinite G·M")]
    [InlineData("negative G·M")]
    [InlineData("undefined kind")]
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
        Finite(world.GetPose(box).Position).Should().BeTrue();
        world.GetPose(box).Position.Y.Should().BeLessThan(100, "the box still falls");
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

        world.GetPose(box).Position.Y.Should().BeApproximately(100 - 0.5 * 9.81, 0.2, "the box fell for 1 s");
    }

    /// <summary>Spawn refuses a frame with a gravity field that physics cannot apply.</summary>
    [Theory]
    [InlineData("NaN acceleration")]
    [InlineData("infinite G·M")]
    [InlineData("undefined kind")]
    public void SpawnRefusesAFrameWithAGravityFieldThatPhysicsCannotApply(string kind)
    {
        using World world = NewWorld(out _);
        FrameHandle root = world.CreateRootFrame(new FrameSpec(FrameBounds.Sphere(1e7f), MotionDriver.Static));

        Action spawn = () => world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static, BadGravity(kind)) });

        spawn.Should().Throw<ArgumentException>().WithMessage("*gravity*not valid*");
    }
}

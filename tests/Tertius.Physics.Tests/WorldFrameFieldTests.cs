using System.Numerics;
using JoltPhysicsSharp;
using Tertius.Frames.Rails;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Physics.Tests;

/// <summary>
/// The orbit and the gravity field of a frame. A game changes the gravity field through the World, which refuses a field
/// that physics cannot apply. The orbit lives on the frame's <see cref="KeplerMotion"/>, whose setter refuses an orbit that
/// the component cannot follow, and a game changes it by a swap of components. Before, a hyperbolic or NaN orbit made the
/// frame, its hull and everything inside it NaN, with no error.
/// </summary>
public sealed class WorldFrameFieldTests
{
    private const double PlanetMu = 5e5;

    private static readonly KeplerOrbit StationOrbit = KeplerOrbit.Circular(5000);

    /// <summary>Orbits that a <see cref="KeplerMotion"/> cannot follow, by the names that the theories use.</summary>
    private static KeplerOrbit BadOrbit(string kind) =>
        kind switch
        {
            "hyperbolic e = 1.2" => StationOrbit with { Eccentricity = 1.2 },
            "NaN semi-major axis" => StationOrbit with { SemiMajorAxis = double.NaN },
            "infinite spin" => StationOrbit with { SpinRate = double.PositiveInfinity },
            _ => default,
        };

    /// <summary>Gravity fields that physics cannot apply, by the names that the theories use.</summary>
    private static GravityField BadGravity(string kind) =>
        kind switch
        {
            "NaN acceleration" => GravityField.Uniform(new(0, float.NaN, 0)),
            "infinite acceleration" => GravityField.Uniform(new(float.PositiveInfinity, 0, 0)),
            "NaN region" => GravityField.Uniform(new(0, -9.81f, 0), new BoundingBox(new(float.NaN), new(1))),
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
            World = NewWorld(backend: out JoltPhysicsBackend backend);
            Backend = backend;
            Planet = World.CreateRootFrame(new(FrameBounds.Sphere(1e7f), GravityField.Point(PlanetMu)));
            Ball = World.Spawn(
                new()
                {
                    Frame = Planet,
                    Pose = At(100),
                    LinearVelocity = new Vector3(1, 0, 0),
                    Body = new(Shape: new SphereShapeDesc(0.5f), Mass: 1, LinearDamping: 0),
                }
            );
            Owner = World.Spawn(
                new SpawnDesc
                {
                    Frame = Planet,
                    Body = new(Shape: new BoxShapeDesc(new(5)), Motion: BodyMotion.Kinematic, Mass: 1000),
                    AsFrame = new(FrameBounds.Sphere(50)),
                }.OnKeplerOrbit(World, StationOrbit)
            );
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
            Backend
                .BodiesIn(Planet)
                .Single(facts =>
                    BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, Owner)
                );

        public void Dispose() => World.Dispose();
    }

    /// <summary>
    /// A station follows a valid orbit. The game replaces its <see cref="KeplerMotion"/> with one on a larger orbit. The new
    /// component's first run teleports the hull onto the new orbit, so the hull moves on at the orbital speed of the new
    /// orbit, √(μ / r), instead of crossing 1 km in one step (docs/plans/rails-components.md, "Changes of motion"). In the
    /// tick of the swap the old component's last sample moves the frame, so the swap has no gap, and from the second sync
    /// point after it the frame stands on the new orbit.
    /// </summary>
    [Fact]
    public void ANewKeplerMotionMovesAKeplerFrameAndItsHullOntoItsOrbitWithoutSweepingTheHullThere()
    {
        using var scene = new OrbitScene();
        World world = scene.World;
        Tick(world, 10);
        var larger = KeplerOrbit.Circular(6000);

        world.RemoveComponent(scene.Owner, world.GetComponent<KeplerMotion>(scene.Owner)!);
        world.AddComponent(scene.Owner, new KeplerMotion { Orbit = larger });
        world.Tick();
        scene
            .Hull.LinearVelocity.Length()
            .Should()
            .BeApproximately(
                (float)Math.Sqrt(PlanetMu / 5000),
                0.01f,
                "the old component's last sample moves the hull"
            );
        world.Tick();

        world.Frames.LocalPose(scene.Station).Position.Length.Should().BeApproximately(6000, 1e-6);
        world
            .PoseInOwnFrame(scene.Owner)
            .Should()
            .Be(world.Frames.LocalPose(scene.Station), "the owner mirrors its frame");
        scene
            .Hull.LinearVelocity.Length()
            .Should()
            .BeApproximately((float)Math.Sqrt(PlanetMu / 6000), 0.01f, "the hull follows the new orbit");
        (scene.Hull.Position - world.Frames.LocalPose(scene.Station).Position).Length.Should().BeLessThan(1e-3);
        world.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The reviewer's probe D10b, now for the component that checks the orbit: the spawn of a blueprint whose
    /// <see cref="KeplerMotion"/> has an orbit that it cannot follow throws, because the component's setter refuses the orbit,
    /// and nothing spawns. Before, a hyperbolic or NaN orbit made the frame, its hull and everything inside it NaN, with no
    /// error.
    /// </summary>
    [Theory]
    [InlineData("hyperbolic e = 1.2")]
    [InlineData("NaN semi-major axis")]
    [InlineData("infinite spin")]
    [InlineData("default")]
    public void ABlueprintWithAnOrbitThatIsNotValidForKeplerMotionThrowsAtSpawn(string kind)
    {
        using World world = NewWorld(backend: out _);
        FrameHandle planet = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f), GravityField.Point(PlanetMu)));
        world.Blueprints.Register(
            new(
                Name: "station",
                Components: [new(typeof(KeplerMotion), new Dictionary<string, object?> { ["Orbit"] = BadOrbit(kind) })],
                Body: new(Shape: new BoxShapeDesc(new(5)), Motion: BodyMotion.Kinematic, Mass: 1000),
                Frame: new(FrameBounds.Sphere(50))
            )
        );
        int live = world.Stats.LiveEntities;

        Action spawn = () => world.Spawn("station", planet, DPose.Identity);

        spawn.Should().Throw<ArgumentException>().WithMessage("*orbit*not valid*");
        world.Stats.LiveEntities.Should().Be(live);
    }

    /// <summary>
    /// The planet that a station orbits loses its point gravity. The World checks nothing about Kepler frames, so the
    /// station's <see cref="KeplerMotion"/> holds the frame still, and the world's debug checks, which run at the end of
    /// every sync point, find nothing wrong.
    /// </summary>
    [Fact]
    public void TheDebugChecksOfTheWorldPassAKeplerFrameWhoseParentLostItsPointGravity()
    {
        using var scene = new OrbitScene();
        scene.World.Tick();
        scene.World.SetFrameGravity(scene.Planet, GravityField.None);

        Action tick = () => Tick(scene.World, 3);

        tick.Should().NotThrow();
        scene.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The game destroys the planet that a station orbits. The station moves up to the root frame, which has no point gravity,
    /// so its <see cref="KeplerMotion"/> has no G·M to orbit: it writes no sample and logs that once. The hull keeps its
    /// motion, so it stays kinematic and holds still where its last sample left it, and its frame with it.
    /// Nothing in either reports a problem.
    /// </summary>
    [Fact]
    public void TheHullOfAKeplerFrameHoldsStillWithItsFrameWhenItsPlanetIsDestroyed()
    {
        var warnings = new List<string>();
        World world = NewWorld(backend: out JoltPhysicsBackend backend, warnings: warnings);
        using (world)
        {
            var log = new List<string>();
            world.Log += log.Add;
            FrameHandle space = world.CreateRootFrame(new(FrameBounds.Sphere(1e9f)));
            Entity planetOwner = world.Spawn(
                new() { Frame = space, AsFrame = new(FrameBounds.Sphere(1e7f), GravityField.Point(PlanetMu)) }
            );
            Entity owner = world.Spawn(
                new SpawnDesc
                {
                    Frame = world.GetOwnedFrame(planetOwner),
                    Body = new(Shape: new BoxShapeDesc(new(5)), Motion: BodyMotion.Kinematic, Mass: 1000),
                    AsFrame = new(FrameBounds.Sphere(50)),
                }.OnKeplerOrbit(world, StationOrbit)
            );
            FrameHandle station = world.GetOwnedFrame(owner);
            Tick(world, 30);

            world.Destroy(planetOwner);
            Tick(world, 2);
            DPose stopped = world.Frames.LocalPose(station);
            Tick(world, 30);

            world.Frames.Parent(station).Should().Be(space);
            world.Frames.LocalPose(station).Should().Be(stopped, "the frame holds still");
            world.Frames.LinearVelocity(station).Should().Be(Double3.Zero);
            JoltPhysicsBackend.BodyFacts hull = backend
                .BodiesIn(space)
                .Single(facts =>
                    BodyUserData.Kind(facts.UserData) == BodyKind.Primary && BodyUserData.Names(facts.UserData, owner)
                );
            hull.Motion.Should().Be(MotionType.Kinematic);
            (hull.Position - stopped.Position).Length.Should().BeLessThan(1e-3);
            log.Should().ContainSingle(line => line.Contains($"{station}"));
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
        using World world = NewWorld(backend: out _);
        FrameHandle ground = Ground(world, 9.81f);
        Entity box = world.Spawn(
            new()
            {
                Frame = ground,
                Pose = At(0, 100),
                Body = Cube(),
            }
        );
        world.Tick();

        Action set = () => world.SetFrameGravity(ground, BadGravity(kind));

        set.Should().Throw<ArgumentException>().WithMessage("*gravity*not valid*");
        world.Frames.Gravity(ground).Should().Be(GravityField.Uniform(new(0, -9.81f, 0)));
        Tick(world, 10);
        Finite(world.PoseInOwnFrame(box).Position).Should().BeTrue();
        world.PoseInOwnFrame(box).Position.Y.Should().BeLessThan(100, "the box still falls");
    }

    /// <summary>A box rests in a frame without gravity. The game turns gravity on, and the box falls.</summary>
    [Fact]
    public void ABoxFallsWhenTheGameTurnsOnTheGravityOfItsFrame()
    {
        using World world = NewWorld(backend: out _);
        FrameHandle room = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f)));
        Entity box = world.Spawn(
            new()
            {
                Frame = room,
                Pose = At(0, 100),
                Body = Cube(),
            }
        );
        Tick(world, 60);

        world.SetFrameGravity(room, GravityField.Uniform(new(0, -9.81f, 0)));
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
        using World world = NewWorld(backend: out JoltPhysicsBackend backend);
        var down = new Vector3(0, -9.81f, 0);
        var cabin = new BoundingBox(new(-20, -1, -20), new(20, 50, 20));
        var wideCabin = new BoundingBox(new(-30, -1, -30), new(30, 50, 30));
        // The floor of the point field is 100 m above its center, where the field pulls straight down.
        (GravityField before, GravityField after, double top) = change switch
        {
            "another acceleration" => (GravityField.Uniform(down), GravityField.Uniform(new(0, -5, 0)), 0.0),
            "another region" => (GravityField.Uniform(down, cabin), GravityField.Uniform(down, wideCabin), 0.0),
            "another G·M" => (GravityField.Point(PlanetMu), GravityField.Point(2 * PlanetMu), 100.0),
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, "No such change."),
        };
        FrameHandle frame = world.CreateRootFrame(new(FrameBounds.Sphere(1e6f), before));
        Floor(world, frame, top);
        Entity box = world.Spawn(
            new()
            {
                Frame = frame,
                Pose = At(0, top + 0.5),
                Body = Cube(),
            }
        );
        world.Tick();
        TickUntil(world, () => !IsAwake(backend, frame, box), 600)
            .Should()
            .BeLessThan(600, "the box rests on the floor and falls asleep");

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
        using World world = NewWorld(backend: out _);
        FrameHandle root = world.CreateRootFrame(new(FrameBounds.Sphere(1e7f)));

        Action spawn = () =>
            world.Spawn(new() { Frame = root, AsFrame = new(FrameBounds.Sphere(50), BadGravity(kind)) });

        spawn.Should().Throw<ArgumentException>().WithMessage("*gravity*not valid*");
    }
}

using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The first scene of the vertical slice, "a ship orbits a planet", at Earth scale. The run starts 10 s before the ship
/// reaches the far side of its orbit, where the mean anomaly wraps from π to -π, and runs 20 s. Every test of the class
/// reads the dumps of that one run. The full orbit is the manual run of scripts/orbit.json.
/// </summary>
public sealed class OrbitRun : IDisposable
{
    /// <summary>The orbit's radius, 400 km above Earth's 6,371 km, in meters.</summary>
    public const double Radius = 6.771e6;

    /// <summary>Earth's G·M, in m³/s².</summary>
    public const double Mu = 3.986004418e14;

    /// <summary>The ship's angular speed round Earth, sqrt(mu / r³), in radians per second.</summary>
    public static double MeanMotion => Math.Sqrt(Mu / (Radius * Radius * Radius));

    public OrbitRun()
    {
        World = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true, StartTime = Math.PI / MeanMotion - 10 }, new Tertius.Physics.JoltPhysicsBackend());
        Scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(World, RenderResources.None);
        Result = ScriptRunner.Run(Script.Parse("""
            { "scene": "orbit", "steps": [ { "do": "dump", "name": "before" }, { "do": "run", "ticks": 1200 }, { "do": "dump", "name": "after" } ] }
            """), World, Scene);
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public RunResult Result { get; }

    public void Dispose() => World.Dispose();
}

public sealed class OrbitSliceTests(OrbitRun run) : IClassFixture<OrbitRun>
{
    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void TheShipIsOnItsCircleAtTheAngleThatItsTimeGivesOnBothSidesOfTheFarSide()
    {
        foreach (StateDump dump in run.Result.Dumps)
        {
            // The orbit starts at (r, 0, 0) and runs counterclockwise seen from +Y: at angle M it is at r (cos M, 0, -sin M).
            double m = OrbitRun.MeanMotion * dump.Time;
            FramePose ship = dump.Entity("ship").RootPose;
            ship.Frame.Should().Be("earth");
            ExpectNear(ship.Position, new Double3(Math.Cos(m), 0, -Math.Sin(m)) * OrbitRun.Radius, 1e-3);
        }
        run.Result.Dump("before").Time.Should().BeLessThan(Math.PI / OrbitRun.MeanMotion);
        run.Result.Dump("after").Time.Should().BeGreaterThan(Math.PI / OrbitRun.MeanMotion);
    }

    [Fact]
    public void TheShipKeepsItsBellyTowardEarthAndItsNoseAlongTheOrbit()
    {
        foreach (StateDump dump in run.Result.Dumps)
        {
            double m = OrbitRun.MeanMotion * dump.Time;
            DQuaternion rotation = dump.Entity("ship").RootPose.Rotation;

            ExpectNear(rotation.Rotate(new Double3(0, -1, 0)), new Double3(-Math.Cos(m), 0, Math.Sin(m)), 1e-9);
            ExpectNear(rotation.Rotate(new Double3(0, 0, -1)), new Double3(-Math.Sin(m), 0, -Math.Cos(m)), 1e-9);
        }
    }

    [Fact]
    public void TheChaseCameraRidesBehindAndAboveTheShipAndLooksAtItsCenter()
    {
        StateDump after = run.Result.Dump("after");
        FramePose ship = after.Entity("ship").RootPose;
        FramePose chase = after.Entity("chase").RootPose;

        // In the ship's own axes, the camera is still at (0, 12, 70) m, 6,771 km from Earth's center, and it looks
        // along -(0, 12, 70) / sqrt(5,044), at the ship's center.
        DQuaternion toShip = ship.Rotation.Conjugate();
        ExpectNear(toShip.Rotate(chase.Position - ship.Position), new Double3(0, 12, 70), 1e-6);
        ExpectNear(toShip.Rotate(chase.Rotation.Rotate(new Double3(0, 0, -1))), new Double3(0, -0.1689638151, -0.9856222548), 1e-9);
    }

    [Fact]
    public void TheChaseCameraIsInsideTheShipsBubble()
    {
        // An entity outside its frame's bubble would leave the frame, if a later check covers entities without bodies.
        FrameHandle ship = run.World.GetOwnedFrame(run.Scene["ship"]);
        Double3 chase = run.World.GetPose(run.Scene["chase"]).Position;

        run.World.Frames.Bounds(ship).FullyOutside(chase, 0, run.World.Frames.Bounds(ship).ExitMargin(0)).Should().BeFalse();
        run.World.Frames.Bounds(ship).SignedDistance(chase).Should().BeNegative();
    }

    [Fact]
    public void TheSceneOffersTheChaseViewAndTheFarViewAndVTogglesBetweenThem()
    {
        run.Scene.Views.Should().Equal("chase", "far");
        run.Scene.NextView(run.Scene["chase"]).Should().Be(run.Scene["far"]);
        run.Scene.NextView(run.Scene["far"]).Should().Be(run.Scene["chase"]);
    }

    [Fact]
    public void TheSunHasTheAngularSizeOfTheRealSunSeenFromEarth()
    {
        run.World.PublishRenderSnapshot();
        run.World.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should().BeTrue();

        // The sun's radius, 696,000 km, seen from 1 AU away.
        snapshot.Sun.AngularRadius.Should().BeApproximately(4.65e-3f, 2e-5f);
    }

    [Fact]
    public void TheRunReportsNoErrorAndKeepsTheChaseView()
    {
        StateDump after = run.Result.Dump("after");

        after.Errors.Should().BeEmpty();
        after.Camera.Should().Be("chase");
        after.Frames.Select(f => (f.Id, f.Parent, f.Driver)).Should().Contain(("ship", "earth", MotionDriver.Kepler));
    }

    [Fact]
    public void EveryCameraStepOfTheCheckedInOrbitScriptNamesAnEntityOfTheScene()
    {
        Script script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "orbit.json")));

        script.Scene.Should().Be("orbit");
        script.Steps.OfType<CameraStep>().Should().NotBeEmpty();
        foreach (CameraStep step in script.Steps.OfType<CameraStep>())
            run.Scene.TryGetEntity(step.Entity, out _).Should().BeTrue($"the scene has no entity {step.Entity}");
    }
}

/// <summary>The start images of the orbit scene, from both views, on the real headless renderer.</summary>
[Collection(GpuCollection.Name)]
public sealed class OrbitSliceImageTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static (int R, int G, int B) Average(Png image, int x, int y)
    {
        int r = 0, g = 0, b = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                (int pr, int pg, int pb) = image.At(x + dx, y + dy);
                r += pr;
                g += pg;
                b += pb;
            }
        return (r / 25, g / 25, b / 25);
    }

    [Fact]
    public void TheChaseViewShowsTheShipWithEarthBelowAndTheFarViewShowsTheWholePlanet()
    {
        Script script = Script.Parse("""
            { "scene": "orbit", "window": { "width": 1280, "height": 720 },
              "steps": [ { "do": "capture", "name": "chase" }, { "do": "camera", "entity": "far" }, { "do": "capture", "name": "far" } ] }
            """);
        using World world = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            ScriptRunner.Run(script, world, DemoScenes.All.Single(s => s.Name == "orbit").Build(world, renderer), folder, new HeadlessPlayback(renderer, script.Window));
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty();
        }

        // The chase camera looks at the ship's center from 70 m behind and 12 m above it, so the hull fills the middle.
        // Earth's horizon, 19.8 degrees below level from 400 km up, crosses the image below the ship.
        Png chase = Png.Load(Path.Combine(folder, "chase.png"));
        (int r, int g, int b) ship = Average(chase, 640, 440);
        (ship.r > 60 && Math.Abs(ship.r - ship.g) < 12 && Math.Abs(ship.g - ship.b) < 12).Should().BeTrue($"the hull is gray, not {ship}");
        (int r, int g, int b) ground = Average(chase, 300, 650);
        (ground.b > ground.r + 40).Should().BeTrue($"Earth below is blue, not {ground}");
        (int r, int g, int b) sky = Average(chase, 640, 120);
        (sky.r + sky.g + sky.b < 90).Should().BeTrue($"the sky above is dark, not {sky}");

        // From three Earth radii out, the planet's disc spans 39 degrees of the 60-degree view, around the middle.
        Png far = Png.Load(Path.Combine(folder, "far.png"));
        (int r, int g, int b) planet = Average(far, 700, 380);
        (planet.b > planet.r + 40).Should().BeTrue($"the sunward side of the planet is blue, not {planet}");
        (int r, int g, int b) space = Average(far, 60, 60);
        (space.r + space.g + space.b < 90).Should().BeTrue($"the corner is space, not {space}");
    }
}

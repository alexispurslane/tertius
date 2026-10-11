using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The first scene of the vertical slice, "a ship orbits a planet", run for a quarter orbit: about 83,000 ticks at Earth
/// scale. The run is long, so every test of the class reads the dumps of one run.
/// </summary>
public sealed class OrbitQuarterRun : IDisposable
{
    public OrbitQuarterRun()
    {
        // No invariant checks every tick: they cost about 0.5 ms a tick, and the run has 83,000 ticks. The checks run in
        // the shorter World tests.
        World = new World(new WorldOptions { MaxDegreeOfParallelism = 1, DisposePhysics = true }, new Tertius.Physics.JoltPhysicsBackend());
        Scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(World, RenderResources.None);
        Result = ScriptRunner.Run(Script.Parse("""
            { "scene": "orbit", "steps": [ { "do": "dump", "name": "start" }, { "do": "run", "ticks": 83173 }, { "do": "dump", "name": "quarter" } ] }
            """), World, Scene);
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public RunResult Result { get; }

    public void Dispose() => World.Dispose();
}

public sealed class OrbitSliceTests(OrbitQuarterRun run) : IClassFixture<OrbitQuarterRun>
{
    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    /// <summary>The mean motion of the ship's orbit, sqrt(mu / r³), in radians per second.</summary>
    private static double MeanMotion => Math.Sqrt(DemoScenes.EarthMu / Math.Pow(DemoScenes.OrbitRadius, 3));

    [Fact]
    public void AfterAQuarterOrbitTheShipIsAQuarterOfTheWayRoundEarthOnItsCircle()
    {
        StateDump quarter = run.Result.Dump("quarter");

        // The orbit starts at (r, 0, 0) and runs counterclockwise seen from +Y: at angle M it is at r (cos M, 0, -sin M).
        double m = MeanMotion * quarter.Time;
        FramePose ship = quarter.Entity("ship").RootPose;
        ship.Frame.Should().Be("earth");
        ExpectNear(ship.Position, new Double3(Math.Cos(m), 0, -Math.Sin(m)) * DemoScenes.OrbitRadius, 1e-3);
        m.Should().BeApproximately(Math.PI / 2, 1e-4, "83,173 ticks are a quarter of the 5,544.9 s orbit");
    }

    [Fact]
    public void TheShipKeepsItsBellyTowardEarthAndItsNoseAlongTheOrbit()
    {
        foreach (StateDump dump in run.Result.Dumps)
        {
            double m = MeanMotion * dump.Time;
            DQuaternion rotation = dump.Entity("ship").RootPose.Rotation;

            ExpectNear(rotation.Rotate(new Double3(0, -1, 0)), new Double3(-Math.Cos(m), 0, Math.Sin(m)), 1e-9);
            ExpectNear(rotation.Rotate(new Double3(0, 0, -1)), new Double3(-Math.Sin(m), 0, -Math.Cos(m)), 1e-9);
        }
    }

    [Fact]
    public void TheChaseCameraRidesAtItsPlaceBehindTheShipToAMicrometerAtEarthScale()
    {
        StateDump quarter = run.Result.Dump("quarter");
        FramePose ship = quarter.Entity("ship").RootPose;
        FramePose chase = quarter.Entity("chase").RootPose;

        // The camera's offset from the ship, in the ship's own axes, is still (0, 12, 70) m, 6,771 km from Earth's center.
        Double3 offset = ship.Rotation.Conjugate().Rotate(chase.Position - ship.Position);
        ExpectNear(offset, new Double3(0, 12, 70), 1e-6);
    }

    [Fact]
    public void TheRunReportsNoErrorAndKeepsTheChaseView()
    {
        StateDump quarter = run.Result.Dump("quarter");

        quarter.Errors.Should().BeEmpty();
        quarter.Camera.Should().Be("chase");
        quarter.Frames.Select(f => (f.Id, f.Parent, f.Driver)).Should().Contain(("ship", "earth", MotionDriver.Kepler));
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

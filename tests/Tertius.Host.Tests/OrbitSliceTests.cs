using System.Numerics;
using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The first scene of the vertical slice, "a ship orbits a planet", at Earth scale: the sun is the root frame, Earth goes
/// round it on its real orbit, Earth's surface turns in place once per sidereal day, and the ship falls freely round
/// Earth under its gravity. The run starts 10 s before the ship reaches the far side of the circle that it starts on, on
/// Earth's night side, and runs 20 s. Every test of the class reads that one run. The full orbit is the manual run of
/// scripts/orbit.json.
/// </summary>
public sealed class OrbitRun : IDisposable
{
    /// <summary>The radius of the circle that the ship starts on, 400 km above Earth's 6,371 km, in meters.</summary>
    public const double Radius = 6.771e6;

    /// <summary>Earth's G·M, in m³/s².</summary>
    public const double Mu = 3.986004418e14;

    /// <summary>The tilt of the ship's orbit to Earth's equator, as the ISS flies, in radians.</summary>
    public const double Inclination = 51.64 * Math.PI / 180;

    /// <summary>The ship's angular speed round Earth, sqrt(mu / r³), in radians per second.</summary>
    public static double MeanMotion => Math.Sqrt(Mu / (Radius * Radius * Radius));

    public OrbitRun()
    {
        World = new(
            new()
            {
                MaxDegreeOfParallelism = 1,
                DisposePhysics = true,
                StartTime = Math.PI / MeanMotion - 10,
            },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced)
        );
        Scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(World, RenderResources.None);
        Result = ScriptRunner.Run(
            script: Script.Parse(
                """
                { "scene": "orbit", "steps": [ { "do": "dump", "name": "before" }, { "do": "run", "ticks": 1200 }, { "do": "dump", "name": "after" } ] }
                """
            ),
            world: World,
            scene: Scene
        );
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public RunResult Result { get; }

    /// <summary>
    /// The point of the circle at mean anomaly <paramref name="m"/>, in Earth's axes, which follow the equator. The circle
    /// crosses the equator northward at +X, at m = 0, and leans by <see cref="Inclination"/> about +X.
    /// </summary>
    public static Double3 OnCircle(double m) =>
        new Double3(Math.Cos(m), Math.Sin(m) * Math.Sin(Inclination), -Math.Sin(m) * Math.Cos(Inclination)) * Radius;

    public FrameHandle Frame(string id) => World.GetOwnedFrame(Scene[id]);

    public void Dispose() => World.Dispose();
}

public sealed class OrbitSliceTests(OrbitRun run) : IClassFixture<OrbitRun>
{
    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void TheSunIsTheRootAndEarthGoesRoundItWithItsSurfaceAndTheShip() =>
        run
            .Result.Dump("after")
            .Frames.Select(f => (f.Id, f.Parent, f.Follows))
            .Should()
            .BeEquivalentTo([
                ("sun", null, "static"),
                ("earth", "sun", "rails"),
                ("earth-surface", "earth", "rails"),
                ("ship", "earth", "physics"),
            ]);

    [Fact]
    public void TheShipFallsRoundEarthOnTheCircleThatItStartedOn()
    {
        foreach (StateDump dump in run.Result.Dumps)
        {
            FramePose ship = dump.Entity("ship").Pose;
            ship.Frame.Should().Be("earth");
            // Without gravity the ship would leave the circle by g t² / 2, 1.7 km after 20 s. Jolt adds each tick's pull to
            // the velocity before it moves the ship, so the ship falls ahead of the circle by g Δt t / 2: 1.45 m after 20 s.
            ExpectNear(ship.Position, OrbitRun.OnCircle(OrbitRun.MeanMotion * dump.Time), 3);
        }

        run.Result.Dump("before").Time.Should().BeLessThan(Math.PI / OrbitRun.MeanMotion);
        run.Result.Dump("after").Time.Should().BeGreaterThan(Math.PI / OrbitRun.MeanMotion);
    }

    [Fact]
    public void TheShipKeepsItsBellyTowardEarthAndItsNoseAlongItsMotion()
    {
        foreach (StateDump dump in run.Result.Dumps)
        {
            double m = OrbitRun.MeanMotion * dump.Time;
            DQuaternion rotation = dump.Entity("ship").Pose.Rotation;
            Double3 along = (OrbitRun.OnCircle(m + 1e-6) - OrbitRun.OnCircle(m - 1e-6)).Normalized();

            // Jolt keeps the rotation as a float quaternion: 1,200 ticks of it turn the ship 1.7e-5 rad off.
            ExpectNear(rotation.Rotate(new Double3(0, -1, 0)), -OrbitRun.OnCircle(m).Normalized(), 1e-4);
            ExpectNear(rotation.Rotate(new Double3(0, 0, -1)), along, 1e-4);
        }
    }

    [Fact]
    public void TheChaseCameraRidesBehindAndAboveTheShipAndLooksAtItsCenter()
    {
        FrameHandle earth = run.Frame("earth");
        DPose ship = run.World.Snapshot.PoseIn(run.Scene["ship"], earth).Found();
        DPose chase = run.World.Snapshot.PoseIn(run.Scene["chase"], earth).Found();

        // In the ship's own axes, the camera is at (0, 12, 70) m, and it looks along -(0, 12, 70) / sqrt(5,044), at the
        // ship's center. The ship's rotation comes from Jolt as floats, so the 71 m offset carries about 5 µm of rounding.
        DQuaternion toShip = ship.Rotation.Conjugate();
        ExpectNear(toShip.Rotate(chase.Position - ship.Position), new(0, 12, 70), 1e-4);
        ExpectNear(
            toShip.Rotate(chase.Rotation.Rotate(new Double3(0, 0, -1))),
            new(0, -0.1689638151, -0.9856222548),
            1e-6
        );
    }

    [Fact]
    public void TheChaseCameraRidesOutsideTheShipsBubbleAndStaysInTheShipsFrameBecauseItHasNoBody()
    {
        // The bubble fits just around the hull, and the camera rides 71 m from the ship's center, past the bubble and its
        // margin. A hand-off from one frame to another is a step of physics, and it moves only bodies.
        FrameBounds bubble = run.World.Frames.Bounds(run.Frame("ship"));
        Double3 chase = run.World.PoseInOwnFrame(run.Scene["chase"]).Position;

        bubble.FullyOutside(chase, 0, bubble.ExitMargin(0)).Should().BeTrue();
        foreach (StateDump dump in run.Result.Dumps)
            dump.Entity("chase").Pose.Frame.Should().Be("ship", $"the camera rides in the ship's frame in {dump.Name}");
    }

    [Fact]
    public void TheSceneOffersTheChaseTheFarAndThePersonsViewAndVCyclesThroughThem()
    {
        run.Scene.Views.Should().Equal("chase", "far", "person");
        run.Scene.NextView(run.Scene["chase"]).Should().Be(run.Scene["far"]);
        run.Scene.NextView(run.Scene["far"]).Should().Be(run.Scene["person"]);
        run.Scene.NextView(run.Scene["person"]).Should().Be(run.Scene["chase"]);
    }

    [Fact]
    public void EarthsSurfaceTurnsOncePerSiderealDayAboutEarthsAxis()
    {
        double angle = 2 * Math.PI / 86_164.0905 * run.World.Time;

        DQuaternion surface = run.World.Frames.LocalPose(run.Frame("earth-surface")).Rotation;

        ExpectNear(surface.Rotate(Double3.UnitX), new(Math.Cos(angle), 0, -Math.Sin(angle)), 1e-9);
        ExpectNear(surface.Rotate(Double3.UnitY), Double3.UnitY, 1e-12);
    }

    [Fact]
    public void TheEarthFrameDoesNotTurnAndItsPoleLeansTowardTheSunOfTheJuneSolstice()
    {
        DQuaternion earth = run.World.Frames.LocalPose(run.Frame("earth")).Rotation;
        double obliquity = 23.4392811 * Math.PI / 180;

        // The pole leans 23.44 degrees from the pole of the orbit toward -Z, where the sun stands seen from Earth in June,
        // and +X, the March equinox direction, stays where it is.
        ExpectNear(earth.Rotate(Double3.UnitY), new(0, Math.Cos(obliquity), -Math.Sin(obliquity)), 1e-9);
        ExpectNear(earth.Rotate(Double3.UnitX), Double3.UnitX, 1e-9);
        run.World.Frames.AngularVelocity(run.Frame("earth")).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheSunHasTheAngularSizeOfTheRealSunAtEarthsDistance()
    {
        double distance = run.World.Frames.Relative(run.Frame("sun"), run.Frame("ship")).Position.Length;
        run.World.PublishRenderSnapshot();
        run.World.RenderSnapshots.AcquireLatest().Should().NotBeNull();
        RenderSnapshot snapshot = run.World.RenderSnapshots.ReadBuffer;

        // The sun's radius, 695,700 km, from Earth's distance, which runs from 147 to 152 million km over a year. The
        // sun's light is the light of the sun's root entity.
        distance.Should().BeInRange(1.47e11, 1.53e11);
        uint sunIndex = run.Scene["sun"].Index;
        PointRenderLight sun = snapshot
            .Lights.Select(light => light.TryGetValue(out PointRenderLight point) ? point : default)
            .Single(light => light.EntityIndex == sunIndex && light.Power > 0);
        Math.Asin(sun.Radius / sun.Position.Length()).Should().BeApproximately(Math.Atan(6.957e8 / distance), 1e-7);
    }

    [Fact]
    public void TheSunlightAtTheShipIsTheSolarConstantForEarthsDistance()
    {
        double distance = run.World.Frames.Relative(run.Frame("sun"), run.Frame("ship")).Position.Length;
        run.World.PublishRenderSnapshot();
        run.World.RenderSnapshots.AcquireLatest().Should().NotBeNull();
        RenderSnapshot snapshot = run.World.RenderSnapshots.ReadBuffer;
        uint sunIndex = run.Scene["sun"].Index;
        PointRenderLight sun = snapshot
            .Lights.Select(light => light.TryGetValue(out PointRenderLight point) ? point : default)
            .Single(light => light.EntityIndex == sunIndex && light.Power > 0);

        double irradiance = sun.Power / (4 * Math.PI * distance * distance);

        // The scene is in SI units (docs/plans/local-light-shadows.md, decision 44): the Sun's whole power, which gives
        // 1,361 W/m² at 1 AU. Earth's distance runs from 0.983 to 1.017 AU over a year.
        irradiance.Should().BeInRange(1361 / (1.017 * 1.017), 1361 / (0.983 * 0.983));
    }

    [Fact]
    public void OnTheFarSideOfItsOrbitTheShipIsInEarthsShadow()
    {
        DPose sun = run.World.Frames.Relative(run.Frame("sun"), run.Frame("ship"));
        DPose earth = run.World.Frames.Relative(run.Frame("earth"), run.Frame("ship"));

        // The line from the ship to the sun passes through Earth's sphere.
        Double3 toSun = sun.Position.Normalized();
        double along = Double3.Dot(earth.Position, toSun);
        along.Should().BePositive("Earth lies between the ship and the sun");
        (earth.Position - toSun * along).Length.Should().BeLessThan(6.371e6);
    }

    [Fact]
    public void TheRunReportsNoErrorAndKeepsTheChaseView()
    {
        StateDump after = run.Result.Dump("after");

        after.Errors.Should().BeEmpty();
        after.Camera.Should().Be("chase");
    }

    [Fact]
    public void EveryCameraStepOfTheCheckedInOrbitScriptNamesAnEntityOfTheScene()
    {
        var script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "orbit.json")));

        script.Scene.Should().Be("orbit");
        script.Steps.Select(step => step.Value).OfType<CameraStep>().Should().NotBeEmpty();
        foreach (CameraStep step in script.Steps.Select(step => step.Value).OfType<CameraStep>())
            run.Scene.EntityOf(step.Entity).Should().NotBeNull($"the scene has no entity {step.Entity}");
    }
}

/// <summary>The orbit scene at its start, the March equinox: the sun stands straight above the ship.</summary>
public sealed class OrbitStartTests
{
    [Fact]
    public void AtTheStartTheSunStandsStraightAboveTheShip()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None);

        Double3 toSun = world
            .Frames.Relative(world.GetOwnedFrame(scene["sun"]), world.GetOwnedFrame(scene["ship"]))
            .Position.Normalized();

        // The ship's up is +Y. Earth's distance from the ship, 6,771 km, shifts the sun by under 5e-5 rad.
        Double3.Distance(toSun, Double3.UnitY).Should().BeLessThan(1e-4);
    }

    [Fact]
    public void OnlyTheCabinHasGravityAndItPullsTowardTheShipsBelly()
    {
        using World world = NewJoltWorld();
        SceneInstance scene = DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None);
        FrameHandle ship = world.GetOwnedFrame(scene["ship"]);

        // The ship falls freely, so its proper acceleration is zero, and only its own field acts inside it. The belly, -Y,
        // faces Earth. Physics takes a person's gravity at the capsule's center, so a person on a spacewalk floats: on the
        // roof or against a side, the center is 0.92 m or 0.32 m out from the hull. The field ends at the cabin, inside
        // the slabs. The ship turns on its facing orbit, but the reads come before its first step, when it has no turns yet,
        // so the effective gravity is the linear gravity (docs/plans/spin-gravity.md, "V5, the effective gravity").
        world.Frames.EffectiveGravity(ship, new(3, -2, 20)).Should().Be(new Double3(0, -9.81f, 0));
        world.Frames.EffectiveGravity(ship, new(0, 4.92, 0)).Should().Be(Double3.Zero);
        world.Frames.EffectiveGravity(ship, new(10.32, 0, 0)).Should().Be(Double3.Zero);
        world.Frames.EffectiveGravity(ship, new(0, 0, -30.32)).Should().Be(Double3.Zero);
        world.Frames.EffectiveGravity(ship, new(0, 3.75, 0)).Should().Be(Double3.Zero);
    }

    [Fact]
    public void SpaceHasNoSkyOrGroundSoTheAmbientLightIsTheSameFromEveryDirection()
    {
        using World world = NewJoltWorld();
        DemoScenes.All.Single(s => s.Name == "orbit").Build(world, RenderResources.None);

        world.Ambient.SkyColor.Should().Be(world.Ambient.GroundColor);
    }

    /// <summary>Resources that give each mesh, texture and material its own id, so that the snapshot holds the scene's instances.</summary>
    private sealed class NumberedResources : IRenderResources
    {
        private int next = 1;

        public MeshId CreateMesh(MeshData mesh) => new(next++);

        public TextureId CreateTexture(TextureData texture) => new(next++);

        public MaterialId CreateMaterial(MaterialDesc material) => new(next++);
    }

    [Fact]
    public void EarthsSphereCastsShadowsAndTheSunDoesNot()
    {
        using World world = NewJoltWorld();
        DemoScenes.All.Single(s => s.Name == "orbit").Build(world, new NumberedResources());
        world.PublishRenderSnapshot();
        world.RenderSnapshots.AcquireLatest().Should().NotBeNull();
        RenderSnapshot snapshot = world.RenderSnapshots.ReadBuffer;

        RenderInstance earth = snapshot.Instances.Single(instance => instance.BoundingRadius == 6.371e6f);
        RenderInstance sun = snapshot.Instances.Single(instance => instance.BoundingRadius == 6.957e8f);
        (earth.Flags & RenderFlags.CastsShadow).Should().Be(RenderFlags.CastsShadow);
        (sun.Flags & RenderFlags.CastsShadow).Should().Be(RenderFlags.None);
        // Both are drawn, and neither ever becomes an impostor.
        (earth.Flags & (RenderFlags.Visible | RenderFlags.Celestial))
            .Should()
            .Be(RenderFlags.Visible | RenderFlags.Celestial);
        (sun.Flags & (RenderFlags.Visible | RenderFlags.Celestial))
            .Should()
            .Be(RenderFlags.Visible | RenderFlags.Celestial);
    }
}

/// <summary>The start images of the orbit scene, from both views, on the real headless renderer.</summary>
public sealed class OrbitSliceImageTests : IDisposable
{
    private readonly string folder = NewFolder();

    /// <summary>
    /// The fixed exposure of a sunlit view of the orbit scene, in stops: −log2(1,361 / 2.5). The scene is in SI units, with
    /// the Sun's 1,361 W/m², and this exposure shows it as 0 stops showed it beside the sunlight of 2.5 W/m² that the
    /// views below were judged with.
    /// </summary>
    private static readonly float SunlitStops = -MathF.Log2(1361f / 2.5f);

    /// <summary>
    /// The fixed exposure that shows the physical stars, in stops: 10, which undoes their scale of 2⁻¹⁰ W/(m²·sr)
    /// (docs/plans/local-light-shadows.md, settled fork 1).
    /// </summary>
    private const float StarStops = 10;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    /// <summary>
    /// The chase capture with the ship <paramref name="degrees"/> along its first circle, as the world starts there, at
    /// the fixed exposure of <paramref name="stops"/>.
    /// </summary>
    private Png ChaseAt(double degrees, string name, float stops)
    {
        double period = 2 * Math.PI / OrbitRun.MeanMotion;
        using var world = new World(
            new()
            {
                MaxDegreeOfParallelism = 1,
                DisposePhysics = true,
                StartTime = degrees / 360 * period,
            },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced)
        );
        var script = Script.Parse(
            $$"""{ "scene": "orbit", "window": { "width": 1280, "height": 720 }, "steps": [ { "do": "capture", "name": "{{name}}" } ] }"""
        );
        VulkanRenderer renderer = GpuTesting.Headless(width: 16, height: 16, exposure: new FixedExposure(stops));
        try
        {
            ScriptRunner.Run(
                script: script,
                world: world,
                scene: DemoScenes.All.Single(s => s.Name == "orbit").Build(world, renderer),
                outputFolder: folder,
                playback: new HeadlessPlayback(renderer, script.Window, new PngFolder(folder))
            );
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty();
        }

        return Png.Load(Path.Combine(folder, name + ".png"));
    }

    [Fact]
    public void AsTheShipTurnsAQuarterTurnOverAQuarterOrbitTheStarsSweepAcrossTheChaseView()
    {
        // The chase camera rides in the ship's frame, which turns once per orbit to face Earth. The stars are fixed to the
        // sun frame's axes, so a quarter orbit later they stand elsewhere in the view. The rows above the ship show sky. The
        // stars are physical, so beside the sunlit hull they show only at an exposure far above the hull's.
        static HashSet<(int X, int Y)> Stars(Png image)
        {
            var stars = new HashSet<(int X, int Y)>();
            for (int y = 0; y < 250; y++)
            for (int x = 0; x < image.Width; x++)
            {
                (int r, int g, int b) = image.At(x, y);
                if (r + g + b > 75)
                    stars.Add((x, y));
            }

            return stars;
        }

        HashSet<(int X, int Y)> start = Stars(ChaseAt(degrees: 0, name: "stars-0", stops: StarStops)),
            quarter = Stars(ChaseAt(degrees: 90, name: "stars-90", stops: StarStops));

        start.Count.Should().BeGreaterThan(50);
        quarter.Count.Should().BeGreaterThan(50);
        start.Intersect(quarter).Count().Should().BeLessThan(start.Count / 5);
    }

    [Fact]
    public void InEarthsShadowTheFaceOfTheShipThatFacesTheSunGoesDark()
    {
        // Past the terminator, the ship's nose points away from the sun, so its back face, which the chase camera sees,
        // faces it: at 100 degrees the sun still lights it; at 120 degrees Earth's shadow covers the ship.
        (int r, int g, int b) day = ChaseAt(degrees: 100, name: "dusk", stops: SunlitStops).Average(640, 440);
        (int r, int g, int b) night = ChaseAt(degrees: 120, name: "umbra", stops: SunlitStops).Average(640, 440);

        (day.r > 150).Should().BeTrue($"before the shadow, the back face is sunlit, not {day}");
        (night.r + night.g + night.b < 120).Should().BeTrue($"in Earth's shadow, the back face is dark, not {night}");
    }

    [Fact]
    public void TheChaseViewShowsTheShipWithEarthBelowAndTheFarViewShowsTheWholePlanet()
    {
        var script = Script.Parse(
            """
            { "scene": "orbit", "window": { "width": 1280, "height": 720 },
              "steps": [ { "do": "capture", "name": "chase" }, { "do": "camera", "entity": "far" }, { "do": "capture", "name": "far" } ] }
            """
        );
        using World world = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(width: 16, height: 16, exposure: new FixedExposure(SunlitStops));
        try
        {
            ScriptRunner.Run(
                script: script,
                world: world,
                scene: DemoScenes.All.Single(s => s.Name == "orbit").Build(world, renderer),
                outputFolder: folder,
                playback: new HeadlessPlayback(renderer, script.Window, new PngFolder(folder))
            );
        }
        finally
        {
            renderer.Dispose();
            renderer.ValidationMessages.Should().BeEmpty();
        }

        // The scene starts at noon at the March equinox: the sun stands straight above the ship. The chase camera looks at
        // the ship's center from 70 m behind and 12 m above it, so the hull fills the middle: its top faces the sun, and its
        // back face, which the camera sees below the top, is edge-on to it. Earth's horizon, 19.8 degrees below level from
        // 400 km up, crosses the image below the ship.
        var chase = Png.Load(Path.Combine(folder, "chase.png"));
        (int r, int g, int b) top = chase.Average(640, 340);
        (top.r > 150 && Math.Abs(top.r - top.g) < 12 && Math.Abs(top.g - top.b) < 12)
            .Should()
            .BeTrue($"the hull's top is sunlit gray, not {top}");
        (int r, int g, int b) back = chase.Average(640, 440);
        (back.r + back.g + back.b < 120)
            .Should()
            .BeTrue($"the hull's back is edge-on to the sun overhead, so it is dark, not {back}");
        (int r, int g, int b) ground = chase.Average(300, 650);
        (ground.b > ground.r + 40).Should().BeTrue($"Earth below is blue, not {ground}");
        (int r, int g, int b) sky = chase.Average(640, 120);
        (sky.r + sky.g + sky.b < 90).Should().BeTrue($"the sky above is dark, not {sky}");

        // From three Earth radii out, the planet's disc spans 39 degrees of the 60-degree view, around the middle. The sun,
        // at Earth's +X, is 90 degrees from the line of sight, so the right half of the disc is lit and the left half is night.
        var far = Png.Load(Path.Combine(folder, "far.png"));
        (int r, int g, int b) day = far.Average(800, 400);
        (day.b > day.r + 60).Should().BeTrue($"the sunward side of the planet is blue, not {day}");
        (int r, int g, int b) night = far.Average(500, 380);
        (night.r + night.g + night.b < 40).Should().BeTrue($"the night side of the planet is dark, not {night}");
        (int r, int g, int b) space = far.Average(60, 60);
        (space.r + space.g + space.b < 90).Should().BeTrue($"the corner is space, not {space}");
    }
}

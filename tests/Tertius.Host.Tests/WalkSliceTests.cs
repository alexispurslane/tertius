using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The second step of the vertical slice, "a person walks inside the ship while it orbits": one headless run of the
/// checked-in script scripts/walk.json, at the orbit scene's start. The script holds W for 2 s, turns the view a quarter
/// turn left with the mouse, holds W for 2 s again, jumps, and holds W for 2 s more, into the port wall. Every test of the
/// class reads that one run.
/// </summary>
public sealed class WalkRun : IDisposable
{
    public WalkRun()
    {
        Script script = Script.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "walk.json")));
        World = NewJoltWorld();
        VulkanRenderer renderer = GpuTesting.Headless(16, 16);
        try
        {
            Scene = DemoScenes.All.Single(s => s.Name == script.Scene).Build(World, renderer);
            Result = ScriptRunner.Run(script, World, Scene, Folder, new HeadlessPlayback(renderer, script.Window));
        }
        finally
        {
            renderer.Dispose();
            ValidationMessages = renderer.ValidationMessages;
        }
    }

    public World World { get; }
    public SceneInstance Scene { get; }
    public RunResult Result { get; }
    public string Folder { get; } = NewFolder();
    public IReadOnlyList<string> ValidationMessages { get; }

    public Png Capture(string name) => Png.Load(Path.Combine(Folder, name + ".png"));

    public void Dispose()
    {
        World.Dispose();
        Directory.Delete(Folder, recursive: true);
    }
}

[Collection(GpuCollection.Name)]
public sealed class WalkSliceTests(WalkRun run) : IClassFixture<WalkRun>
{
    // The cabin floor is at y = -3.5 m in the ship's axes, and the person's capsule center rests 0.9 m above it. The
    // person starts 5 cm higher.
    private const double Standing = -2.6;

    private Double3 PersonAt(string dump) => run.Result.Dump(dump).Entity("person").Pose.Position;

    private static void ExpectNear(Double3 actual, Double3 expected, double tolerance) =>
        Double3.Distance(actual, expected).Should().BeLessThan(tolerance, $"{actual} is not {expected}");

    [Fact]
    public void ThePersonStandsOnTheCabinFloorInTheShipsFrame()
    {
        FramePose standing = run.Result.Dump("standing").Entity("person").Pose;

        standing.Frame.Should().Be("ship");
        standing.Position.Y.Should().BeApproximately(Standing, 0.005);
        ExpectNear(standing.Position with { Y = 0 }, new Double3(0, 0, 10), 1e-3);
    }

    [Fact]
    public void HoldingWWalksThePersonTowardTheShipsNoseAtWalkingSpeed()
    {
        // 120 ticks are 2 s, at 3 m/s. The key's release reaches the person as late after its press as the press came after
        // the post.
        ExpectNear(PersonAt("walked") - PersonAt("standing"), new Double3(0, 0, -6), 0.01);
    }

    [Fact]
    public void AfterTheMouseTurnsTheViewAQuarterTurnLeftWWalksTowardTheShipsLeftSide()
    {
        ExpectNear(PersonAt("turned") - PersonAt("walked"), new Double3(-6, 0, 0), 0.01);
    }

    [Fact]
    public void AJumpInTheOrbitingShipRisesStraightOffTheDeckAndLandsWhereItLeft()
    {
        Double3 start = PersonAt("turned"), top = PersonAt("jumping");

        // A jump at 4 m/s in 9.81 m/s² peaks 0.41 s later, 0.82 m up. The jump acts in the third tick after its post, so the
        // dump comes after 25 moves. Physics adds each tick's gravity to the velocity before the move, so the person is
        // 1/60 s · (25 · 4 m/s - 9.81 m/s² · (1 + 2 + ... + 25) / 60 s) = 0.781 m up.
        (top.Y - start.Y).Should().BeApproximately(0.781, 0.005);
        ExpectNear(top with { Y = 0 }, start with { Y = 0 }, 0.01);
        ExpectNear(PersonAt("landed"), start, 0.02);
    }

    [Fact]
    public void ThePortWallStopsThePerson()
    {
        Double3 wall = PersonAt("at-wall");

        // 2 s at 3 m/s would take the person 6 m, from x = -6 m to -12 m. The wall's inner face is at x = -9.5 m, and the
        // capsule's radius is 0.3 m.
        wall.X.Should().BeInRange(-9.2, -9.15);
        ExpectNear(wall with { X = 0 }, PersonAt("landed") with { X = 0 }, 0.01);
    }

    [Fact]
    public void TheShipCarriesThePersonTensOfKilometersRoundEarthWhileItWalksAFewMetersInside()
    {
        static Double3 InEarth(StateDump dump)
        {
            FramePose ship = dump.Entity("ship").Pose, person = dump.Entity("person").Pose;
            return ship.Position + ship.Rotation.Rotate(person.Position);
        }
        StateDump first = run.Result.Dump("standing"), last = run.Result.Dump("landed");

        // 387 ticks, 6.45 s, at 7.67 km/s.
        Double3.Distance(InEarth(last), InEarth(first)).Should().BeInRange(49e3, 50e3);
        Double3.Distance(PersonAt("landed"), PersonAt("standing")).Should().BeLessThan(9);
        last.Entity("person").Pose.Frame.Should().Be("ship");
    }

    [Fact]
    public void TheRunReportsNoErrorAndShowsThePersonsView()
    {
        StateDump last = run.Result.Dump("at-wall");

        last.Errors.Should().BeEmpty();
        last.Camera.Should().Be("person");
        run.ValidationMessages.Should().BeEmpty();
    }

    [Fact]
    public void FromInsideTheCabinThePersonSeesALampLitFloorAndTheNoseWallAhead()
    {
        // The person looks along the ship's -Z from 1.65 m above the floor, 39.5 m from the nose wall. The sun stands
        // straight above the ship, but the hull keeps its light out: the lamps light the cabin in their warm white.
        Png cabin = run.Capture("cabin");
        (int r, int g, int b) floor = cabin.Average(640, 650);
        (int r, int g, int b) wall = cabin.Average(640, 360);

        (floor.r > 90 && floor.r > floor.b + 10).Should().BeTrue($"the floor ahead is lamp lit, not {floor}");
        (wall.r > 40 && wall.r > wall.b).Should().BeTrue($"the nose wall is lamp lit, not {wall}");
    }

    [Fact]
    public void AfterTheQuarterTurnThePortWallFillsTheViewWhereTheRoofWasDark()
    {
        // Facing the nose, the top left of the image shows the roof, which the lamps leave dark away from them. After the
        // quarter turn, the person faces the port wall from 3.5 m away, and the wall, lit by the lamps, fills the image.
        (int r, int g, int b) roof = run.Capture("cabin").Average(300, 100);
        (int r, int g, int b) wall = run.Capture("turned").Average(300, 100);

        (roof.r + roof.g + roof.b < 90).Should().BeTrue($"facing the nose, the top left shows the dark roof, not {roof}");
        (wall.r > 80 && wall.r > wall.b).Should().BeTrue($"after the turn, the top left shows the lamp-lit port wall, not {wall}");
    }
}

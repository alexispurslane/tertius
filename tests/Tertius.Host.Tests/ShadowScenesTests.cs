using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The demo scenes of the shadows of every light (docs/plans/local-light-shadows.md, "Part 7" and "Cost"): the lamp
/// halls that the cost measures, with their count, kind and motion of lamps, the scenes of one, two and four stars, and
/// the checked-in script of each scene.
/// </summary>
public sealed class ShadowScenesTests : IDisposable
{
    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private SceneInstance Build(string name) =>
        DemoScenes.All.Single(s => s.Name == name).Build(world, RenderResources.None);

    [Theory]
    [InlineData("lamp-hall-4", 4)]
    [InlineData("lamp-hall-16", 16)]
    [InlineData("lamp-hall-64", 64)]
    [InlineData("lamp-hall-16-moving", 16)]
    [InlineData("lamp-hall-16-hair", 16)]
    public void APointLampHallHasItsCountOfPointLampsThatCastShadows(string name, int count)
    {
        SceneInstance scene = Build(name);

        LightData[] lights = Lamps(scene);

        lights.Should().HaveCount(count);
        lights
            .Should()
            .AllSatisfy(light => light.Value.Should().BeOfType<PointLightData>().Which.CastsShadow.Should().BeTrue());
    }

    [Theory]
    [InlineData("lamp-hall-4-spot", 4)]
    [InlineData("lamp-hall-16-spot", 16)]
    [InlineData("lamp-hall-64-spot", 64)]
    [InlineData("lamp-hall-16-spot-moving", 16)]
    public void ASpotLampHallHasItsCountOfSpotLampsThatTakeOneTileEach(string name, int count)
    {
        SceneInstance scene = Build(name);

        LightData[] lights = Lamps(scene);

        lights.Should().HaveCount(count);
        lights
            .Should()
            .AllSatisfy(light =>
            {
                SpotLightData spot = light.Value.Should().BeOfType<SpotLightData>().Which;
                spot.CastsShadow.Should().BeTrue();
                spot.OuterConeAngle.Should()
                    .BeLessThanOrEqualTo(MathF.PI / 3, "a spot light of up to 60 degrees takes one tile of the atlas");
            });
    }

    [Theory]
    [InlineData("lamp-hall-16-moving", true)]
    [InlineData("lamp-hall-16-spot-moving", true)]
    [InlineData("lamp-hall-16", false)]
    [InlineData("lamp-hall-16-spot", false)]
    public void TheLampsOfAMovingHallMovePastThePillarsAndThoseOfAStillHallStay(string name, bool moving)
    {
        SceneInstance scene = Build(name);
        FrameHandle hall = world.GetFrame(scene["pillar-0"]);
        Double3 start = world.PoseIn(scene["lamp-0"], hall).Position;

        for (int i = 0; i < 10; i++)
            world.Tick();
        double moved = (world.PoseIn(scene["lamp-0"], hall).Position - start).Length;

        if (moving)
            moved.Should().BeGreaterThan(0.1, "every tile of a moving lamp is stale in every frame");
        else
            moved.Should().Be(0, "every tile of a still lamp is cached after the first frame");
    }

    /// <summary>
    /// The hair hall measures a SIMD group that shades hair and other pixels together under many shadowed lamps
    /// (docs/plans/skin-and-hair.md, "Cost"), so its view looks at the wall of hair from close by, with the hall's lamps
    /// all round.
    /// </summary>
    [Fact]
    public void TheHairHallsViewLooksAtItsWallOfHairCardsFromCloseBy()
    {
        SceneInstance scene = Build("lamp-hall-16-hair");
        FrameHandle hall = world.GetFrame(scene["pillar-0"]);
        Double3 eye = world.PoseIn(scene["hair-camera"], hall).Position;

        Entity[] cards =
        [
            .. Enumerable
                .Range(0, int.MaxValue)
                .Select(i => scene.EntityOf($"hair-wall-{i}"))
                .TakeWhile(card => card is not null)
                .Select(card => card!.Value),
        ];

        world.Camera.Should().Be(scene["hair-camera"]);
        scene.Views.Should().Contain("hair-camera");
        cards.Should().HaveCountGreaterThan(50, "the wall of cards fills most of the view");
        cards.Should().AllSatisfy(card => (world.PoseIn(card, hall).Position - eye).Length.Should().BeLessThan(2));
    }

    [Theory]
    [InlineData("stars-1", 1)]
    [InlineData("stars-2", 2)]
    [InlineData("stars-4", 4)]
    public void AStarSceneHasItsCountOfSunsOneAstronomicalUnitAboveThePlane(string name, int count)
    {
        SceneInstance scene = Build(name);
        FrameHandle plane = world.GetFrame(scene["floor"]);

        Entity[] stars = [.. Enumerable.Range(1, count).Select(i => scene[$"star-{i}"])];

        scene.EntityOf($"star-{count + 1}").Should().BeNull();
        stars
            .Should()
            .AllSatisfy(star =>
            {
                Double3 place = world.PoseIn(star, plane).Position;
                place.Length.Should().BeApproximately(DemoScenes.AstronomicalUnit, 1e3);
                place.Y.Should().BePositive("each star stands above the plane");
                world
                    .GetRender(star)
                    .Light.Value.Should()
                    .BeOfType<PointLightData>()
                    .Which.Power.Should()
                    .Be((float)DemoScenes.SunPower);
            });
    }

    /// <summary>
    /// A run checks every step and resolves every name before its first tick: the scene and each camera. The run stops
    /// before its first tick here, so it changes nothing.
    /// </summary>
    [Theory]
    [InlineData("spot-box.json")]
    [InlineData("pillar-room.json")]
    [InlineData("lamp-hall-4.json")]
    [InlineData("lamp-hall-16.json")]
    [InlineData("lamp-hall-64.json")]
    [InlineData("lamp-hall-4-spot.json")]
    [InlineData("lamp-hall-16-spot.json")]
    [InlineData("lamp-hall-64-spot.json")]
    [InlineData("lamp-hall-4-moving.json")]
    [InlineData("lamp-hall-16-moving.json")]
    [InlineData("lamp-hall-64-moving.json")]
    [InlineData("lamp-hall-4-spot-moving.json")]
    [InlineData("lamp-hall-16-spot-moving.json")]
    [InlineData("lamp-hall-64-spot-moving.json")]
    [InlineData("lamp-hall-16-hair.json")]
    [InlineData("stars-1.json")]
    [InlineData("stars-2.json")]
    [InlineData("stars-4.json")]
    public void TheCheckedInScriptOfEachShadowScenePassesTheChecksOfARun(string file)
    {
        var script = Script.Load(Path.Combine(RepositoryRoot(), "scripts", file));
        SceneInstance scene = Build(script.Scene);

        RunResult result = ScriptRunner.Run(
            script: script,
            world: world,
            scene: scene,
            outputFolder: null,
            playback: new StopsAtOnce()
        );

        result.Stopped.Should().BeTrue();
        script
            .Scene.Should()
            .Be(Path.GetFileNameWithoutExtension(file), "each scene of the shadows has its own script");
        script.Steps.Should().Contain(step => step.Value is FilmStep, "the film times the passes for the cost");
    }

    /// <summary>The lights of the lamps <c>lamp-0</c> to the last one of <paramref name="scene"/>.</summary>
    private LightData[] Lamps(SceneInstance scene) =>
        [
            .. Enumerable
                .Range(0, int.MaxValue)
                .Select(i => scene.EntityOf($"lamp-{i}"))
                .TakeWhile(lamp => lamp is not null)
                .Select(lamp => world.GetRender(lamp!.Value).Light),
        ];

    /// <summary>A playback that stops the run before its first tick.</summary>
    private sealed class StopsAtOnce : IPlayback
    {
        public bool WaitForTick() => false;

        public bool Capture(World world, string name) => false;
    }
}

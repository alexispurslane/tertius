using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The review scenes of hair, skin and the screen-space reflections (docs/plans/skin-and-hair.md and
/// docs/plans/screen-space-reflections.md): <c>hair-cards</c>, <c>hair-cards-backlit</c>, <c>hair-bent-cards</c>,
/// <c>hair-rugs</c>, <c>skin-and-hair</c> and <c>reflections</c>. Each scene has the views that its builder names, and
/// each view looks at what the builder says it shows. The checked-in script of each scene that has one passes the checks
/// of a run: <c>hair-bent-cards.json</c>, <c>hair-rugs.json</c>, <c>skin-and-hair.json</c> and <c>reflections.json</c>.
/// The hair card scenes lit from the front and from behind have no script. No test draws, so none needs a GPU.
/// </summary>
public sealed class HairSkinAndReflectionScenesTests : IDisposable
{
    /// <summary>The width of the window of the scenes' scripts divided by its height: 1280 by 720.</summary>
    private const double ScriptAspect = 16.0 / 9;

    private readonly World world = NewJoltWorld();

    public void Dispose() => world.Dispose();

    private SceneInstance Build(string name) =>
        DemoScenes.All.Single(s => s.Name == name).Build(world, RenderResources.None);

    [Theory]
    [InlineData("hair-cards", "front-camera", "back-camera")]
    [InlineData("hair-cards-backlit", "front-camera", "back-camera")]
    [InlineData("hair-bent-cards", "front-camera", "bent-camera")]
    [InlineData("hair-rugs", "rug-camera", "low-camera")]
    [InlineData(
        "skin-and-hair",
        "head-camera",
        "ear-camera",
        "hair-fringed-camera",
        "hair-cutout-camera",
        "hair-glass-camera",
        "hair-camera"
    )]
    [InlineData("reflections", "front-camera", "low-camera", "edge-camera")]
    public void EachReviewSceneHasTheViewsThatItNames(string name, params string[] views)
    {
        SceneInstance scene = Build(name);

        scene.Views.Should().BeEquivalentTo(views);
    }

    /// <summary>
    /// The scripts of the hair-rugs and reflections scenes capture before their first camera step, so their first
    /// pictures show the world's camera that the scene starts with. A script check resolves only the views that a camera
    /// step names.
    /// </summary>
    [Theory]
    [InlineData("hair-rugs", "rug-camera")]
    [InlineData("reflections", "front-camera")]
    public void TheSceneOfAScriptThatCapturesBeforeItsFirstCameraStepStartsInTheViewOfThatCapture(
        string name,
        string view
    )
    {
        SceneInstance scene = Build(name);

        world.Camera.Should().Be(scene[view]);
    }

    [Theory]
    [InlineData("hair-cards", "hair", "standard")]
    [InlineData("hair-cards-backlit", "hair", "standard")]
    [InlineData("hair-bent-cards", "flat", "bent")]
    public void TheFrontViewOfEachHairCardSceneSeesEveryCardOfBothFans(string name, string left, string right)
    {
        SceneInstance scene = Build(name);

        string[] cards = [.. FanCards(left, 5), .. FanCards(right, 5)];

        cards.Should().AllSatisfy(card => InPicture(scene, "front-camera", card).Should().BeTrue(card));
    }

    [Theory]
    [InlineData("hair-cards")]
    [InlineData("hair-cards-backlit")]
    public void TheBackViewOfAHairCardSceneSeesEveryCardFromBehind(string name)
    {
        SceneInstance scene = Build(name);

        string[] cards = [.. FanCards("hair", 5), .. FanCards("standard", 5)];

        Forward(scene, "back-camera").Z.Should().BePositive("the cards face +Z, toward the front view");
        cards.Should().AllSatisfy(card => InPicture(scene, "back-camera", card).Should().BeTrue(card));
    }

    [Theory]
    [InlineData("hair-cards", true)]
    [InlineData("hair-cards-backlit", false)]
    public void TheSunLightsTheFansFromTheFrontInTheHairCardsAndFromBehindInTheBacklitScene(
        string name,
        bool fromTheFront
    )
    {
        SceneInstance scene = Build(name);
        FrameHandle surface = world.GetFrame(scene["floor"]);

        double sun = world.PoseIn(scene["sun"], surface).Position.Z;
        double front = world.PoseIn(scene["front-camera"], surface).Position.Z;

        (Math.Sign(sun) == Math.Sign(front)).Should().Be(fromTheFront, "the fans stand at z = 0, between the views");
    }

    [Fact]
    public void TheBentViewOfTheHairBentCardsSceneSeesEveryBentCard()
    {
        SceneInstance scene = Build("hair-bent-cards");

        FanCards("bent", 5).Should().AllSatisfy(card => InPicture(scene, "bent-camera", card).Should().BeTrue(card));
    }

    [Theory]
    [InlineData("rug-camera")]
    [InlineData("low-camera")]
    public void EachViewOfTheHairRugsSceneSeesBothRugsAndTheGlowingBarBehindThem(string view)
    {
        SceneInstance scene = Build("hair-rugs");

        string[] subjects = ["flat-rug", "tilted-rug", "bar"];

        subjects.Should().AllSatisfy(subject => InPicture(scene, view, subject).Should().BeTrue(subject));
    }

    [Fact]
    public void TheHeadViewOfTheSkinAndHairSceneLooksAlongMinusZAtTheHeadAndItsNose()
    {
        SceneInstance scene = Build("skin-and-hair");

        Double3 forward = Forward(scene, "head-camera");

        forward.Z.Should().BeApproximately(-1, 1e-9);
        InPicture(scene, "head-camera", "head").Should().BeTrue();
        InPicture(scene, "head-camera", "nose").Should().BeTrue();
    }

    [Fact]
    public void TheEarViewOfTheSkinAndHairSceneLooksAtTheEarTowardTheSun()
    {
        SceneInstance scene = Build("skin-and-hair");

        InPicture(scene, "ear-camera", "ear").Should().BeTrue();
        InPicture(scene, "ear-camera", "sun").Should().BeTrue("the ear is lit from behind");
    }

    [Theory]
    [InlineData("hair-fringed")]
    [InlineData("hair-cutout")]
    [InlineData("hair-glass")]
    public void EachFanViewOfTheSkinAndHairSceneSeesEveryCardOfItsFan(string fan)
    {
        SceneInstance scene = Build("skin-and-hair");

        FanCards(fan, 12)
            .Should()
            .AllSatisfy(card => InPicture(scene, $"{fan}-camera", card).Should().BeTrue(card));
    }

    [Fact]
    public void TheHairViewOfTheSkinAndHairSceneSeesTheThreeFansAndThePane()
    {
        SceneInstance scene = Build("skin-and-hair");

        string[] subjects =
        [
            .. FanCards("hair-fringed", 12),
            .. FanCards("hair-cutout", 12),
            .. FanCards("hair-glass", 12),
            "hair-pane",
        ];

        subjects.Should().AllSatisfy(subject => InPicture(scene, "hair-camera", subject).Should().BeTrue(subject));
    }

    [Fact]
    public void TheFrontViewOfTheReflectionsSceneLooksDownAtThirtyDegrees()
    {
        SceneInstance scene = Build("reflections");

        Forward(scene, "front-camera").Y.Should().BeApproximately(-Math.Sin(Math.PI / 6), 1e-9);
    }

    [Fact]
    public void TheEdgeViewOfTheReflectionsSceneLooksLevelAtTheBar()
    {
        SceneInstance scene = Build("reflections");

        Forward(scene, "edge-camera").Y.Should().BeApproximately(0, 1e-9);
        InPicture(scene, "edge-camera", "bar").Should().BeTrue();
    }

    /// <summary>
    /// A run checks every step and resolves every name before its first tick: the scene and each camera. The run stops
    /// before its first tick here, so it changes nothing. The hair-cards and hair-cards-backlit scenes have no script.
    /// </summary>
    [Theory]
    [InlineData("hair-bent-cards.json")]
    [InlineData("hair-rugs.json")]
    [InlineData("skin-and-hair.json")]
    [InlineData("reflections.json")]
    public void TheCheckedInScriptOfEachHairSkinAndReflectionScenePassesTheChecksOfARun(string file)
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
        script.Scene.Should().Be(Path.GetFileNameWithoutExtension(file), "each review scene has its own script");
    }

    /// <summary>The scene ids <c>{fan}-0</c> to <c>{fan}-{count - 1}</c> of the cards of one fan.</summary>
    private static string[] FanCards(string fan, int count) => [.. Enumerable.Range(0, count).Select(i => $"{fan}-{i}")];

    /// <summary>The direction that the view <paramref name="view"/> of <paramref name="scene"/> looks along, in its frame.</summary>
    private Double3 Forward(SceneInstance scene, string view) =>
        world.PoseIn(scene[view], world.GetFrame(scene[view])).TransformDirection(new Double3(0, 0, -1));

    /// <summary>
    /// Whether the center of the entity <paramref name="id"/> of <paramref name="scene"/> lies in the picture of the view
    /// <paramref name="view"/>: in front of it, inside the world's vertical field of view and inside the width of the
    /// scripts' window. A view looks along its own -Z.
    /// </summary>
    private bool InPicture(SceneInstance scene, string view, string id)
    {
        FrameHandle frame = world.GetFrame(scene[view]);
        Double3 seen = world.PoseIn(scene[view], frame).InverseTransformPoint(world.PoseIn(scene[id], frame).Position);
        double halfHeight = Math.Tan(world.VerticalFov / 2) * -seen.Z;
        return seen.Z < 0 && Math.Abs(seen.Y) < halfHeight && Math.Abs(seen.X) < halfHeight * ScriptAspect;
    }

    /// <summary>A playback that stops the run before its first tick.</summary>
    private sealed class StopsAtOnce : IPlayback
    {
        public bool WaitForTick() => false;

        public bool Capture(World world, string name) => false;
    }
}

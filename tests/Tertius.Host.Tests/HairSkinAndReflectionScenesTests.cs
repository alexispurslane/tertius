using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The review scenes of hair, skin and the screen-space reflections (docs/plans/skin-and-hair.md and
/// docs/plans/screen-space-reflections.md): <c>hair-cards</c>, <c>hair-cards-backlit</c>, <c>hair-bent-cards</c>,
/// <c>hair-rugs</c>, <c>skin-and-hair</c> and <c>reflections</c>. Each scene has the views that its builder names, and
/// the views that the tests below check look at what the builder says they show. The scenes with a checked-in script have
/// one of their own name: <c>hair-bent-cards.json</c>, <c>hair-rugs.json</c>, <c>skin-and-hair.json</c> and
/// <c>reflections.json</c>. The hair card scenes lit from the front and from behind have no script.
/// <see cref="CheckedInScriptsTests"/> runs the checks of a run over every checked-in script. No test draws, so none
/// needs a GPU.
/// </summary>
public sealed class HairSkinAndReflectionScenesTests : IDisposable
{
    private readonly World world = NewJoltWorld();

    /// <summary>
    /// The size of the pictures of the scene that <see cref="Build"/> made last: the window of the scene's checked-in
    /// script, or <see cref="ScriptWindow.Default"/> for a scene with no script, which a script with no window key gets.
    /// </summary>
    private ScriptWindow window = ScriptWindow.Default;

    public void Dispose() => world.Dispose();

    private static string ScriptPath(string file) => Path.Combine(RepositoryRoot(), "scripts", file);

    private SceneInstance Build(string name)
    {
        string script = ScriptPath(name + ".json");
        window = File.Exists(script) ? Script.Load(script).Window : ScriptWindow.Default;
        return DemoScenes.All.Single(s => s.Name == name).Build(world, RenderResources.None);
    }

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
    /// The first capture of each script shows the last camera step before it, or the world's camera that the scene starts
    /// with when no camera step comes first, as in hair-rugs.json and reflections.json. That camera is one of the scene's
    /// named views. The checks of a run resolve only the entity that a camera step names, and not the scene's own camera.
    /// </summary>
    [Theory]
    [InlineData("hair-bent-cards.json")]
    [InlineData("hair-rugs.json")]
    [InlineData("skin-and-hair.json")]
    [InlineData("reflections.json")]
    public void TheFirstPictureOfEachScriptShowsANamedViewOfItsOwnScene(string file)
    {
        var script = Script.Load(ScriptPath(file));
        script.Scene.Should().Be(Path.GetFileNameWithoutExtension(file), "each review scene has its own script");
        SceneInstance scene = Build(script.Scene);

        string? stepped = script
            .Steps.Select(step => step.Value)
            .TakeWhile(step => step is not CaptureStep)
            .OfType<CameraStep>()
            .LastOrDefault()
            ?.Entity;
        Entity shown = stepped is null ? world.Camera : scene[stepped];

        scene.Views.Select(view => scene[view]).Should().Contain(shown);
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
    public void TheLowViewOfTheReflectionsSceneLooksAlongTheStripsFromTheirNearEnd()
    {
        SceneInstance scene = Build("reflections");

        Double3 forward = Forward(scene, "low-camera");

        forward.X.Should().BeApproximately(0, 1e-9);
        forward.Z.Should().BeLessThan(-0.99, "the strips run along Z, and the view stands just above them");
        Enumerable
            .Range(0, 6)
            .Select(i => $"strip-{i}")
            .Should()
            .AllSatisfy(strip => InPicture(scene, "low-camera", strip).Should().BeTrue(strip));
    }

    [Fact]
    public void TheEdgeViewOfTheReflectionsSceneLooksLevelAtTheBar()
    {
        SceneInstance scene = Build("reflections");

        Forward(scene, "edge-camera").Y.Should().BeApproximately(0, 1e-9);
        InPicture(scene, "edge-camera", "bar").Should().BeTrue();
    }

    /// <summary>The scene ids <c>{fan}-0</c> to <c>{fan}-{count - 1}</c> of the cards of one fan.</summary>
    private static string[] FanCards(string fan, int count) => [.. Enumerable.Range(0, count).Select(i => $"{fan}-{i}")];

    /// <summary>The direction that the view <paramref name="view"/> of <paramref name="scene"/> looks along, in its frame.</summary>
    private Double3 Forward(SceneInstance scene, string view) =>
        world.PoseIn(scene[view], world.GetFrame(scene[view])).TransformDirection(new Double3(0, 0, -1));

    /// <summary>
    /// Whether the center of the entity <paramref name="id"/> of <paramref name="scene"/> lies in the picture of the view
    /// <paramref name="view"/>: in front of it, inside the world's vertical field of view and inside the width that the
    /// aspect of <see cref="window"/> gives. A view looks along its own -Z.
    /// </summary>
    private bool InPicture(SceneInstance scene, string view, string id)
    {
        FrameHandle frame = world.GetFrame(scene[view]);
        Double3 seen = world.PoseIn(scene[view], frame).InverseTransformPoint(world.PoseIn(scene[id], frame).Position);
        double halfHeight = Math.Tan(world.VerticalFov / 2) * -seen.Z;
        double halfWidth = halfHeight * window.Width / window.Height;
        return seen.Z < 0 && Math.Abs(seen.Y) < halfHeight && Math.Abs(seen.X) < halfWidth;
    }
}

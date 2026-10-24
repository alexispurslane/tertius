using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Physics;
using Tertius.Simulation;
using static Tertius.Testing.WorldTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// FilmScript.Generate: it rehearses a scene on the real World and the real Jolt backend, section by section, and writes
/// each section as a camera step, its posts and one film step whose frames end at the first frame at or after the
/// section's cue, plus its hold. The scene of these tests drops a ball 5 m onto a floor in a root frame with 9.81 m/s²
/// of gravity, and holds a camera and a listener that records every Ping it handles.
/// </summary>
public sealed class FilmScriptTests
{
    private static readonly Scene Drop = new("film-drop", (world, resources) =>
    {
        var scene = new SceneInstance();
        FrameHandle ground = Ground(world, 9.81f);
        Floor(world, ground);
        scene.Add("ball", world.Spawn(new() { Frame = ground, Pose = At(0, 5), Body = Ball() }));
        scene.Add("camera", world.Spawn(new() { Frame = ground, Pose = At(0, 2, 10), Render = new RenderData() }));
        scene.Add("listener", world.Spawn(new() { Frame = ground, Pose = At(5, 1), Components = [new Recorder()] }));
        return scene;
    });

    private static readonly ScriptWindow Window = new(64, 36);

    /// <summary>A cue that holds once the world has run <paramref name="ticks"/> ticks.</summary>
    private static Cue AfterTick(long ticks) => (world, _) => world.TickNumber >= ticks;

    /// <summary>The height of the ball's center above the floor's top, in meters.</summary>
    private static double BallHeight(World world, SceneInstance scene) =>
        world.PoseIn(scene["ball"], world.GetFrame(scene["ball"])).Position.Y;

    private static Script Generate(params FilmSection[] sections) => FilmScript.Generate(Drop, Window, sections);

    [Fact]
    public void ASectionFilmsEveryNthTickUntilItsFirstFrameAtOrAfterItsCue()
    {
        Script script = Generate(new FilmSection(Name: "drop", Camera: "camera", Every: 4, Until: AfterTick(101)));

        // The cue is asked after each frame of 4 ticks: it first holds after tick 104, the 26th frame.
        script.Steps.Should().Equal(new CameraStep("camera"), new FilmStep("drop", 26, 4));
    }

    [Fact]
    public void ASectionWithOutlinesTurnsThemOnBeforeItsCameraStepAndTheNextSectionWithoutThemTurnsThemOff()
    {
        Script script = Generate(
            new FilmSection(Name: "plain", Camera: "camera", Every: 1, Until: FilmSection.Now),
            new FilmSection(Name: "outlined", Camera: "camera", Every: 1, Until: FilmSection.Now, Outlines: true),
            new FilmSection(Name: "still-outlined", Camera: "camera", Every: 1, Until: FilmSection.Now, Outlines: true),
            new FilmSection(Name: "plain-again", Camera: "camera", Every: 1, Until: FilmSection.Now));

        script.Steps.Should().Equal(
            new CameraStep("camera"), new FilmStep("plain", 1, 1),
            new OutlinesStep(true), new CameraStep("camera"), new FilmStep("outlined", 1, 1),
            new CameraStep("camera"), new FilmStep("still-outlined", 1, 1),
            new OutlinesStep(false), new CameraStep("camera"), new FilmStep("plain-again", 1, 1));
    }

    [Fact]
    public void ASectionGoesOnForItsHoldAfterItsCue()
    {
        Script script = Generate(new FilmSection(Name: "drop", Camera: "camera", Every: 4, Until: AfterTick(101), Hold: 10));

        // 104 ticks to the cue, then 10 ticks of hold, which take three more frames of 4 ticks: 116 ticks in all.
        script.Steps[^1].Should().Be((ScriptStep)new FilmStep("drop", 29, 4));
    }

    [Fact]
    public void ASectionStartsWhereTheSectionBeforeItEnded()
    {
        Script script = Generate(
            new FilmSection(Name: "first", Camera: "camera", Every: 4, Until: AfterTick(101)),
            new FilmSection(Name: "second", Camera: "camera", Every: 5, Until: AfterTick(150)));

        // The second section starts after tick 104 and asks its cue after ticks 109, 114 and so on: it first holds after
        // tick 154, its 10th frame.
        script.Steps.Should().Equal(new CameraStep("camera"), new FilmStep("first", 26, 4), new CameraStep("camera"),
            new FilmStep("second", 10, 5));
    }

    [Fact]
    public void ASectionWhoseCueHoldsAtItsStartFilmsOnlyItsHold()
    {
        Script script = Generate(new FilmSection(Name: "still", Camera: "camera", Every: 3, Until: FilmSection.Now, Hold: 30));

        script.Steps[^1].Should().Be((ScriptStep)new FilmStep("still", 10, 3));
    }

    [Fact]
    public void ASectionFilmsAtLeastOneFrame()
    {
        Script script = Generate(new FilmSection(Name: "still", Camera: "camera", Every: 3, Until: FilmSection.Now));

        script.Steps[^1].Should().Be((ScriptStep)new FilmStep("still", 1, 3));
    }

    /// <summary>
    /// A post at the start of a section is routed at the end of the next tick and handled in the tick after that, as in a
    /// run of the script, so a section that waits for its effect films 2 frames of 1 tick.
    /// </summary>
    [Fact]
    public void ASectionPostsItsEventsAtItsStartAsTheRunOfTheScriptDoes()
    {
        var ping = new PostStep(new ToEntity("listener"), "Ping");

        Script script = Generate(
            new FilmSection(Name: "still", Camera: "camera", Every: 1, Until: FilmSection.Now, Hold: 10),
            new FilmSection(Name: "ping", Camera: "camera", Every: 1,
                Until: (world, scene) => world.GetComponent<Recorder>(scene["listener"])!.Seen.Count > 0,
                Posts: [ping]));

        script.Steps.Should().Equal(new CameraStep("camera"), new FilmStep("still", 10, 1), new CameraStep("camera"),
            ping, new FilmStep("ping", 2, 1));
    }

    [Fact]
    public void TheGeneratedScriptHasTheSceneAndTheWindowAndReadsBackFromItsJson()
    {
        Script script = Generate(new FilmSection(Name: "drop", Camera: "camera", Every: 2, Until: AfterTick(10)));

        Script read = Script.Parse(script.ToJson());

        read.Scene.Should().Be("film-drop");
        read.Window.Should().Be(Window);
        read.Steps.Should().Equal(script.Steps);
    }

    /// <summary>
    /// The script reaches each cue at the end of its section in a run of its own: in a new world, built as the command
    /// builds it, the last frame of the drop is the first one with the ball's center below 1 m.
    /// </summary>
    [Fact]
    public void ARunOfTheGeneratedScriptReachesTheCueAtTheLastFrameOfItsSection()
    {
        Script script = Generate(new FilmSection(Name: "drop", Camera: "camera", Every: 1,
            Until: (world, scene) => BallHeight(world, scene) < 1));
        using var world = new World(new() { DisposePhysics = true },
            new JoltPhysicsBackend(JoltBackendOptions.Unpaced));
        SceneInstance scene = Drop.Build(world, RenderResources.None);
        var heights = new List<double>();

        ScriptRunner.Run(script: script, world: world, scene: scene, outputFolder: null,
            playback: new Probe(() => heights.Add(BallHeight(world, scene))));

        heights.Should().HaveCountGreaterThan(30, "the ball falls 4 m in about 0.9 s");
        heights[^1].Should().BeLessThan(1);
        heights[^2].Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ASectionWhoseCueNeverHoldsWithinItsLimitFailsWithItsName()
    {
        long lastAsked = -1;
        Action generate = () => Generate(new FilmSection(Name: "never", Camera: "camera", Every: 1, Until: (world, _) =>
        {
            lastAsked = world.TickNumber;
            return false;
        }, Limit: 30));

        generate.Should().Throw<InvalidOperationException>().WithMessage("*'never'*30 ticks*");
        lastAsked.Should().Be(30, "the generator asks the cue after each tick of its limit of 30, and then gives up");
    }

    /// <summary>A playback that draws nothing and calls <paramref name="captured"/> at each capture.</summary>
    private sealed class Probe(Action captured) : IPlayback
    {
        public bool WaitForTick() => true;

        public bool Capture(World world, string name)
        {
            captured();
            return true;
        }
    }
}

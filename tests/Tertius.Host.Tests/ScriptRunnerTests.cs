using System.Numerics;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// ScriptRunner.Run: steps in file order, ticks in lock-step with the run steps, and posts that reach the entities that
/// their targets name. Each test builds its scene in a world with no physics.
/// </summary>
public class ScriptRunnerTests
{
    private static RunResult Run(World world, SceneInstance scene, params string[] steps) =>
        ScriptRunner.Run(Script.Parse($$"""{ "scene": "test", "steps": [ {{string.Join(", ", steps)}} ] }"""), world, scene);

    private const string NudgeProbe = """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "amount": 1.5 } }""";

    [Fact]
    public void RunStepsAdvanceTheWorldByTheirTicksInFileOrder()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        RunResult result = Run(world, scene,
            """{ "do": "run", "ticks": 3 }""", """{ "do": "dump", "name": "a" }""",
            """{ "do": "run", "ticks": 2 }""", """{ "do": "dump", "name": "b" }""");

        result.Dump("a").Tick.Should().Be(3);
        result.Dump("b").Tick.Should().Be(5);
        world.TickNumber.Should().Be(5, "the run ends after the last step");
    }

    [Fact]
    public void APostedEventReachesItsRecipientOnTheSecondTickAfterThePost()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, NudgeProbe, """{ "do": "run", "ticks": 1 }""");
        ProbeOf(world, scene, "probe").Seen.Should().BeEmpty("the event is routed at the end of the first tick");

        Run(world, scene, """{ "do": "run", "ticks": 1 }""");
        ProbeOf(world, scene, "probe").Seen.Should().ContainSingle().Which.Tick.Should().Be(1, "the second tick of the run is tick 1");
    }

    [Fact]
    public void APostBetweenTwoRunStepsIsPostedAfterTheTicksBeforeIt()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "run", "ticks": 4 }""", NudgeProbe, """{ "do": "run", "ticks": 2 }""");

        ProbeOf(world, scene, "probe").Seen.Should().ContainSingle().Which.Tick.Should().Be(5);
    }

    [Theory]
    [InlineData("""{ "kind": "entity", "id": "probe" }""", new[] { "probe" })]
    [InlineData("""{ "kind": "topic", "name": "test" }""", new[] { "probe" })]
    [InlineData("""{ "kind": "frame", "id": "ship" }""", new[] { "crew" })]
    [InlineData("""{ "kind": "broadcast" }""", new[] { "probe", "bystander", "crew" })]
    public void EachKindOfTargetReachesTheEntitiesThatItNames(string target, string[] reached)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, $$"""{ "do": "post", "to": {{target}}, "event": "Nudge" }""", """{ "do": "run", "ticks": 2 }""");

        string[] all = ["probe", "bystander", "crew"];
        all.Where(id => ProbeOf(world, scene, id).Seen.Count > 0).Should().BeEquivalentTo(reached);
    }

    [Fact]
    public void APayloadIsReadIntoTheFieldsOfItsTypeByTheirCamelCaseNames()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": "mouseMove", "code": 7, "delta": [1.5, -2] } }""",
            """{ "do": "run", "ticks": 2 }""");

        InputEvent input = ProbeOf(world, scene, "probe").Seen.Single().Event.As<InputEvent>();
        input.Kind.Should().Be(InputKind.MouseMove);
        input.Code.Should().Be(7);
        input.Delta.Should().Be(new Vector2(1.5f, -2));
    }

    [Fact]
    public void AnEventIsFoundByTheCSharpNameOfAPayloadTypeInAnyLoadedAssembly()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, NudgeProbe, """{ "do": "run", "ticks": 2 }""");

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Nudge>().Amount.Should().Be(1.5f);
    }

    [Fact]
    public void AnEntityFieldOfAPayloadIsReadFromASceneId()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Tag", "payload": { "other": "crew" } }""",
            """{ "do": "run", "ticks": 2 }""");

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Tag>().Other.Should().Be(scene["crew"]);
    }

    [Fact]
    public void AMissingPayloadPostsTheDefaultPayload()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge" }""", """{ "do": "run", "ticks": 2 }""");

        ProbeOf(world, scene, "probe").Seen.Single().Event.As<Nudge>().Amount.Should().Be(0);
    }

    [Theory]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Shove" }""", "Shove")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "ghost" }, "event": "Nudge" }""", "ghost")]
    [InlineData("""{ "do": "post", "to": { "kind": "frame", "id": "probe" }, "event": "Nudge" }""", "probe")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "force": 1 } }""", "force")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Tag", "payload": { "other": "ghost" } }""", "ghost")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Twin" }""", "Twin")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Label" }""", "Label")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Huge" }""", "Huge")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Fleeting" }""", "Fleeting")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Address", "payload": { "value": 1 } }""", "Address")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Fixed", "payload": { "amount": 5 } }""", "amount")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "amount": 1e39 } }""", "amount")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1e39, 0, 0] } }""", "direction")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1, 2] } }""", "direction")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "InputEvent", "payload": { "kind": 4 } }""", "kind")]
    [InlineData("""{ "do": "camera", "entity": "ghost" }""", "ghost")]
    public void AStepThatCannotRunStopsTheRunBeforeTheFirstTick(string badStep, string named)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Action run = () => Run(world, scene, """{ "do": "run", "ticks": 3 }""", badStep, """{ "do": "run", "ticks": 3 }""");

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain(named);
        world.TickNumber.Should().Be(0);
    }

    [Theory]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "crew" }, "event": "Nudge" }""")]
    [InlineData("""{ "do": "post", "to": { "kind": "frame", "id": "ship" }, "event": "Nudge" }""")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Link", "payload": { "frame": "ship" } }""")]
    [InlineData("""{ "do": "camera", "entity": "crew" }""")]
    public void AStepThatNamesAnEntityThatIsGoneStopsTheRunBeforeTheFirstTick(string step)
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        // The crew dies with the ship: destroying a frame moves its contents to the parent, so destroy the crew first.
        world.Destroy(scene["crew"]);
        world.Destroy(scene["ship"]);

        Action run = () => Run(world, scene, step, """{ "do": "run", "ticks": 1 }""");

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1").And.Contain("not alive");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void AScriptBuiltInCodeIsCheckedLikeAParsedOne()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        var script = new Script("test", [new RunStep(1), new DumpStep("../escape")]);

        Action run = () => ScriptRunner.Run(script, world, scene);

        run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void EveryFieldTypeWithASpecialFormIsReadFromItsScriptForm()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene,
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Aim", "payload": { "direction": [1, 2, 3], "point": [4, 5, 6] } }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Spin", "payload": { "turn": [0, 0, 0.6, 0.8], "attitude": [0, 0.6, 0, 0.8] } }""",
            """{ "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Link", "payload": { "channel": "test", "frame": "ship" } }""",
            """{ "do": "run", "ticks": 2 }""");

        List<(long Tick, Event Event)> seen = ProbeOf(world, scene, "probe").Seen;
        seen.Should().HaveCount(3);
        Aim aim = seen[0].Event.As<Aim>();
        aim.Direction.Should().Be(new Vector3(1, 2, 3));
        aim.Point.Should().Be(new Double3(4, 5, 6));
        Spin spin = seen[1].Event.As<Spin>();
        spin.Turn.Should().Be(new Quaternion(0, 0, 0.6f, 0.8f));
        spin.Attitude.Should().Be(new DQuaternion(0, 0.6, 0, 0.8));
        Link link = seen[2].Event.As<Link>();
        link.Channel.Should().Be(Topics.Get("test"));
        link.Frame.Should().Be(world.GetOwnedFrame(scene["ship"]));
    }

    [Fact]
    public void ACameraStepMakesTheEntityTheWorldsCamera()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        Run(world, scene, """{ "do": "camera", "entity": "crew" }""");

        world.Camera.Should().Be(scene["crew"]);
    }

    [Fact]
    public void ADumpNamesTheCameraThatTheStepsBeforeItChose()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);

        RunResult result = Run(world, scene,
            """{ "do": "dump", "name": "before" }""", """{ "do": "camera", "entity": "crew" }""", """{ "do": "dump", "name": "after" }""");

        result.Dump("before").Camera.Should().BeNull("the probes scene sets no camera");
        result.Dump("after").Camera.Should().Be("crew");
    }

    [Fact]
    public void ACaptureWithoutARendererStopsTheRunBeforeTheFirstTick()
    {
        using World world = NewWorld();
        SceneInstance scene = Probes(world);
        string folder = NewFolder();
        try
        {
            Action run = () => ScriptRunner.Run(Script.Parse("""{ "scene": "test", "steps": [ { "do": "run", "ticks": 1 }, { "do": "capture", "name": "now" } ] }"""), world, scene, folder);

            run.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2").And.Contain("renderer");
            world.TickNumber.Should().Be(0);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

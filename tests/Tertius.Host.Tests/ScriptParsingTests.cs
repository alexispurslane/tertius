using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// Script.Parse: the JSON form of a script, and the scripts that it refuses before anything runs. See
/// docs/plans/host-and-scripted-runs.md, "Script format".
/// </summary>
public class ScriptParsingTests
{
    [Fact]
    public void AScriptReadsItsSceneAndItsStepsInFileOrder()
    {
        Script script = Script.Parse("""
            {
              "scene": "probes",
              "steps": [
                { "do": "run", "ticks": 3 },
                { "do": "post", "to": { "kind": "entity", "id": "probe" }, "event": "Nudge", "payload": { "amount": 2.5 } },
                { "do": "dump", "name": "after-nudge" }
              ]
            }
            """);

        script.Scene.Should().Be("probes");
        script.Steps.Should().HaveCount(3);
        script.Steps[0].Should().Be(new RunStep(3));
        PostStep post = script.Steps[1].Should().BeOfType<PostStep>().Which;
        post.To.Should().Be(new ToEntity("probe"));
        post.Event.Should().Be("Nudge");
        post.Payload!.Value.GetProperty("amount").GetDouble().Should().Be(2.5);
        script.Steps[2].Should().Be(new DumpStep("after-nudge"));
    }

    [Theory]
    [InlineData("""{ "kind": "entity", "id": "probe" }""", "entity")]
    [InlineData("""{ "kind": "topic", "name": "input" }""", "topic")]
    [InlineData("""{ "kind": "frame", "id": "ship" }""", "frame")]
    [InlineData("""{ "kind": "broadcast" }""", "broadcast")]
    public void AnEventTargetIsReadByItsKind(string target, string kind)
    {
        Script script = Script.Parse($$"""{ "scene": "probes", "steps": [ { "do": "post", "to": {{target}}, "event": "Nudge" } ] }""");

        MessageTarget expected = kind switch
        {
            "entity" => new ToEntity("probe"),
            "topic" => new ToTopic("input"),
            "frame" => new ToFrame("ship"),
            _ => new ToEveryone(),
        };
        script.Steps[0].Should().BeOfType<PostStep>().Which.To.Should().Be(expected);
    }

    [Fact]
    public void APostStepWithoutAPayloadHasNone()
    {
        Script script = Script.Parse("""{ "scene": "probes", "steps": [ { "do": "post", "to": { "kind": "broadcast" }, "event": "Nudge" } ] }""");

        script.Steps[0].Should().BeOfType<PostStep>().Which.Payload.Should().BeNull();
    }

    [Theory]
    [InlineData("""{ "steps": [ { "do": "run", "ticks": 1 } ] }""", "scene")]
    [InlineData("""{ "scene": "probes" }""", "steps")]
    [InlineData("""{ "scene": "probes", "steps": [ { "do": "jump" } ] }""", "jump")]
    [InlineData("""{ "scene": "probes", "steps": [ { "ticks": 1 } ] }""", "do")]
    [InlineData("""{ "scene": "probes", "steps": [ { "do": "run", "ticks": 1, "speed": 2 } ] }""", "speed")]
    [InlineData("""{ "scene": "probes", "steps": [ { "do": "run", "ticks": 1 } ], "steps2": [] }""", "steps2")]
    [InlineData("""{ "scene": "probes", "steps": [ { "do": "post", "to": { "kind": "planet", "id": "x" }, "event": "Nudge" } ] }""", "planet")]
    [InlineData("""{ "scene": "probes", "steps": [ { "do": "run", "ticks": "1" } ] }""", "ticks")]
    [InlineData("""{ "scene": "probes", "steps": [ { "do": "run", "ticks": 1, "ticks": 500 } ] }""", "ticks")]
    [InlineData("""{ "scene": "probes", "steps": [ null ] }""", "step 1")]
    public void AScriptWithAMissingUnknownOrMistypedKeyIsRefusedByName(string json, string named)
    {
        Action parse = () => Script.Parse(json);

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain(named);
    }

    [Fact]
    public void AScriptWithACommentIsRefused()
    {
        Action parse = () => Script.Parse("""
            {
              // The content formats are plain JSON, and so are scripts.
              "scene": "probes", "steps": [ { "do": "run", "ticks": 1 } ]
            }
            """);

        parse.Should().Throw<ScriptException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ARunStepOfLessThanOneTickIsRefused(int ticks)
    {
        Action parse = () => Script.Parse($$"""{ "scene": "probes", "steps": [ { "do": "run", "ticks": {{ticks}} } ] }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1");
    }

    [Fact]
    public void TwoDumpsWithOneNameAreRefused()
    {
        Action parse = () => Script.Parse("""
            { "scene": "probes", "steps": [ { "do": "dump", "name": "a" }, { "do": "run", "ticks": 1 }, { "do": "dump", "name": "a" } ] }
            """);

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 3");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Upper")]
    [InlineData("")]
    [InlineData("-leading-dash")]
    public void ADumpNameOutsideTheSceneIdGrammarIsRefused(string name)
    {
        Action parse = () => Script.Parse($$"""{ "scene": "probes", "steps": [ { "do": "dump", "name": "{{name}}" } ] }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1");
    }

    [Fact]
    public void AScriptWithNoStepsIsRefused()
    {
        Action parse = () => Script.Parse("""{ "scene": "probes", "steps": [] }""");

        parse.Should().Throw<ScriptException>();
    }

    [Fact]
    public void TheDoAndKindKeysCanStandAnywhereInTheirObject()
    {
        Script script = Script.Parse("""{ "steps": [ { "ticks": 2, "do": "run" }, { "event": "Nudge", "to": { "id": "probe", "kind": "entity" }, "do": "post" } ], "scene": "probes" }""");

        script.Steps.Should().Equal(new RunStep(2), new PostStep(new ToEntity("probe"), "Nudge"));
    }

    [Fact]
    public void AScriptReadsItsWindowSizeAndItsCaptureAndCameraSteps()
    {
        Script script = Script.Parse("""
            {
              "scene": "lit-box",
              "window": { "width": 640, "height": 360 },
              "steps": [ { "do": "camera", "entity": "away" }, { "do": "capture", "name": "away" } ]
            }
            """);

        script.Window.Should().Be(new ScriptWindow(640, 360));
        script.Steps.Should().Equal(new CameraStep("away"), new CaptureStep("away"));
    }

    [Theory]
    [InlineData(0, 360)]
    [InlineData(640, -1)]
    [InlineData(16385, 360)]
    [InlineData(640, 0)]
    [InlineData(640, 16385)]
    public void AWindowSideOutsideOneTo16384PixelsIsRefused(int width, int height)
    {
        Action parse = () => Script.Parse($$"""{ "scene": "lit-box", "window": { "width": {{width}}, "height": {{height}} }, "steps": [ { "do": "run", "ticks": 1 } ] }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("window");
    }

    [Fact]
    public void TwoCapturesWithOneNameAreRefused()
    {
        Action parse = () => Script.Parse("""
            { "scene": "lit-box", "steps": [ { "do": "capture", "name": "a" }, { "do": "run", "ticks": 1 }, { "do": "capture", "name": "a" } ] }
            """);

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 3");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Upper")]
    public void ACaptureNameOutsideTheSceneIdGrammarIsRefused(string name)
    {
        Action parse = () => Script.Parse($$"""{ "scene": "lit-box", "steps": [ { "do": "capture", "name": "{{name}}" } ] }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1");
    }

    [Fact]
    public void ACaptureAndADumpCanShareANameBecauseTheirFilesDiffer()
    {
        Script script = Script.Parse("""{ "scene": "lit-box", "steps": [ { "do": "capture", "name": "end" }, { "do": "dump", "name": "end" } ] }""");

        script.Steps.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16384, 1)]
    [InlineData(1, 16384)]
    public void AWindowSideOfOneTo16384PixelsIsAccepted(int width, int height)
    {
        Script script = Script.Parse($$"""{ "scene": "lit-box", "window": { "width": {{width}}, "height": {{height}} }, "steps": [ { "do": "run", "ticks": 1 } ] }""");

        script.Window.Should().Be(new ScriptWindow(width, height));
    }
}

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
        script.Steps[0].Should().Be(new ScriptStep(new RunStep(3)));
        PostStep post = script.Steps[1].Value.Should().BeOfType<PostStep>().Which;
        post.To.Should().Be(new MessageTarget(new ToEntity("probe")));
        post.Event.Should().Be("Nudge");
        post.Payload!.Value.GetProperty("amount").GetDouble().Should().Be(2.5);
        script.Steps[2].Should().Be(new ScriptStep(new DumpStep("after-nudge")));
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
        script.Steps[0].Value.Should().BeOfType<PostStep>().Which.To.Should().Be(expected);
    }

    [Fact]
    public void APostStepWithoutAPayloadHasNone()
    {
        Script script = Script.Parse("""{ "scene": "probes", "steps": [ { "do": "post", "to": { "kind": "broadcast" }, "event": "Nudge" } ] }""");

        script.Steps[0].Value.Should().BeOfType<PostStep>().Which.Payload.Should().BeNull();
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

    [Fact]
    public void AnOutlinesStepReadsWhetherItShowsOrHidesTheFrameOutlines()
    {
        Script script = Script.Parse("""{ "scene": "lit-box", "steps": [ { "do": "outlines", "show": true }, { "do": "outlines", "show": false } ] }""");

        script.Steps.Should().Equal(new OutlinesStep(true), new OutlinesStep(false));
    }

    private static string ScriptOf(params string[] steps) => $$"""{ "scene": "lit-box", "steps": [ {{string.Join(", ", steps)}} ] }""";

    private static string Film(string name, int frames, int every) => $$"""{ "do": "film", "name": "{{name}}", "frames": {{frames}}, "every": {{every}} }""";

    private static string Capture(string name) => $$"""{ "do": "capture", "name": "{{name}}" }""";

    [Fact]
    public void AFilmStepReadsItsNameItsNumberOfFramesAndTheTicksBeforeEachFrame()
    {
        Script script = Script.Parse(ScriptOf(Film("climb", 18000, 2)));

        script.Steps.Should().Equal(new FilmStep("climb", 18000, 2));
    }

    [Theory]
    [InlineData(0, 1, "frames")]
    [InlineData(-1, 1, "frames")]
    [InlineData(1_000_001, 1, "frames")]
    [InlineData(1, 0, "every")]
    [InlineData(1, -3, "every")]
    public void AFilmOfFramesOutsideOneToAMillionOrOfLessThanOneTickBeforeEachFrameIsRefused(int frames, int every, string named)
    {
        Action parse = () => Script.Parse(ScriptOf(Film("climb", frames, every)));

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1 (film)").And.Contain(named);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1_000_000, 1)]
    [InlineData(1, int.MaxValue)]
    public void AFilmOfOneToAMillionFramesWithOneTickOrMoreBeforeEachIsAccepted(int frames, int every)
    {
        Script script = Script.Parse(ScriptOf(Film("climb", frames, every)));

        script.Steps.Should().Equal(new FilmStep("climb", frames, every));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Upper")]
    [InlineData("")]
    [InlineData("-leading-dash")]
    public void AFilmNameOutsideTheSceneIdGrammarIsRefused(string name)
    {
        Action parse = () => Script.Parse(ScriptOf(Film(name, 1, 1)));

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1 (film)");
    }

    [Fact]
    public void AFilmNameOfMoreThan57CharactersIsRefusedBecauseItsFrameNamesWouldPassTheSceneIdLimitOf64()
    {
        Action parse = () => Script.Parse(ScriptOf(Film(new string('a', 58), 1, 1)));

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 1 (film)").And.Contain("57");
    }

    [Fact]
    public void AFilmNameOf57CharactersIsAccepted()
    {
        Script script = Script.Parse(ScriptOf(Film(new string('a', 57), 1, 1)));

        script.Steps.Should().Equal(new FilmStep(new string('a', 57), 1, 1));
    }

    [Fact]
    public void TwoFilmsWithOneNameAreRefusedBecauseTheirFramesHaveTheSameNamesAndTheMessageNamesBothSteps()
    {
        Action parse = () => Script.Parse(ScriptOf(Film("climb", 3, 1), """{ "do": "run", "ticks": 1 }""", Film("climb", 1, 5)));

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 3 (film)").And.Contain("step 1").And.Contain("climb");
    }

    [Fact]
    public void ACaptureNamedAfterAFrameOfAnEarlierFilmIsRefusedAndTheMessageNamesBothSteps()
    {
        Action parse = () => Script.Parse(ScriptOf(Film("climb", 3, 1), Capture("climb-000002")));

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2 (capture)").And.Contain("step 1").And.Contain("climb-000002");
    }

    [Fact]
    public void AFilmWithAFrameNamedLikeAnEarlierCaptureIsRefusedAndTheMessageNamesBothSteps()
    {
        Action parse = () => Script.Parse(ScriptOf(Capture("climb-000002"), Film("climb", 3, 1)));

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("step 2 (film)").And.Contain("step 1").And.Contain("climb-000002");
    }

    /// <summary>The film "climb" has 3 frames, <c>climb-000000</c> to <c>climb-000002</c>, and none of these names is one of them.</summary>
    [Theory]
    [InlineData("climb-000003")]
    [InlineData("climb")]
    [InlineData("climb-00001")]
    [InlineData("climb-0000001")]
    [InlineData("climb--00001")]
    [InlineData("climb_000001")]
    [InlineData("climbs-000001")]
    [InlineData("climx-000001")]
    public void ACaptureWhoseNameIsNoFrameOfAFilmStandsBeforeOrAfterIt(string capture)
    {
        Script after = Script.Parse(ScriptOf(Film("climb", 3, 1), Capture(capture)));
        Script before = Script.Parse(ScriptOf(Capture(capture), Film("climb", 3, 1)));

        after.Steps.Should().HaveCount(2);
        before.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void TwoFilmsWithDifferentNamesStandTogetherEvenWhenOneNameIsAFrameNameOfTheOther()
    {
        Script script = Script.Parse(ScriptOf(Film("climb", 3, 1), Film("climb-000001", 3, 1)));

        script.Steps.Should().HaveCount(2);
    }

    [Fact]
    public void AFrameOfAFilmAndADumpCanShareANameBecauseTheirFilesDiffer()
    {
        Script script = Script.Parse(ScriptOf(Film("climb", 3, 1), """{ "do": "dump", "name": "climb-000001" }"""));

        script.Steps.Should().HaveCount(2);
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

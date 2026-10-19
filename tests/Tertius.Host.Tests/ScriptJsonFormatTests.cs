using System.Text.Json;
using System.Text.Json.Nodes;
using Tertius.Host.Scripting;

namespace Tertius.Host.Tests;

/// <summary>
/// The exact JSON that scripts and dumps write for steps and event targets, and the exact words with which the strict
/// reader refuses a bad step or target. They pin the format, so that the way the code reads and writes steps and targets
/// can change without changing a script, a recording or a dump. See docs/plans/host-and-scripted-runs.md, "Script format".
/// </summary>
public class ScriptJsonFormatTests
{
    private static Script ScriptWith(string step) => Script.Parse($$"""{ "scene": "probes", "steps": [ {{step}} ] }""");

    private const string NotValid = "The script is not valid JSON of a script: ";

    [Fact]
    public void ARecordingWritesEachKindOfStepOnOneLineWithItsDoKeyFirst()
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new { amount = 2.5 });
        var script = new Script("probes",
            [
                new RunStep(3),
                new PostStep(new ToEntity("probe"), "Nudge", payload),
                new PostStep(new ToTopic("input"), "Nudge"),
                new PostStep(new ToFrame("ship"), "Nudge"),
                new PostStep(new ToEveryone(), "Nudge"),
                new DumpStep("a"),
                new CaptureStep("b"),
                new CameraStep("c"),
                new OutlinesStep(true),
            ],
            new ScriptWindow(640, 360));

        script.ToJson().Should().Be("""
            {
              "scene": "probes",
              "window": {
                "width": 640,
                "height": 360
              },
              "steps": [
                {"do":"run","ticks":3},
                {"do":"post","to":{"kind":"entity","id":"probe"},"event":"Nudge","payload":{"amount":2.5}},
                {"do":"post","to":{"kind":"topic","name":"input"},"event":"Nudge","payload":null},
                {"do":"post","to":{"kind":"frame","id":"ship"},"event":"Nudge","payload":null},
                {"do":"post","to":{"kind":"broadcast"},"event":"Nudge","payload":null},
                {"do":"dump","name":"a"},
                {"do":"capture","name":"b"},
                {"do":"camera","entity":"c"},
                {"do":"outlines","show":true}
              ]
            }

            """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AWrittenScriptReadsBackAsTheSameSteps()
    {
        var script = new Script("probes",
            [new RunStep(3), new PostStep(new ToEntity("probe"), "Nudge"), new PostStep(new ToTopic("input"), "Nudge"), new PostStep(new ToFrame("ship"), "Nudge"),
             new PostStep(new ToEveryone(), "Nudge"), new DumpStep("a"), new CaptureStep("b"), new CameraStep("c"), new OutlinesStep(true), new OutlinesStep(false)]);

        Script parsed = Script.Parse(script.ToJson());

        parsed.Steps.Should().Equal(script.Steps);
    }

    public static TheoryData<string> CheckedInScripts =>
        [.. Directory.GetFiles(Path.Combine(HostTesting.RepositoryRoot(), "scripts"), "*.json").Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

    // The expected steps come from the raw JSON of the file, never from the types that read it, so the test holds through
    // a change of those types. A post with no payload key writes "payload": null, which reads as the same post.
    [Theory]
    [MemberData(nameof(CheckedInScripts))]
    public void EveryCheckedInScriptWritesBackTheStepsOfItsFileAndReadsBackAsItself(string file)
    {
        string text = File.ReadAllText(Path.Combine(HostTesting.RepositoryRoot(), "scripts", file));
        string written = Script.Parse(text).ToJson();
        JsonArray steps = JsonNode.Parse(text)!["steps"]!.AsArray();
        JsonArray writtenSteps = JsonNode.Parse(written)!["steps"]!.AsArray();

        writtenSteps.Should().HaveSameCount(steps);
        for (int i = 0; i < steps.Count; i++)
        {
            JsonObject step = steps[i]!.AsObject();
            if (step["do"]!.GetValue<string>() == "post" && !step.ContainsKey("payload"))
                step["payload"] = null;
            JsonNode.DeepEquals(writtenSteps[i], step).Should().BeTrue($"step {i + 1} of {file} writes {writtenSteps[i]!.ToJsonString()}, and the file has {step.ToJsonString()}");
        }
        Script.Parse(written).ToJson().Should().Be(written);
    }

    [Theory]
    [InlineData("entity", """
        {
          "tick": 1,
          "event": "Nudge",
          "to": {
            "kind": "entity",
            "id": "crew"
          },
          "from": null,
          "payload": {}
        }
        """)]
    [InlineData("topic", """
        {
          "tick": 1,
          "event": "Nudge",
          "to": {
            "kind": "topic",
            "name": "input"
          },
          "from": null,
          "payload": {}
        }
        """)]
    [InlineData("frame", """
        {
          "tick": 1,
          "event": "Nudge",
          "to": {
            "kind": "frame",
            "id": "ship"
          },
          "from": null,
          "payload": {}
        }
        """)]
    [InlineData("broadcast", """
        {
          "tick": 1,
          "event": "Nudge",
          "to": {
            "kind": "broadcast"
          },
          "from": null,
          "payload": {}
        }
        """)]
    public void ADumpWritesATargetAsItsKindFollowedByItsMembers(string kind, string expected)
    {
        MessageTarget target = kind switch
        {
            "entity" => new ToEntity("crew"),
            "topic" => new ToTopic("input"),
            "frame" => new ToFrame("ship"),
            _ => new ToEveryone(),
        };

        string written = JsonSerializer.Serialize(new EventRecord(1, "Nudge", target, null, new object()), ScriptJson.Dumps(new SceneInstance()));

        written.Should().Be(expected.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ADoKeyWrittenWithAnEscapeStillNamesTheStep()
    {
        // The key is d, a backslash, u006f: the JSON escape of the letter o. (char)92 is the backslash.
        Script script = ScriptWith($$"""{ "d{{(char)92}}u006f": "run", "ticks": 4 }""");

        script.Steps.Should().Equal(new RunStep(4));
    }

    [Fact]
    public void ADoValueMustMatchTheCaseOfTheStepName()
    {
        Action parse = () => ScriptWith("""{ "do": "Run", "ticks": 1 }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Contain("'Run'");
    }

    [Fact]
    public void AMemberKeyMustMatchTheCaseOfItsName()
    {
        Action parse = () => ScriptWith("""{ "do": "run", "Ticks": 1 }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Be(NotValid + "The JSON property 'Ticks' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.RunStep'.");
    }

    [Theory]
    [InlineData("""{ "do": "run", "ticks": 1, "speed": 2 }""", "The JSON property 'speed' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.RunStep'.")]
    [InlineData("""{ "do": "run", "ticks": 1, "ticks": 500 }""", "Duplicate property 'ticks' encountered during deserialization of type 'Tertius.Host.Scripting.RunStep'.")]
    [InlineData("""{ "do": "run" }""", "JSON deserialization for type 'Tertius.Host.Scripting.RunStep' was missing required properties including: 'ticks'.")]
    [InlineData("""{ "do": "camera", "entity": "a", "name": "b" }""", "The JSON property 'name' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.CameraStep'.")]
    [InlineData("""{ "do": "outlines", "show": true, "name": "b" }""", "The JSON property 'name' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.OutlinesStep'.")]
    [InlineData("""{ "do": "outlines" }""", "JSON deserialization for type 'Tertius.Host.Scripting.OutlinesStep' was missing required properties including: 'show'.")]
    [InlineData("""{ "do": "post", "to": { "kind": "broadcast" }, "event": "Nudge", "extra": 1 }""", "The JSON property 'extra' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.PostStep'.")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity" }, "event": "Nudge" }""", "JSON deserialization for type 'Tertius.Host.Scripting.ToEntity' was missing required properties including: 'id'.")]
    [InlineData("""{ "do": "post", "to": { "kind": "broadcast", "id": "x" }, "event": "Nudge" }""", "The JSON property 'id' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.ToEveryone'.")]
    [InlineData("""{ "do": "post", "to": { "kind": "topic", "id": "x" }, "event": "Nudge" }""", "The JSON property 'id' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.ToTopic'.")]
    [InlineData("""{ "do": "post", "to": { "kind": "frame", "name": "x" }, "event": "Nudge" }""", "The JSON property 'name' could not be mapped to any .NET member contained in type 'Tertius.Host.Scripting.ToFrame'.")]
    public void TheMembersOfAStepOrATargetAreReadStrictlyWithTheWordsOfSystemTextJson(string step, string message)
    {
        Action parse = () => ScriptWith(step);

        parse.Should().Throw<ScriptException>().Which.Message.Should().Be(NotValid + message);
    }

    [Theory]
    [InlineData("""{ "do": "run", "ticks": "1" }""", "The JSON value could not be converted to System.Int32. Path: $.ticks | LineNumber: 0 | BytePositionInLine: 59.")]
    [InlineData("""{ "do": "run", "ticks": 1.5 }""", "The JSON value could not be converted to System.Int32. Path: $.ticks | LineNumber: 0 | BytePositionInLine: 59.")]
    [InlineData("""{ "do": "run", "ticks": 99999999999 }""", "The JSON value could not be converted to System.Int32. Path: $.ticks | LineNumber: 0 | BytePositionInLine: 67.")]
    [InlineData("""{ "do": "dump", "name": 5 }""", "The JSON value could not be converted to System.String. Path: $.name | LineNumber: 0 | BytePositionInLine: 57.")]
    [InlineData("""{ "do": "outlines", "show": "yes" }""", "The JSON value could not be converted to System.Boolean. Path: $.show | LineNumber: 0 | BytePositionInLine: 65.")]
    [InlineData("""{ "do": "capture", "name": null }""", "The constructor parameter 'Name' on type 'Tertius.Host.Scripting.CaptureStep' doesn't allow null values. Consider updating its nullability annotation. Path: $.name | LineNumber: 0 | BytePositionInLine: 63.")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": 3 }, "event": "Nudge" }""", "The JSON value could not be converted to System.String. Path: $.id | LineNumber: 0 | BytePositionInLine: 81.")]
    [InlineData("""{ "do": "post", "to": { "kind": "entity", "id": null }, "event": "Nudge" }""", "The constructor parameter 'Id' on type 'Tertius.Host.Scripting.ToEntity' doesn't allow null values. Consider updating its nullability annotation. Path: $.id | LineNumber: 0 | BytePositionInLine: 84.")]
    public void AMistypedMemberIsRefusedWithItsPathAndItsPositionInTheText(string step, string message)
    {
        Action parse = () => ScriptWith(step);

        parse.Should().Throw<ScriptException>().Which.Message.Should().Be(NotValid + message);
    }

    [Theory]
    [InlineData("\"run\"", "The JSON value could not be converted to Tertius.Host.Scripting.ScriptStep. Path: $.steps[0] | LineNumber: 0 | BytePositionInLine: 37.")]
    [InlineData("5", "The JSON value could not be converted to Tertius.Host.Scripting.ScriptStep. Path: $.steps[0] | LineNumber: 0 | BytePositionInLine: 33.")]
    [InlineData("[1]", "The JSON value could not be converted to Tertius.Host.Scripting.ScriptStep. Path: $.steps[0] | LineNumber: 0 | BytePositionInLine: 33.")]
    [InlineData("""{ "do": "post", "to": "x", "event": "Nudge" }""", "The JSON value could not be converted to Tertius.Host.Scripting.MessageTarget. Path: $.to | LineNumber: 0 | BytePositionInLine: 57.")]
    public void AStepOrATargetThatIsNotAnObjectIsRefusedWithItsPathAndItsPositionInTheText(string step, string message)
    {
        Action parse = () => ScriptWith(step);

        parse.Should().Throw<ScriptException>().Which.Message.Should().Be(NotValid + message);
    }

    [Fact]
    public void ABrokenTextInsideAStepIsRefusedWithThePathOfTheStep()
    {
        Action parse = () => ScriptWith("""{ "do": "post", "to": { "kind": "broadcast" }, "event": "Nudge", "payload": { "a": [1, 2 }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Be(NotValid + "'}' is invalid without a matching open. Path: $.steps[0] | LineNumber: 0 | BytePositionInLine: 121.");
    }

    [Fact]
    public void ANullStepIsRefusedAtItsStepNumber()
    {
        Action parse = () => ScriptWith("null");

        parse.Should().Throw<ScriptException>().Which.Message.Should().Be("step 1: a step cannot be null.");
    }
}

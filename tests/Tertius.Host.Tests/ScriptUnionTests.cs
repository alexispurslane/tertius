using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tertius.Host.Scripting;
using Tertius.Simulation;

namespace Tertius.Host.Tests;

/// <summary>
/// The closed sets of the script format and the runner are custom C# 15 unions, as <c>Target</c> is: a script step, an
/// event target, and the runner's step after its checks. See docs/plans/blue-danube-demo.md, "Engine changes for the demo".
/// </summary>
public class ScriptUnionTests
{
    /// <summary>The types of the cases of a custom union: the parameter types of its constructors that take one case.</summary>
    private static string[] CasesOf(Type union) => union
        .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(constructor => constructor.GetParameters().Length == 1)
        .Select(constructor => constructor.GetParameters()[0].ParameterType.Name)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static void ShouldBeACustomUnionOf(Type union, params string[] cases)
    {
        union.IsValueType.Should().BeTrue($"{union.Name} is a struct, like Target");
        union.GetCustomAttribute<UnionAttribute>().Should().NotBeNull($"{union.Name} carries the [Union] attribute");
        CasesOf(union).Should().Equal(cases.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AScriptStepIsACustomUnionOfTheFiveStepRecords() =>
        ShouldBeACustomUnionOf(typeof(ScriptStep), nameof(RunStep), nameof(PostStep), nameof(DumpStep), nameof(CaptureStep), nameof(CameraStep));

    [Fact]
    public void AMessageTargetIsACustomUnionOfTheFourTargetRecords() =>
        ShouldBeACustomUnionOf(typeof(MessageTarget), nameof(ToEntity), nameof(ToTopic), nameof(ToFrame), nameof(ToEveryone));

    [Fact]
    public void TheRunnersReadyStepIsACustomUnionOfItsFiveReadyRecords()
    {
        Type ready = typeof(ScriptRunner).GetNestedType("ReadyStep", BindingFlags.NonPublic)!;

        ShouldBeACustomUnionOf(ready, "ReadyRun", "ReadyPost", "ReadyCamera", "ReadyDump", "ReadyCapture");
    }

    // One sample of every case. A new case without a sample fails the two tests that compare the samples with the cases.
    private static readonly ScriptStep[] StepSamples =
        [new RunStep(3), new PostStep(new ToEntity("probe"), "Nudge"), new DumpStep("a"), new CaptureStep("b"), new CameraStep("c")];

    private static readonly MessageTarget[] TargetSamples = [new ToEntity("probe"), new ToTopic("input"), new ToFrame("ship"), new ToEveryone()];

    public static TheoryData<int> StepSampleNumbers => [.. Enumerable.Range(0, StepSamples.Length)];

    public static TheoryData<int> TargetSampleNumbers => [.. Enumerable.Range(0, TargetSamples.Length)];

    [Fact]
    public void EveryCaseOfTheStepUnionHasASample() =>
        StepSamples.Select(step => step.Value!.GetType().Name).Order(StringComparer.Ordinal).Should().Equal(CasesOf(typeof(ScriptStep)));

    [Fact]
    public void EveryCaseOfTheTargetUnionHasASample() =>
        TargetSamples.Select(target => target.Value!.GetType().Name).Order(StringComparer.Ordinal).Should().Equal(CasesOf(typeof(MessageTarget)));

    [Fact]
    public void AStepHoldsExactlyTheCaseItWasMadeFrom()
    {
        ScriptStep step = new RunStep(3);

        step.HasValue.Should().BeTrue();
        step.TryGetValue(out RunStep? run).Should().BeTrue();
        run!.Ticks.Should().Be(3);
        step.TryGetValue(out PostStep? post).Should().BeFalse();
        post.Should().BeNull();
        step.Value.Should().BeSameAs(run);
    }

    [Fact]
    public void ATargetHoldsExactlyTheCaseItWasMadeFrom()
    {
        MessageTarget target = new ToFrame("ship");

        target.HasValue.Should().BeTrue();
        target.TryGetValue(out ToFrame? frame).Should().BeTrue();
        frame!.Id.Should().Be("ship");
        target.TryGetValue(out ToEntity? entity).Should().BeFalse();
        entity.Should().BeNull();
    }

    [Fact]
    public void ADefaultStepOrTargetHoldsNoCase()
    {
        ScriptStep step = default;
        MessageTarget target = default;

        step.HasValue.Should().BeFalse();
        step.Value.Should().BeNull();
        step.TryGetValue(out RunStep? _).Should().BeFalse();
        target.HasValue.Should().BeFalse();
        target.Value.Should().BeNull();
        target.TryGetValue(out ToEveryone? _).Should().BeFalse();
    }

    [Fact]
    public void AStepOrATargetCannotBeMadeFromANullCase()
    {
        Action step = () => _ = new ScriptStep((RunStep)null!);
        Action target = () => _ = new MessageTarget((ToEveryone)null!);

        step.Should().Throw<ArgumentNullException>();
        target.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StepsAreEqualWhenTheyHoldEqualCases()
    {
        var one = new ScriptStep(new DumpStep("a"));
        var same = new ScriptStep(new DumpStep("a"));
        var other = new ScriptStep(new DumpStep("b"));

        (one == same).Should().BeTrue();
        one.Equals((object)same).Should().BeTrue();
        one.GetHashCode().Should().Be(same.GetHashCode());
        (one != other).Should().BeTrue();
    }

    [Fact]
    public void StepsOfDifferentCasesAreNotEqualEvenWhenTheirTextIsTheSame()
    {
        var dump = new ScriptStep(new DumpStep("a"));
        var capture = new ScriptStep(new CaptureStep("a"));

        (dump == capture).Should().BeFalse();
        dump.Equals((object)capture).Should().BeFalse();
        (dump == default).Should().BeFalse();
        (default(ScriptStep) == default).Should().BeTrue();
    }

    [Fact]
    public void TargetsOfDifferentCasesAreNotEqualEvenWhenTheirTextIsTheSame()
    {
        var entity = new MessageTarget(new ToEntity("a"));
        var frame = new MessageTarget(new ToFrame("a"));

        (entity == frame).Should().BeFalse();
        entity.Equals((object)frame).Should().BeFalse();
        (entity == new MessageTarget(new ToEntity("a"))).Should().BeTrue();
        entity.GetHashCode().Should().Be(new MessageTarget(new ToEntity("a")).GetHashCode());
        (entity == default).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(StepSampleNumbers))]
    public void EachKindOfStepReadsBackAsItselfFromWhatItWrites(int sample)
    {
        ScriptStep step = StepSamples[sample];

        string written = JsonSerializer.Serialize(step, ScriptJson.Recordings);

        JsonSerializer.Deserialize<ScriptStep>(written, ScriptJson.Scripts).Should().Be(step);
    }

    [Theory]
    [MemberData(nameof(TargetSampleNumbers))]
    public void EachKindOfTargetReadsBackAsItselfFromWhatItWrites(int sample)
    {
        MessageTarget target = TargetSamples[sample];

        string written = JsonSerializer.Serialize(target, ScriptJson.Recordings);

        JsonSerializer.Deserialize<MessageTarget>(written, ScriptJson.Scripts).Should().Be(target);
    }

    [Fact]
    public void AStepThatHoldsNoCaseWritesNullAndReadsBackAsNoCase()
    {
        JsonSerializer.Serialize(default(ScriptStep), ScriptJson.Recordings).Should().Be("null");
        JsonSerializer.Serialize(default(MessageTarget), ScriptJson.Recordings).Should().Be("null");
        JsonSerializer.Deserialize<ScriptStep>("null", ScriptJson.Scripts).HasValue.Should().BeFalse();
    }

    [Fact]
    public void AScriptBuiltWithAStepThatHoldsNoCaseIsRefusedAtItsStepNumberBeforeAnythingRuns()
    {
        using World world = HostTesting.NewWorld();
        SceneInstance scene = HostTesting.Probes(world);
        var script = new Script("probes", [new RunStep(1), default]);

        Action run = () => ScriptRunner.Run(script, world, scene);

        run.Should().Throw<ScriptException>().Which.Message.Should().Be("step 2: a step cannot be null.");
        world.TickNumber.Should().Be(0);
    }

    [Fact]
    public void ThePostStepOfANullTargetIsRefusedWhenItIsRead()
    {
        Action parse = () => Script.Parse("""{ "scene": "probes", "steps": [ { "do": "post", "to": null, "event": "Nudge" } ] }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().StartWith("The script is not valid JSON of a script: An event target cannot be null. Path: $.to");
    }

    public static IEnumerable<object[]> OptionsOfTheScriptFormat() =>
    [
        [(Func<JsonSerializerOptions>)(() => ScriptJson.Scripts)],
        [(Func<JsonSerializerOptions>)(() => ScriptJson.Recordings)],
        [(Func<JsonSerializerOptions>)(() => ScriptJson.Dumps(new SceneInstance()))],
        [(Func<JsonSerializerOptions>)(() => ScriptJson.Payloads(HostTesting.NewWorld(), new SceneInstance()))],
    ];

    [Theory]
    [MemberData(nameof(OptionsOfTheScriptFormat))]
    public void EveryOptionsObjectOfTheScriptFormatWritesTheDiscriminatorsFirst(Func<JsonSerializerOptions> options)
    {
        using JsonDocument step = JsonDocument.Parse(JsonSerializer.Serialize<ScriptStep>(new PostStep(new ToTopic("input"), "Nudge"), options()));

        step.RootElement.EnumerateObject().Select(member => member.Name).Should().Equal("do", "to", "event", "payload");
        step.RootElement.GetProperty("to").EnumerateObject().Select(member => member.Name).Should().Equal("kind", "name");
    }

    [Fact]
    public void OptionsThatAreNotOfTheScriptFormatCannotWriteAStepBecauseTheyWouldLeaveOutItsDoKey()
    {
        Action write = () => JsonSerializer.Serialize(new ScriptStep(new RunStep(1)), new JsonSerializerOptions());

        write.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("do");
    }

    [Fact]
    public void OptionsThatAreNotOfTheScriptFormatCannotReadAStepEither()
    {
        Action read = () => JsonSerializer.Deserialize<ScriptStep>("""{ "do": "run", "ticks": 1 }""", new JsonSerializerOptions());

        read.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("do");
    }

    private static Script ScriptWith(string step) => Script.Parse($$"""{ "scene": "probes", "steps": [ {{step}} ] }""");

    private const string NotValid = "The script is not valid JSON of a script: ";
    private const string KindsOfStep = "The kinds of step are run, post, dump, capture and camera.";
    private const string KindsOfTarget = "The kinds of event target are entity, topic, frame and broadcast.";

    [Theory]
    [InlineData("""{ "ticks": 1 }""", "A step needs a \"do\" key. " + KindsOfStep)]
    [InlineData("""{ "do": "jump" }""", "'jump' is not a kind of step. " + KindsOfStep)]
    [InlineData("""{ "do": "", "ticks": 1 }""", "'' is not a kind of step. " + KindsOfStep)]
    [InlineData("""{ "do": 5, "ticks": 1 }""", "The \"do\" key must be a string, such as \"run\".")]
    [InlineData("""{ "do": null, "ticks": 1 }""", "The \"do\" key must be a string, such as \"run\".")]
    [InlineData("""{ "do": "run", "do": "run", "ticks": 1 }""", "The \"do\" key is given twice.")]
    [InlineData("""{ "do": "run", "ticks": 1, "do": "dump" }""", "The \"do\" key is given twice.")]
    public void AStepWithoutAGoodDoKeyIsRefusedWithTheKindsOfStep(string step, string message)
    {
        Action parse = () => ScriptWith(step);

        parse.Should().Throw<ScriptException>().Which.Message.Should().StartWith(NotValid + message);
    }

    [Theory]
    [InlineData("""{ }""", "An event target needs a \"kind\" key. " + KindsOfTarget)]
    [InlineData("""{ "id": "x" }""", "An event target needs a \"kind\" key. " + KindsOfTarget)]
    [InlineData("""{ "kind": "planet", "id": "x" }""", "'planet' is not a kind of event target. " + KindsOfTarget)]
    [InlineData("""{ "kind": "Entity", "id": "x" }""", "'Entity' is not a kind of event target. " + KindsOfTarget)]
    [InlineData("""{ "kind": 3 }""", "The \"kind\" key must be a string, such as \"entity\".")]
    [InlineData("""{ "kind": null }""", "The \"kind\" key must be a string, such as \"entity\".")]
    [InlineData("""{ "kind": "entity", "kind": "entity", "id": "x" }""", "The \"kind\" key is given twice.")]
    public void ATargetWithoutAGoodKindKeyIsRefusedWithTheKindsOfTarget(string target, string message)
    {
        Action parse = () => ScriptWith($$"""{ "do": "post", "to": {{target}}, "event": "Nudge" }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().StartWith(NotValid + message);
    }

    [Fact]
    public void ARefusedDoKeyNamesItsPlaceInTheText()
    {
        Action parse = () => ScriptWith("""{ "do": "jump" }""");

        parse.Should().Throw<ScriptException>().Which.Message.Should().EndWith("Path: $.steps[0] | LineNumber: 0 | BytePositionInLine: 33.");
    }

    [Fact]
    public void TheDoKeyIsFoundBeyondMembersThatHoldObjectsAndArrays()
    {
        Script script = ScriptWith("""{ "event": "Nudge", "payload": { "do": "dump", "deep": [ { "do": "x" }, 1 ] }, "to": { "id": "probe", "kind": "entity" }, "do": "post" }""");

        script.Steps[0].TryGetValue(out PostStep? post).Should().BeTrue();
        post!.Payload!.Value.GetProperty("do").GetString().Should().Be("dump");
    }
}

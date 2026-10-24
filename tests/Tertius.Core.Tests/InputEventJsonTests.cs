using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The JSON form of an input event, which scripts and recordings use: <c>{"kind":"keyDown","code":87}</c> for a key or
/// a mouse button, and <c>{"kind":"mouseMove","delta":[3,-4]}</c> for a move or a scroll. The form belongs to the type, so
/// it does not depend on the options that serialize it.
/// </summary>
public sealed class InputEventJsonTests
{
    // The options of scripts, as far as they could change a payload: camelCase, public fields, and an error for a member that
    // the type does not know.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        IncludeFields = true,
    };

    /// <summary>Every kind, with the JSON that a script writes for it and the event that the JSON stands for.</summary>
    public static TheoryData<string, InputEvent> Forms =>
        new()
        {
            { """{"kind":"keyDown","code":87}""", InputEvent.KeyDown(87) },
            { """{"kind":"keyUp","code":87}""", InputEvent.KeyUp(87) },
            { """{"kind":"mouseDown","code":0}""", InputEvent.MouseDown(0) },
            { """{"kind":"mouseUp","code":1}""", InputEvent.MouseUp(1) },
            {
                """{"kind":"mouseMove","delta":[-628.3185,174.5329]}""",
                InputEvent.MouseMove(new(-628.3185f, 174.5329f))
            },
            { """{"kind":"scroll","delta":[0,-2]}""", InputEvent.Scroll(new(0, -2)) },
        };

    /// <summary>
    /// Posts as the recorder wrote them before the input event was a union: every member of the old struct, with zero for
    /// the member that the kind does not use, and the delta in the form of every other vector of a recording.
    /// </summary>
    public static TheoryData<string, InputEvent> OldRecordedForms =>
        new()
        {
            { """{"kind":"keyDown","code":87,"delta":[0, 0]}""", InputEvent.KeyDown(87) },
            { """{"kind":"keyUp","code":87,"delta":[0, 0]}""", InputEvent.KeyUp(87) },
            { """{"kind":"mouseDown","code":0,"delta":[0, 0]}""", InputEvent.MouseDown(0) },
            { """{"kind":"mouseUp","code":1,"delta":[0, 0]}""", InputEvent.MouseUp(1) },
            { """{"kind":"mouseMove","code":0,"delta":[3, -4]}""", InputEvent.MouseMove(new(3, -4)) },
            { """{"kind":"scroll","code":0,"delta":[0, 1]}""", InputEvent.Scroll(new(0, 1)) },
        };

    private static InputEvent Read(string json) => JsonSerializer.Deserialize<InputEvent>(json, Options);

    private static string Write(InputEvent e) => JsonSerializer.Serialize(e, Options);

    [Theory]
    [MemberData(nameof(Forms))]
    public void EachKindReadsFromTheJsonThatScriptsWrite(string json, InputEvent expected) =>
        (Read(json) == expected).Should().BeTrue();

    [Theory]
    [MemberData(nameof(Forms))]
    public void EachKindWritesTheJsonThatScriptsWrite(string expected, InputEvent e) =>
        JsonNode
            .DeepEquals(JsonNode.Parse(Write(e)), JsonNode.Parse(expected))
            .Should()
            .BeTrue($"{Write(e)} is the same JSON as {expected}");

    [Theory]
    [MemberData(nameof(Forms))]
    public void EachKindWritesNothingBeyondItsOwnMembers(string expected, InputEvent e)
    {
        JsonNode written = JsonNode.Parse(Write(e))!;

        written
            .AsObject()
            .Select(member => member.Key)
            .Order(StringComparer.Ordinal)
            .Should()
            .Equal(JsonNode.Parse(expected)!.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Forms))]
    public void EachKindReadsBackAsTheEventThatWasWritten(string _, InputEvent e) =>
        (Read(Write(e)) == e).Should().BeTrue();

    [Fact]
    public void ADeltaReadsBackToTheLastBit()
    {
        var delta = new Vector2(0.1f, -1f / 3f);

        Vector2 read = Read(Write(InputEvent.MouseMove(delta))) is MouseMoved moved
            ? moved.Delta
            : throw new InvalidOperationException();

        BitConverter.SingleToInt32Bits(read.X).Should().Be(BitConverter.SingleToInt32Bits(0.1f));
        BitConverter.SingleToInt32Bits(read.Y).Should().Be(BitConverter.SingleToInt32Bits(-1f / 3f));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ACodeOfAnyIntegerReadsBack(int code)
    {
        (Read(Write(InputEvent.KeyDown(code))) == InputEvent.KeyDown(code)).Should().BeTrue();
        (Read(Write(InputEvent.MouseUp(code))) == InputEvent.MouseUp(code)).Should().BeTrue();
    }

    [Fact]
    public void TheMembersOfAnEventReadInAnyOrder()
    {
        (Read("""{"code":87,"kind":"keyDown"}""") == InputEvent.KeyDown(87)).Should().BeTrue();
        (Read("""{"delta":[1,2],"kind":"scroll"}""") == InputEvent.Scroll(new(1, 2))).Should().BeTrue();
    }

    [Fact]
    public void WhitespaceBetweenTheTokensOfAnEventDoesNotMatter() => (
            Read(
                """
                {
                  "kind" : "mouseMove",
                  "delta" : [ 1.5 , -2 ]
                }
                """
            ) == InputEvent.MouseMove(new(1.5f, -2))
        ).Should().BeTrue();

    [Fact]
    public void AKindNameReadsWhateverItsCase()
    {
        (Read("""{"kind":"KEYDOWN","code":1}""") == InputEvent.KeyDown(1)).Should().BeTrue();
        (Read("""{"kind":"MouseMove","delta":[1,2]}""") == InputEvent.MouseMove(new(1, 2))).Should().BeTrue();
    }

    [Fact]
    public void TheMemberNamesDoNotFollowTheNamingPolicyOfTheOptions()
    {
        var shouting = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
            IncludeFields = true,
        };

        JsonSerializer.Serialize(InputEvent.KeyDown(87), shouting).Should().Be("""{"kind":"keyDown","code":87}""");
        (JsonSerializer.Deserialize<InputEvent>("""{"kind":"keyDown","code":87}""", shouting) == InputEvent.KeyDown(87))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ADeltaIsWrittenOnOneLineEvenWhenTheOptionsIndent()
    {
        var indented = new JsonSerializerOptions { WriteIndented = true };

        string json = JsonSerializer.Serialize(InputEvent.MouseMove(new(1.5f, -2)), indented);

        json.Should().Contain("""[1.5, -2]""");
    }

    [Fact]
    public void ADeltaWritesNegativeZeroAsZeroSoThatEqualEventsWriteTheSameText()
    {
        Write(InputEvent.Scroll(new(-0f, 0f))).Should().Be(Write(InputEvent.Scroll(Vector2.Zero)));
        Write(InputEvent.Scroll(new(-0f, 1))).Should().Contain("[0, 1]");
    }

    [Fact]
    public void ADeltaThatJsonCannotHoldIsWrittenAsAStringSoThatADumpOfABrokenStateStillWrites() =>
        Write(InputEvent.MouseMove(new(float.NaN, float.PositiveInfinity))).Should().Contain("""["NaN", "Infinity"]""");

    [Fact]
    public void TheEmptyEventIsWrittenAsNull() => Write(default).Should().Be("null");

    [Fact]
    public void AListOfEventsReadsEventByEvent()
    {
        InputEvent[] events = JsonSerializer.Deserialize<InputEvent[]>(
            """[{"kind":"keyDown","code":87},{"kind":"scroll","delta":[1,2]},{"kind":"mouseUp","code":0}]""",
            Options
        )!;

        events.Should().Equal(InputEvent.KeyDown(87), InputEvent.Scroll(new(1, 2)), InputEvent.MouseUp(0));
    }

    [Fact]
    public void AnEventInsideAnObjectReadsAndLeavesTheReaderAtTheNextMember()
    {
        Wrapper read = JsonSerializer.Deserialize<Wrapper>(
            """{"first":{"kind":"keyUp","code":3},"count":5}""",
            Options
        );

        (read.First == InputEvent.KeyUp(3)).Should().BeTrue();
        read.Count.Should().Be(5);
    }

    public struct Wrapper
    {
        public InputEvent First;
        public int Count;
    }

    [Theory]
    [InlineData("""{"kind":"jump","code":1}""", "'jump' is not an input event kind")]
    [InlineData("""{"kind":"","code":1}""", "'' is not an input event kind")]
    [InlineData("""{"kind":4,"delta":[0,0]}""", "kind must be a string")]
    [InlineData("""{"kind":null,"code":1}""", "kind must be a string")]
    [InlineData("""{"kind":["keyDown"],"code":1}""", "kind must be a string")]
    [InlineData("""{"code":1}""", "needs a kind")]
    [InlineData("""{}""", "needs a kind")]
    public void AMissingOrUnknownKindIsRefused(string json, string named)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(named);
    }

    [Theory]
    [InlineData("""{"kind":"keyDown"}""", "A keyDown event needs a code")]
    [InlineData("""{"kind":"keyUp"}""", "A keyUp event needs a code")]
    [InlineData("""{"kind":"mouseDown"}""", "A mouseDown event needs a code")]
    [InlineData("""{"kind":"mouseUp"}""", "A mouseUp event needs a code")]
    [InlineData("""{"kind":"mouseMove"}""", "A mouseMove event needs a delta")]
    [InlineData("""{"kind":"scroll"}""", "A scroll event needs a delta")]
    public void AMissingMemberOfTheKindIsRefused(string json, string named)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(named);
    }

    [Theory]
    [InlineData("""{"kind":"keyDown","code":1,"extra":2}""", "extra")]
    [InlineData("""{"kind":"mouseMove","delta":[1,2],"extra":[]}""", "extra")]
    [InlineData("""{"kind":"keyDown","Code":1}""", "Code")]
    [InlineData("""{"kind":"keyDown","code":1,"Kind":"keyDown"}""", "Kind")]
    [InlineData("""{"kind":"scroll","delta":[1,2],"Delta":[1,2]}""", "Delta")]
    public void AnUnknownMemberIsRefused(string json, string named)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(named);
    }

    // The member of another kind is refused when it holds anything but zero. The zero value is the exception of old
    // recordings, below.
    [Theory]
    [InlineData("""{"kind":"keyDown","code":1,"delta":[0,0.5]}""", "A keyDown event has a code, not a delta")]
    [InlineData("""{"kind":"mouseUp","code":1,"delta":[-1,0]}""", "A mouseUp event has a code, not a delta")]
    [InlineData("""{"kind":"mouseMove","delta":[0,0],"code":1}""", "A mouseMove event has a delta, not a code")]
    [InlineData("""{"code":1,"kind":"scroll","delta":[0,0]}""", "A scroll event has a delta, not a code")]
    [InlineData("""{"kind":"scroll","delta":[1,2],"code":-1}""", "A scroll event has a delta, not a code")]
    public void AMemberOfAnotherKindIsRefused(string json, string named)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(named);
    }

    [Theory]
    [MemberData(nameof(OldRecordedForms))]
    public void APostThatTheRecorderWroteBeforeTheUnionReadsAsItsKind(string json, InputEvent expected) =>
        Read(json).Should().Be(expected);

    [Theory]
    [InlineData("""{"kind":"keyDown","code":1,"code":2}""", "The code is given twice")]
    [InlineData("""{"kind":"keyDown","kind":"keyUp","code":2}""", "The kind is given twice")]
    [InlineData("""{"kind":"scroll","delta":[1,2],"delta":[3,4]}""", "The delta is given twice")]
    public void AMemberThatIsGivenTwiceIsRefused(string json, string named)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain(named);
    }

    [Theory]
    [InlineData("""{"kind":"keyDown","code":87.5}""")]
    [InlineData("""{"kind":"keyDown","code":"87"}""")]
    [InlineData("""{"kind":"keyDown","code":null}""")]
    [InlineData("""{"kind":"keyDown","code":true}""")]
    [InlineData("""{"kind":"keyDown","code":[87]}""")]
    [InlineData("""{"kind":"mouseDown","code":2147483648}""")]
    public void ACodeThatIsNotAnIntegerIsRefused(string json)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain("The code must be an integer");
    }

    [Theory]
    [InlineData("""{"kind":"mouseMove","delta":[1]}""")]
    [InlineData("""{"kind":"mouseMove","delta":[1,2,3]}""")]
    [InlineData("""{"kind":"mouseMove","delta":[]}""")]
    [InlineData("""{"kind":"mouseMove","delta":1}""")]
    [InlineData("""{"kind":"mouseMove","delta":null}""")]
    [InlineData("""{"kind":"mouseMove","delta":{"x":1,"y":2}}""")]
    [InlineData("""{"kind":"scroll","delta":["1","2"]}""")]
    [InlineData("""{"kind":"scroll","delta":[1,null]}""")]
    public void ADeltaThatIsNotAnArrayOfTwoNumbersIsRefused(string json)
    {
        Action read = () => Read(json);

        read.Should()
            .Throw<JsonException>()
            .Which.Message.Should()
            .Contain("Expected the delta, an array of 2 numbers");
    }

    [Theory]
    [InlineData("""{"kind":"scroll","delta":[1e39,0]}""")]
    [InlineData("""{"kind":"scroll","delta":[0,-1e39]}""")]
    public void ADeltaWithANumberThatRoundsToInfinityIsRefused(string json)
    {
        Action read = () => Read(json);

        read.Should()
            .Throw<JsonException>()
            .Which.Message.Should()
            .Contain("The number is out of the range of a float");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""[{"kind":"keyDown","code":1}]""")]
    [InlineData("5")]
    [InlineData("\"keyDown\"")]
    [InlineData("true")]
    public void AValueThatIsNotAnObjectIsRefused(string json)
    {
        Action read = () => Read(json);

        read.Should().Throw<JsonException>().Which.Message.Should().Contain("Expected an input event");
    }
}

using System.Numerics;
using System.Runtime.CompilerServices;
using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The input that the host sends to the input topic: a key or a mouse button that went down or up, a mouse that moved, or
/// a scroll. The value holds only the fields of its own case, so a key code and a mouse movement can never be confused.
/// </summary>
public class InputEventTests
{
    private const int KeyW = 87;
    private static readonly Vector2 Moved = new(3, -4);

    /// <summary>One event of every case, with the case that it must read back as.</summary>
    public static TheoryData<InputEvent, object> EveryCase => new()
    {
        { InputEvent.KeyDown(KeyW), new KeyChanged(KeyW, true) },
        { InputEvent.KeyUp(KeyW), new KeyChanged(KeyW, false) },
        { InputEvent.MouseDown(1), new MouseButtonChanged(1, true) },
        { InputEvent.MouseUp(1), new MouseButtonChanged(1, false) },
        { InputEvent.MouseMove(Moved), new MouseMoved(Moved) },
        { InputEvent.Scroll(Moved), new Scrolled(Moved) },
    };

    private static int CasesHeld(InputEvent e) =>
        (e.TryGetValue(out KeyChanged _) ? 1 : 0)
        + (e.TryGetValue(out MouseButtonChanged _) ? 1 : 0)
        + (e.TryGetValue(out MouseMoved _) ? 1 : 0)
        + (e.TryGetValue(out Scrolled _) ? 1 : 0);

    [Fact]
    public void TheDefaultInputEventHoldsNoCase()
    {
        InputEvent none = default;

        none.HasValue.Should().BeFalse();
        none.TryGetValue(out KeyChanged key).Should().BeFalse();
        none.TryGetValue(out MouseButtonChanged button).Should().BeFalse();
        none.TryGetValue(out MouseMoved moved).Should().BeFalse();
        none.TryGetValue(out Scrolled scrolled).Should().BeFalse();
        key.Should().Be(default(KeyChanged));
        button.Should().Be(default(MouseButtonChanged));
        moved.Should().Be(default(MouseMoved));
        scrolled.Should().Be(default(Scrolled));
        none.Value.Should().BeNull();
        none.ToString().Should().Be("InputEvent(none)");
    }

    [Fact]
    public void AKeyThatWentDownReadsBackAsItsCodeAndDown()
    {
        InputEvent e = InputEvent.KeyDown(KeyW);

        e.HasValue.Should().BeTrue();
        e.TryGetValue(out KeyChanged key).Should().BeTrue();
        key.Should().Be(new KeyChanged(KeyW, IsDown: true));
        (e is KeyChanged { Code: var code, IsDown: var down } ? (code, down) : default).Should().Be((KeyW, true), "a pattern match reads the same case");
    }

    [Fact]
    public void AKeyThatWentUpReadsBackAsItsCodeAndNotDown()
    {
        InputEvent e = InputEvent.KeyUp(KeyW);

        e.TryGetValue(out KeyChanged key).Should().BeTrue();
        key.Should().Be(new KeyChanged(KeyW, IsDown: false));
    }

    [Fact]
    public void AMouseButtonThatWentDownReadsBackAsItsCodeAndDown()
    {
        InputEvent e = InputEvent.MouseDown(2);

        e.HasValue.Should().BeTrue();
        e.TryGetValue(out MouseButtonChanged button).Should().BeTrue();
        button.Should().Be(new MouseButtonChanged(2, IsDown: true));
        (e is MouseButtonChanged { Code: var code, IsDown: var down } ? (code, down) : default).Should().Be((2, true), "a pattern match reads the same case");
    }

    [Fact]
    public void AMouseButtonThatWentUpReadsBackAsItsCodeAndNotDown()
    {
        InputEvent e = InputEvent.MouseUp(2);

        e.TryGetValue(out MouseButtonChanged button).Should().BeTrue();
        button.Should().Be(new MouseButtonChanged(2, IsDown: false));
    }

    [Fact]
    public void AMouseMoveReadsBackAsItsDelta()
    {
        InputEvent e = InputEvent.MouseMove(Moved);

        e.HasValue.Should().BeTrue();
        e.TryGetValue(out MouseMoved moved).Should().BeTrue();
        moved.Delta.Should().Be(Moved);
        (e is MouseMoved { Delta: var delta } ? delta : default).Should().Be(Moved, "a pattern match reads the same case");
    }

    [Fact]
    public void AScrollReadsBackAsItsDelta()
    {
        InputEvent e = InputEvent.Scroll(Moved);

        e.HasValue.Should().BeTrue();
        e.TryGetValue(out Scrolled scrolled).Should().BeTrue();
        scrolled.Delta.Should().Be(Moved);
        (e is Scrolled { Delta: var delta } ? delta : default).Should().Be(Moved, "a pattern match reads the same case");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnInputEventHoldsExactlyOneCaseAndBoxesIt(InputEvent e, object expectedCase)
    {
        CasesHeld(e).Should().Be(1);
        e.Value.Should().Be(expectedCase);
    }

    [Fact]
    public void AKeyAndAMouseButtonWithTheSameCodeAreKeptApart()
    {
        InputEvent key = InputEvent.KeyDown(1);
        InputEvent button = InputEvent.MouseDown(1);

        key.TryGetValue(out MouseButtonChanged keyAsButton).Should().BeFalse("a key is not a mouse button");
        button.TryGetValue(out KeyChanged buttonAsKey).Should().BeFalse("a mouse button is not a key");
        keyAsButton.Should().Be(default(MouseButtonChanged), "the code and the flag of a key do not leak out as a button");
        buttonAsKey.Should().Be(default(KeyChanged), "the code and the flag of a button do not leak out as a key");
    }

    [Fact]
    public void AMouseMoveAndAScrollWithTheSameDeltaAreKeptApart()
    {
        InputEvent move = InputEvent.MouseMove(Moved);
        InputEvent scroll = InputEvent.Scroll(Moved);

        move.TryGetValue(out Scrolled moveAsScroll).Should().BeFalse("a mouse move is not a scroll");
        scroll.TryGetValue(out MouseMoved scrollAsMove).Should().BeFalse("a scroll is not a mouse move");
        moveAsScroll.Should().Be(default(Scrolled), "the delta of a move does not leak out as a scroll");
        scrollAsMove.Should().Be(default(MouseMoved), "the delta of a scroll does not leak out as a move");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void TheViewOfEveryOtherCaseIsEmpty(InputEvent e, object _)
    {
        bool isKey = e.TryGetValue(out KeyChanged key);
        bool isButton = e.TryGetValue(out MouseButtonChanged button);
        bool isMove = e.TryGetValue(out MouseMoved moved);
        bool isScroll = e.TryGetValue(out Scrolled scrolled);

        if (!isKey)
            key.Should().Be(default(KeyChanged));
        if (!isButton)
            button.Should().Be(default(MouseButtonChanged));
        if (!isMove)
            moved.Should().Be(default(MouseMoved));
        if (!isScroll)
            scrolled.Should().Be(default(Scrolled));
    }

    [Fact]
    public void AnInputEventBuiltFromACaseIsTheOneThatTheFactoryBuilds()
    {
        InputEvent viaConstructor = new(new KeyChanged(KeyW, true));
        InputEvent viaKey = new KeyChanged(KeyW, true);
        InputEvent viaButton = new MouseButtonChanged(1, false);
        InputEvent viaMove = new MouseMoved(Moved);
        InputEvent viaScroll = new Scrolled(Moved);

        (viaConstructor == InputEvent.KeyDown(KeyW)).Should().BeTrue();
        (viaKey == InputEvent.KeyDown(KeyW)).Should().BeTrue();
        (viaButton == InputEvent.MouseUp(1)).Should().BeTrue();
        (viaMove == InputEvent.MouseMove(Moved)).Should().BeTrue();
        (viaScroll == InputEvent.Scroll(Moved)).Should().BeTrue();
    }

    [Fact]
    public void AKeyWithCodeZeroIsStillAKeyAndNotTheEmptyEvent()
    {
        InputEvent.KeyDown(0).HasValue.Should().BeTrue();
        InputEvent.MouseUp(0).HasValue.Should().BeTrue();
        InputEvent.MouseMove(Vector2.Zero).HasValue.Should().BeTrue();
        InputEvent.Scroll(Vector2.Zero).HasValue.Should().BeTrue();
        (InputEvent.KeyDown(0) == default).Should().BeFalse();
        (InputEvent.MouseMove(Vector2.Zero) == default).Should().BeFalse();
    }

    [Fact]
    public void EventsOfTheSameCaseAndFieldsAreEqualAndHashTheSame()
    {
        InputEvent.KeyDown(KeyW).Equals(InputEvent.KeyDown(KeyW)).Should().BeTrue();
        InputEvent.KeyDown(KeyW).Equals((object)InputEvent.KeyDown(KeyW)).Should().BeTrue();
        (InputEvent.KeyDown(KeyW) != InputEvent.KeyDown(KeyW)).Should().BeFalse();
        InputEvent.KeyDown(KeyW).GetHashCode().Should().Be(InputEvent.KeyDown(KeyW).GetHashCode());
        InputEvent.MouseUp(1).GetHashCode().Should().Be(InputEvent.MouseUp(1).GetHashCode());
        InputEvent.MouseMove(Moved).GetHashCode().Should().Be(InputEvent.MouseMove(Moved).GetHashCode());
        InputEvent.Scroll(Moved).GetHashCode().Should().Be(InputEvent.Scroll(Moved).GetHashCode());
        default(InputEvent).GetHashCode().Should().Be(default(InputEvent).GetHashCode());
        (default(InputEvent) == default).Should().BeTrue();
    }

    [Fact]
    public void EventsThatDifferInTheirCodeTheirFlagOrTheirDeltaAreNotEqual()
    {
        (InputEvent.KeyDown(KeyW) == InputEvent.KeyDown(KeyW + 1)).Should().BeFalse("the key code differs");
        (InputEvent.KeyDown(KeyW) == InputEvent.KeyUp(KeyW)).Should().BeFalse("down is not up");
        (InputEvent.MouseDown(1) == InputEvent.MouseDown(2)).Should().BeFalse("the button differs");
        (InputEvent.MouseDown(1) == InputEvent.MouseUp(1)).Should().BeFalse("down is not up");
        (InputEvent.MouseMove(Moved) == InputEvent.MouseMove(new Vector2(3, 4))).Should().BeFalse("the delta differs");
        (InputEvent.MouseMove(Moved) == InputEvent.MouseMove(new Vector2(-4, 3))).Should().BeFalse("the delta differs");
        (InputEvent.Scroll(Moved) == InputEvent.Scroll(new Vector2(3, 4))).Should().BeFalse("the delta differs");
        (InputEvent.Scroll(Moved) != InputEvent.Scroll(new Vector2(3, 4))).Should().BeTrue();
    }

    [Fact]
    public void EventsOfDifferentCasesAreNotEqualWhateverTheirFieldsAre()
    {
        (InputEvent.KeyDown(1) == InputEvent.MouseDown(1)).Should().BeFalse("a key is not a mouse button");
        (InputEvent.KeyUp(1) == InputEvent.MouseUp(1)).Should().BeFalse("a key is not a mouse button");
        (InputEvent.MouseMove(Moved) == InputEvent.Scroll(Moved)).Should().BeFalse("a mouse move is not a scroll");
        (InputEvent.KeyDown(0) == default).Should().BeFalse();
        (InputEvent.MouseMove(Vector2.Zero) == InputEvent.Scroll(Vector2.Zero)).Should().BeFalse();
        (InputEvent.MouseMove(Vector2.Zero) == InputEvent.KeyUp(0)).Should().BeFalse();
        InputEvent.KeyDown(1).Equals((object)InputEvent.MouseDown(1)).Should().BeFalse();
        InputEvent.KeyDown(1).Equals("keyDown").Should().BeFalse("an object that is not an input event is never equal");
    }

    [Fact]
    public void AnEventThatHoldsANaNEqualsItselfAndAnyCopyAndHashesTheSame()
    {
        InputEvent nan = InputEvent.MouseMove(new Vector2(float.NaN, 1));
        InputEvent copy = nan;

        (nan == copy).Should().BeTrue("equality must agree with the hash, and NaN equals NaN there");
        nan.GetHashCode().Should().Be(copy.GetHashCode());
    }

    [Theory]
    [InlineData("KeyDown", "InputEvent(KeyChanged { Code = 87, IsDown = True })")]
    [InlineData("KeyUp", "InputEvent(KeyChanged { Code = 87, IsDown = False })")]
    [InlineData("MouseDown", "InputEvent(MouseButtonChanged { Code = 87, IsDown = True })")]
    [InlineData("MouseUp", "InputEvent(MouseButtonChanged { Code = 87, IsDown = False })")]
    public void TheTextOfAKeyOrButtonEventNamesItsCase(string factory, string expected)
    {
        InputEvent e = factory switch
        {
            "KeyDown" => InputEvent.KeyDown(87),
            "KeyUp" => InputEvent.KeyUp(87),
            "MouseDown" => InputEvent.MouseDown(87),
            _ => InputEvent.MouseUp(87),
        };

        e.ToString().Should().Be(expected);
    }

    [Fact]
    public void TheTextOfAMoveAndOfAScrollNamesTheirCase()
    {
        InputEvent.MouseMove(new Vector2(1, 2)).ToString().Should().Be($"InputEvent(MouseMoved {{ Delta = {new Vector2(1, 2)} }})");
        InputEvent.Scroll(new Vector2(1, 2)).ToString().Should().Be($"InputEvent(Scrolled {{ Delta = {new Vector2(1, 2)} }})");
    }

    [Fact]
    public void AnInputEventIsUnmanagedAndFitsInAPayload()
    {
        RuntimeHelpers.IsReferenceOrContainsReferences<InputEvent>().Should().BeFalse("a payload is copied as raw bytes, so it can hold no reference");
        Unsafe.SizeOf<InputEvent>().Should().BeLessThanOrEqualTo(EventTypeRegistry.MaxPayloadSize);
        EventTypeRegistry.Register<InputEvent>().Should().BeGreaterThanOrEqualTo(0, "registering a payload that is too large throws");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void AnInputEventSurvivesBeingCopiedIntoAnEventAndBack(InputEvent sent, object _)
    {
        Event e = Event.Create(Topics.Input, Entity.Null, sent);

        e.Is<InputEvent>().Should().BeTrue();
        (e.As<InputEvent>() == sent).Should().BeTrue();
    }
}

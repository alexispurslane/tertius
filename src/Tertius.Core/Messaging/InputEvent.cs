using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Tertius;

/// <summary>The case of an <see cref="InputEvent"/> in which a key went down or up.</summary>
/// <param name="Code">The key: the integer value of a Silk.NET <c>Key</c>. Core has no reference to Silk.NET, so it keeps the number.</param>
/// <param name="IsDown">True when the key went down, false when it went up.</param>
public readonly record struct KeyChanged(int Code, bool IsDown);

/// <summary>The case in which a mouse button went down or up.</summary>
/// <param name="Code">The button: the integer value of a Silk.NET <c>MouseButton</c>.</param>
/// <param name="IsDown">True when the button went down, false when it went up.</param>
public readonly record struct MouseButtonChanged(int Code, bool IsDown);

/// <summary>The case in which the mouse moved.</summary>
/// <param name="Delta">The movement since the last event of this case, in pixels: X to the right, Y down.</param>
public readonly record struct MouseMoved(Vector2 Delta);

/// <summary>The case in which the wheel or the pad scrolled.</summary>
/// <param name="Delta">The scroll amount: X along the horizontal axis, Y along the vertical axis.</param>
public readonly record struct Scrolled(Vector2 Delta);

/// <summary>
/// The host sends input to <see cref="Topics.Input"/>: a key or a mouse button that went down or up
/// (<see cref="KeyChanged"/>, <see cref="MouseButtonChanged"/>), a mouse that moved (<see cref="MouseMoved"/>) or a
/// scroll (<see cref="Scrolled"/>). An input event is a payload, so it is unmanaged and fits
/// <see cref="EventTypeRegistry.MaxPayloadSize"/>.
/// </summary>
/// <remarks>
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler pattern-match
/// it without boxing. <see cref="Value"/> boxes, so use it only for debugging. The default value holds no case: nothing
/// sends it, and a component that matches the cases ignores it. The key case and the button case share their code and
/// their flag, and the move case and the scroll case share their delta. A case reads only its own fields, so the fields
/// of the cases that an event does not hold are zero and never matter.
/// Scripts and recordings write an input event as JSON in the form of <see cref="InputEventJsonConverter"/>.
/// </remarks>
[Union]
[JsonConverter(typeof(InputEventJsonConverter))]
public readonly struct InputEvent : IEventPayload, IEquatable<InputEvent>
{
    private const byte KindNone = 0, KindKey = 1, KindButton = 2, KindMove = 3, KindScroll = 4;

    private readonly byte kind;
    /// <summary>Whether the key or the button of <see cref="KeyChanged"/> and of <see cref="MouseButtonChanged"/> went down.</summary>
    private readonly bool isDown;
    /// <summary>The key or the button of <see cref="KeyChanged"/> and of <see cref="MouseButtonChanged"/>.</summary>
    private readonly int code;
    /// <summary>The movement or the scroll amount of <see cref="MouseMoved"/> and of <see cref="Scrolled"/>.</summary>
    private readonly Vector2 delta;

    public InputEvent(KeyChanged key) { kind = KindKey; code = key.Code; isDown = key.IsDown; }
    public InputEvent(MouseButtonChanged button) { kind = KindButton; code = button.Code; isDown = button.IsDown; }
    public InputEvent(MouseMoved moved) { kind = KindMove; delta = moved.Delta; }
    public InputEvent(Scrolled scrolled) { kind = KindScroll; delta = scrolled.Delta; }

    public static implicit operator InputEvent(KeyChanged key) => new(key);
    public static implicit operator InputEvent(MouseButtonChanged button) => new(button);
    public static implicit operator InputEvent(MouseMoved moved) => new(moved);
    public static implicit operator InputEvent(Scrolled scrolled) => new(scrolled);

    /// <summary>The key with the Silk.NET <c>Key</c> value <paramref name="code"/> went down.</summary>
    public static InputEvent KeyDown(int code) => new(new KeyChanged(code, true));

    /// <summary>The key with the Silk.NET <c>Key</c> value <paramref name="code"/> went up.</summary>
    public static InputEvent KeyUp(int code) => new(new KeyChanged(code, false));

    /// <summary>The mouse button with the Silk.NET <c>MouseButton</c> value <paramref name="button"/> went down.</summary>
    public static InputEvent MouseDown(int button) => new(new MouseButtonChanged(button, true));

    /// <summary>The mouse button with the Silk.NET <c>MouseButton</c> value <paramref name="button"/> went up.</summary>
    public static InputEvent MouseUp(int button) => new(new MouseButtonChanged(button, false));

    /// <summary>The mouse moved by <paramref name="delta"/> pixels.</summary>
    public static InputEvent MouseMove(Vector2 delta) => new(new MouseMoved(delta));

    /// <summary>The scroll moved by <paramref name="delta"/>.</summary>
    public static InputEvent Scroll(Vector2 delta) => new(new Scrolled(delta));

    /// <summary>False for the default value, which holds no case.</summary>
    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out KeyChanged value)
    {
        value = kind == KindKey ? new KeyChanged(code, isDown) : default;
        return kind == KindKey;
    }

    public bool TryGetValue(out MouseButtonChanged value)
    {
        value = kind == KindButton ? new MouseButtonChanged(code, isDown) : default;
        return kind == KindButton;
    }

    public bool TryGetValue(out MouseMoved value)
    {
        value = kind == KindMove ? new MouseMoved(delta) : default;
        return kind == KindMove;
    }

    public bool TryGetValue(out Scrolled value)
    {
        value = kind == KindScroll ? new Scrolled(delta) : default;
        return kind == KindScroll;
    }

    public object? Value => kind switch
    {
        KindKey => new KeyChanged(code, isDown),
        KindButton => new MouseButtonChanged(code, isDown),
        KindMove => new MouseMoved(delta),
        KindScroll => new Scrolled(delta),
        _ => null,
    };

    // The held case alone decides equality: the fields of the other cases are not compared. Vector2.Equals, not the operator
    // ==, which follows IEEE 754 and calls a NaN unequal to itself: Equals treats a NaN as equal to a NaN, which agrees with
    // GetHashCode, so an event that holds a NaN equals itself.
    public bool Equals(InputEvent other) => kind == other.kind && kind switch
    {
        KindKey or KindButton => code == other.code && isDown == other.isDown,
        KindMove or KindScroll => delta.Equals(other.delta),
        _ => true,
    };

    public override bool Equals(object? obj) => obj is InputEvent other && Equals(other);

    public override int GetHashCode() => kind switch
    {
        KindKey or KindButton => HashCode.Combine(kind, code, isDown),
        KindMove or KindScroll => HashCode.Combine(kind, delta),
        _ => 0,
    };

    public static bool operator ==(InputEvent a, InputEvent b) => a.Equals(b);
    public static bool operator !=(InputEvent a, InputEvent b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        KeyChanged c => $"InputEvent({c})",
        MouseButtonChanged c => $"InputEvent({c})",
        MouseMoved c => $"InputEvent({c})",
        Scrolled c => $"InputEvent({c})",
        null => "InputEvent(none)",
    };
}

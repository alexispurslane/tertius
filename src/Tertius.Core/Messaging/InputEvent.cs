using System.Numerics;
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
/// <param name="Delta">
/// The movement in pixels since the mouse position that the window reported before, X to the right, Y down. The host's
/// <c>MouseTracker</c> measures it from report to report, so it is not the sum of the movements since the last event of this
/// case: the host posts no movement while the fly camera is on, and the first report after the cursor mode changes moves
/// nothing.
/// </param>
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
/// The union generator writes its union members (docs/plans/union-tooling.md). The default value holds no case: nothing
/// sends it, and a component that matches the cases ignores it. The cases share their slots by type: the key case and the
/// button case share their code and their flag, and the move case and the scroll case share their delta. It is 16 bytes.
/// Scripts and recordings write an input event as JSON in the form of <see cref="InputEventJsonConverter"/>.
/// </remarks>
[GeneratedUnion(typeof(KeyChanged), typeof(MouseButtonChanged), typeof(MouseMoved), typeof(Scrolled))]
[JsonConverter(typeof(InputEventJsonConverter))]
public readonly partial struct InputEvent : IEventPayload
{
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
}

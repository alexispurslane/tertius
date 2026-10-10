using System.Numerics;

namespace Tertius;

/// <summary>Sent every tick to each entity whose mask contains it. The scheduler puts it first in the list.</summary>
public struct Tick : IEventPayload
{
    public long Number;
    public float DeltaTime;
}

/// <summary>Sent on the first tick of a new entity.</summary>
public struct Spawned : IEventPayload;

/// <summary>A request to destroy the recipient. A component can remove it from the list to refuse.</summary>
public struct Destroy : IEventPayload;

/// <summary>Sent after a hand-off moved the entity to another frame.</summary>
public struct FrameChanged : IEventPayload
{
    public FrameHandle From;
    public FrameHandle To;
}

/// <summary>Physics reported a contact that started. Point and normal are in the recipient's frame.</summary>
public struct Collision : IEventPayload
{
    public Entity Other;
    public Vector3 Point;
    public Vector3 Normal;
    public float Impulse;
}

public enum InputKind : byte
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
    MouseMove,
    Scroll,
}

/// <summary>
/// The host sends input to <see cref="Topics.Input"/>. <see cref="Code"/> is a Silk.NET <c>Key</c> or
/// <c>MouseButton</c> value. <see cref="Delta"/> is the mouse movement in pixels, or the scroll amount.
/// </summary>
public struct InputEvent : IEventPayload
{
    public InputKind Kind;
    public int Code;
    public Vector2 Delta;
}

/// <summary>Registers the engine payload types first, so that their ids are always 0 to 6.</summary>
public static class EngineEvents
{
    public static void Register()
    {
        EventTypeRegistry.Register<Tick>();
        EventTypeRegistry.Register<Spawned>();
        EventTypeRegistry.Register<Destroy>();
        EventTypeRegistry.Register<FrameChanged>();
        EventTypeRegistry.Register<Collision>();
        EventTypeRegistry.Register<InputEvent>();
    }
}

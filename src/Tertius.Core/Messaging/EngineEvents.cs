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

/// <summary>
/// Physics reported a contact that started: the first step in which the two bodies really touched, by a solver impulse
/// or zero separation, or for a sensor body (<see cref="BodyDesc.IsSensor"/>) by an overlap. Point and normal are in the
/// recipient's frame. <see cref="Impulse"/> is zero for the touch of a sensor, which pushes nothing. Each Collision ends
/// later with exactly one <see cref="CollisionEnded"/> with the same <see cref="Other"/>.
/// </summary>
public struct Collision : IEventPayload
{
    public Entity Other;
    public Double3 Point;
    public Vector3 Normal;
    public float Impulse;
}

/// <summary>
/// Physics reported that a contact which started with a <see cref="Collision"/> ended: the two bodies stopped touching,
/// or the body of <see cref="Other"/> was destroyed, in which case <see cref="Other"/> may name an entity that no
/// longer exists. Sleep, wake and moves between numeric frames end nothing.
/// </summary>
public struct CollisionEnded : IEventPayload
{
    public Entity Other;
}

/// <summary>
/// The answer to a ray cast that a component asked for with <c>Self.Physics</c>. It arrives in the tick after the
/// request, in the inbox of the entity that asked. <see cref="Entity"/> is the entity hit, or null for a miss, which is an
/// answer too. <see cref="Point"/> and <see cref="Normal"/> are in the frame of the entity that asked, as its frame is in
/// the tick that the answer arrives. <see cref="Distance"/> is along the ray from its origin, in meters. A miss has the
/// end of the ray as its point, the ray's length as its distance, and no normal. <see cref="Tag"/> is the value that the
/// request gave, so that a component can match each answer to its question.
/// </summary>
/// <remarks>
/// An event payload holds 48 bytes, and the entity, the point, the distance and the tag take 40 of them. So the normal is
/// stored in the octahedral form of two floats (Cigolle et al. 2014, as the renderer's G-buffer stores normals), which
/// keeps its direction within a millionth of a radian. <see cref="Normal"/> converts it on each read and write.
/// </remarks>
public struct RayHit : IEventPayload
{
    /// <summary>The entity hit, or null for a miss.</summary>
    public Entity Entity;
    public Double3 Point;
    public float Distance;
    public int Tag;
    private Vector2 octahedralNormal;

    /// <summary>True when the ray hit an entity.</summary>
    public readonly bool IsHit => !Entity.IsNull;

    /// <summary>The unit normal of the surface at <see cref="Point"/>, or zero for a miss. Set it to a unit vector.</summary>
    public Vector3 Normal
    {
        readonly get => IsHit ? Decode(octahedralNormal) : Vector3.Zero;
        set => octahedralNormal = Encode(value);
    }

    /// <summary>Folds a unit vector onto the octahedron |x| + |y| + |z| = 1, and the octahedron's lower half over its upper.</summary>
    private static Vector2 Encode(Vector3 n)
    {
        n /= MathF.Abs(n.X) + MathF.Abs(n.Y) + MathF.Abs(n.Z);
        return n.Z >= 0 ? new Vector2(n.X, n.Y) : new Vector2((1 - MathF.Abs(n.Y)) * SignNotZero(n.X), (1 - MathF.Abs(n.X)) * SignNotZero(n.Y));
    }

    private static Vector3 Decode(Vector2 e)
    {
        var n = new Vector3(e.X, e.Y, 1 - MathF.Abs(e.X) - MathF.Abs(e.Y));
        if (n.Z < 0)
            n = new Vector3((1 - MathF.Abs(e.Y)) * SignNotZero(e.X), (1 - MathF.Abs(e.X)) * SignNotZero(e.Y), n.Z);
        return Vector3.Normalize(n);
    }

    private static float SignNotZero(float v) => v >= 0 ? 1 : -1;
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

/// <summary>Registers the engine payload types first, so that their ids are always 0 to 7.</summary>
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
        EventTypeRegistry.Register<CollisionEnded>();
        EventTypeRegistry.Register<RayHit>();
    }
}

using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>
/// The recipient of a message: one entity, the subscribers of a topic, or every live entity.
/// This is a custom C# 15 union. <see cref="HasValue"/> and the <c>TryGetValue</c> methods let the compiler
/// pattern-match it without boxing. <see cref="Value"/> boxes, so use it only for debugging.
/// </summary>
[Union]
public readonly struct Target : IEquatable<Target>
{
    private const byte KindNone = 0, KindEntity = 1, KindTopic = 2, KindBroadcast = 3;

    private readonly ulong bits;
    private readonly byte kind;

    public Target(Entity entity) { kind = KindEntity; bits = entity.Raw; }
    public Target(Topic topic) { kind = KindTopic; bits = (ulong)(uint)topic.Id; }
    public Target(Broadcast broadcast) { kind = KindBroadcast; bits = 0; }

    public static implicit operator Target(Entity entity) => new(entity);
    public static implicit operator Target(FrameHandle frame) => new(frame.Entity);
    public static implicit operator Target(Topic topic) => new(topic);
    public static implicit operator Target(Broadcast broadcast) => new(broadcast);

    public bool HasValue => kind != KindNone;

    public bool TryGetValue(out Entity entity)
    {
        entity = Entity.FromRaw(bits);
        return kind == KindEntity;
    }

    public bool TryGetValue(out Topic topic)
    {
        topic = new Topic((int)(uint)bits);
        return kind == KindTopic;
    }

    public bool TryGetValue(out Broadcast broadcast)
    {
        broadcast = default;
        return kind == KindBroadcast;
    }

    public object? Value => kind switch
    {
        KindEntity => Entity.FromRaw(bits),
        KindTopic => new Topic((int)(uint)bits),
        KindBroadcast => new Broadcast(),
        _ => null,
    };

    public bool Equals(Target other) => kind == other.kind && bits == other.bits;
    public override bool Equals(object? obj) => obj is Target other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(kind, bits);
    public static bool operator ==(Target a, Target b) => a.Equals(b);
    public static bool operator !=(Target a, Target b) => !a.Equals(b);

    public override string ToString() => this switch
    {
        Entity e => $"Target({e})",
        Topic t => $"Target({Topics.NameOf(t)})",
        Broadcast => "Target(broadcast)",
        _ => "Target(none)",
    };
}

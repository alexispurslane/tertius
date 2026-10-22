using System.Diagnostics.CodeAnalysis;

namespace Tertius;

/// <summary>
/// A handle to an entity: a slot index and a generation. The generation of a slot increments every time the
/// slot is freed, so a handle to a destroyed entity is stale. Live generations start at 1, so
/// <c>default(Entity)</c> is the null handle.
/// </summary>
[SuppressMessage("Design", "CA1036",
    Justification =
        "CompareTo gives a deterministic sort order only. Operators such as < would suggest that one entity comes before another in a meaningful way.")]
public readonly record struct Entity(uint Index, uint Generation) : IComparable<Entity>
{
    public static Entity Null => default;

    public bool IsNull => Generation == 0;

    /// <summary>The handle packed into 64 bits: generation in the high half, index in the low half.</summary>
    public ulong Raw => ((ulong)Generation << 32) | Index;

    public static Entity FromRaw(ulong raw) => new((uint)raw, (uint)(raw >> 32));

    public int CompareTo(Entity other) =>
        Index != other.Index ? Index.CompareTo(other.Index) : Generation.CompareTo(other.Generation);

    public override string ToString() => IsNull ? "Entity(null)" : $"Entity({Index}v{Generation})";
}

/// <summary>
/// A handle to a reference frame. A frame is an entity that also has a row in the <see cref="FrameTree"/>.
/// <see cref="Index"/> and <see cref="Generation"/> identify that row.
/// </summary>
public readonly record struct FrameHandle(Entity Entity, int Index, uint Generation)
{
    public static FrameHandle Null => default;

    public bool IsNull => Generation == 0;

    /// <summary>The message topic of this frame. Every entity in the frame subscribes to it.</summary>
    public Topic Topic => Topic.OfFrame(this);

    public static implicit operator Entity(FrameHandle frame) => frame.Entity;

    public override string ToString() => IsNull ? "Frame(null)" : $"Frame({Index}v{Generation}, {Entity})";
}

/// <summary>
/// A message topic. Non-negative ids are named topics from <see cref="Topics"/>. Negative ids are frame topics.
/// A frame topic also carries the frame's generation, so a message to a destroyed frame never reaches a new frame
/// that reuses its index. Named topics have generation 0.
/// </summary>
public readonly record struct Topic(int Id, uint Generation = 0)
{
    public bool IsFrameTopic => Id < 0;

    /// <summary>The frame index of a frame topic.</summary>
    public int FrameIndex => -1 - Id;

    public static Topic OfFrame(FrameHandle frame) => new(-1 - frame.Index, frame.Generation);
}

/// <summary>The target case for a message to every live entity.</summary>
public readonly record struct Broadcast;

/// <summary>Named topics. Ids are dense and stable for the life of the process.</summary>
public static class Topics
{
    private static readonly Dictionary<string, Topic> ByName = [];
    private static readonly List<string> Names = [];
    private static readonly Lock Gate = new();

    /// <summary>The topic that the host sends input events to.</summary>
    public static readonly Topic Input = Get("input");

    public static int Count
    {
        get
        {
            lock (Gate)
            {
                return Names.Count;
            }
        }
    }

    public static Topic Get(string name)
    {
        lock (Gate)
        {
            if (ByName.TryGetValue(name, out Topic topic))
                return topic;
            topic = new(Names.Count);
            Names.Add(name);
            ByName.Add(name, topic);
            return topic;
        }
    }

    public static string NameOf(Topic topic)
    {
        if (topic.IsFrameTopic)
            return $"frame:{topic.FrameIndex}";
        lock (Gate)
        {
            return topic.Id < Names.Count ? Names[topic.Id] : $"topic:{topic.Id}";
        }
    }
}

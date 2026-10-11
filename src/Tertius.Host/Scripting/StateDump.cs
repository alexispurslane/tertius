using System.Numerics;
using Tertius.Simulation;

namespace Tertius.Host.Scripting;

/// <summary>
/// The state of the world at one moment of a scripted run. See docs/plans/host-and-scripted-runs.md, "State dumps".
/// </summary>
/// <param name="Name">The name of the dump step.</param>
/// <param name="Tick">The number of completed ticks, <c>World.TickNumber</c>.</param>
/// <param name="Time">Simulation time, in seconds.</param>
/// <param name="Camera">The scene id of the world's camera, or null if the world has no live camera.</param>
/// <param name="Entities">Every live entity, in slot order.</param>
/// <param name="Frames">Every frame, in the dense order of the frame tree: each parent before its children.</param>
/// <param name="Events">Every event that the sync points routed since the previous dump, by tick, then in routing order.</param>
/// <param name="Errors"><c>World.Errors</c> at the time of the dump, oldest first.</param>
public sealed record StateDump(
    string Name,
    long Tick,
    double Time,
    string? Camera,
    IReadOnlyList<EntityRecord> Entities,
    IReadOnlyList<FrameRecord> Frames,
    IReadOnlyList<EventRecord> Events,
    IReadOnlyList<string> Errors)
{
    /// <summary>The entity with the scene id. Throws <see cref="KeyNotFoundException"/> if the dump has none.</summary>
    public EntityRecord Entity(string id) =>
        Entities.FirstOrDefault(entity => entity.Id == id) ?? throw new KeyNotFoundException($"The dump '{Name}' has no entity with the id '{id}'.");
}

/// <summary>One live entity.</summary>
/// <param name="Id">The scene id, or <c>#index.generation</c>.</param>
/// <param name="Pose">The pose in the entity's own frame. The owner of a root frame has no frame.</param>
/// <param name="RootPose">The pose in the root frame of the entity's frame tree.</param>
/// <param name="Velocity">Linear velocity in the axes of the entity's own frame, in m/s.</param>
/// <param name="AngularVelocity">Angular velocity in the axes of the entity's own frame, in rad/s.</param>
public sealed record EntityRecord(string Id, FramePose Pose, FramePose RootPose, Vector3 Velocity, Vector3 AngularVelocity);

/// <summary>A pose and the frame that it is relative to.</summary>
/// <param name="Frame">The scene id of the frame's owner, or null for no frame.</param>
public sealed record FramePose(string? Frame, Double3 Position, DQuaternion Rotation);

/// <summary>One frame of the frame tree.</summary>
/// <param name="Id">The scene id of the entity that owns the frame.</param>
/// <param name="Parent">The scene id of the parent frame's owner, or null for a root frame.</param>
/// <param name="Driver">What moves the frame.</param>
public sealed record FrameRecord(string Id, string? Parent, MotionDriver Driver);

/// <summary>One routed event.</summary>
/// <param name="Tick">The number of completed ticks after the sync point that routed it.</param>
/// <param name="Event">The C# name of the payload type.</param>
/// <param name="To">Who the event was for.</param>
/// <param name="From">The scene id of the sender, or null for an event from outside the simulation.</param>
/// <param name="Payload">The payload, boxed.</param>
public sealed record EventRecord(long Tick, string Event, MessageTarget To, string? From, object Payload);

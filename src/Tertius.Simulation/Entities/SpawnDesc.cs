using System.Numerics;

namespace Tertius.Simulation;

/// <summary>Everything needed to create an entity. The world copies the lists when it accepts the description.</summary>
public sealed class SpawnDesc
{
    /// <summary>The frame to spawn in. It may be null only when <see cref="AsFrame"/> is set: the entity is then a root frame.</summary>
    public FrameHandle Frame { get; init; }

    /// <summary>The pose in <see cref="Frame"/>. For a frame entity, this is the frame's local pose.</summary>
    public DPose Pose { get; init; } = DPose.Identity;

    /// <summary>Linear velocity in frame axes.</summary>
    public Vector3 LinearVelocity { get; init; }

    /// <summary>Angular velocity in frame axes.</summary>
    public Vector3 AngularVelocity { get; init; }

    /// <summary>A physics body. If set, physics owns the entity's transform.</summary>
    public BodyDesc? Body { get; init; }

    /// <summary>The render row. Null means nothing to draw.</summary>
    public RenderData? Render { get; init; }

    /// <summary>The component instances. Each instance belongs to one entity, so do not share them between spawns.</summary>
    public IReadOnlyList<IComponent> Components { get; init; } = [];

    /// <summary>If set, the entity becomes a frame whose parent is <see cref="Frame"/> and whose local pose is <see cref="Pose"/>.</summary>
    public FrameSpec? AsFrame { get; init; }

    /// <summary>
    /// If set, the entity follows this parent with the fixed local offset <see cref="AttachOffset"/>. The parent must be
    /// in the same frame. An attached entity has no body and is destroyed with its parent.
    /// </summary>
    public Entity AttachTo { get; init; }

    /// <summary>The fixed pose of the entity relative to <see cref="AttachTo"/>.</summary>
    public Pose AttachOffset { get; init; } = Tertius.Pose.Identity;

    /// <summary>Named topics to subscribe to. The frame topic is automatic.</summary>
    public IReadOnlyList<Topic> Subscriptions { get; init; } = [];
}

/// <summary>Makes a spawned entity a reference frame.</summary>
/// <param name="Bounds">The bubble that decides which objects belong to the frame.</param>
/// <param name="Driver">What moves the frame.</param>
/// <param name="Gravity">The gravity field that objects inside feel.</param>
/// <param name="Orbit">The orbit around the parent, for <see cref="MotionDriver.Kepler"/>.</param>
public sealed record FrameSpec(FrameBounds Bounds, MotionDriver Driver, GravityField Gravity = default, KeplerOrbit Orbit = default);

using System.Numerics;

namespace Tertius.Simulation;

/// <summary>Everything needed to create an entity. The world copies the lists when it accepts the description.</summary>
public sealed class SpawnDesc
{
    /// <summary>
    /// The frame to spawn in. It can be null when <see cref="AsFrame"/> is set: the entity is then a root frame. The frame
    /// that an attached entity names does not count, because it becomes live in the frame of its parent (see
    /// <see cref="AttachTo"/>). So an attached entity can leave this null.
    /// </summary>
    public FrameHandle Frame { get; init; }

    /// <summary>The pose in <see cref="Frame"/>. For a frame entity, this is the frame's local pose.</summary>
    public DPose Pose { get; init; } = DPose.Identity;

    /// <summary>Linear velocity relative to <see cref="Frame"/>, in its axes. Physics converts it when it creates the body.</summary>
    public Double3 LinearVelocity { get; init; }

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
    /// If set, the entity follows this parent with the fixed local offset <see cref="AttachOffset"/>. An attached entity
    /// has no body, cannot be a frame, and is destroyed with its parent. The world derives its pose and velocities from the
    /// parent. It becomes live in the frame that the parent is in at that time, whatever <see cref="Frame"/> names: at the
    /// call outside a tick, and at the sync point during a tick. So if physics hands the parent into another frame in the
    /// step of the same tick, or the parent's frame dies and the parent moves to an ancestor frame, the new entity becomes
    /// live in the parent's new frame. The parent must be live at that time, and it must not own a root frame, because such
    /// an owner is in no frame. For another parent, <see cref="World.Spawn(SpawnDesc)"/> throws
    /// <see cref="ArgumentException"/> outside a tick. At the sync point, the world drops the spawn and records an error in
    /// <see cref="World.Errors"/>.
    /// </summary>
    public Entity AttachTo { get; init; }

    /// <summary>The fixed pose of the entity relative to <see cref="AttachTo"/>.</summary>
    public Pose AttachOffset { get; init; } = Tertius.Pose.Identity;

    /// <summary>Named topics to subscribe to. The frame topic is automatic.</summary>
    public IReadOnlyList<Topic> Subscriptions { get; init; } = [];
}

/// <summary>Makes a spawned entity a reference frame.</summary>
/// <param name="Bounds">The bubble that decides which objects belong to the frame.</param>
/// <param name="Driver">
/// The motion of the frame's hull: dynamic for <see cref="MotionDriver.Physics"/>, kinematic for
/// <see cref="MotionDriver.Kepler"/> and <see cref="MotionDriver.Spin"/>, and static for <see cref="MotionDriver.Static"/>
/// (see <see cref="BodyDesc.FrameHullMotion"/>).
/// </param>
/// <param name="Gravity">The gravity field that objects inside feel.</param>
/// <remarks>
/// A frame on rails takes its orbit or its spin from a component of its entity, <see cref="KeplerMotion"/> or
/// <see cref="SpinMotion"/>, not from this description (docs/plans/rails-components.md).
/// </remarks>
public sealed record FrameSpec(FrameBounds Bounds, MotionDriver Driver, GravityField Gravity = default);

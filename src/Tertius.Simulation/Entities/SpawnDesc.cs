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

    /// <summary>
    /// The new entity's first motion intent: how it moves in the step after its spawn, relative to <see cref="Frame"/>, in
    /// its axes (docs/plans/rails-components.md, "The first motion intent of a spawn"). It is the motion intent that the
    /// entity would have written in the spawn's tick, had it run then, so that a frame on rails stands on its rails from its
    /// first step. A component that calls <see cref="Self.Spawn(SpawnDesc)"/> in tick k gives the motion of the step that
    /// ends at <c>self.TimeAt(self.Tick + 2)</c>, and a game that calls <see cref="World.Spawn(SpawnDesc)"/> outside a tick
    /// the motion of the step that ends at <c>World.TimeAt(World.TickNumber + 1)</c>. For the owner of a frame,
    /// <see cref="Frame"/> is the frame's semantic parent, and for the owner of a root frame it is null, so the intent is in
    /// the root's own space. The default holds no case: the entity has no motion intent in that step, and a frame on rails
    /// holds still for it. A spawn refuses a case that its entity would refuse, and a number that is not finite or a zero
    /// rotation, with an <see cref="ArgumentException"/>.
    /// </summary>
    public MotionIntent FirstMove { get; init; }

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
/// <param name="Gravity">The gravity field that objects inside feel.</param>
/// <remarks>
/// The frame moves as its owner's body moves, not as this description says. For a frame with a parent, the owner's body
/// is the frame's hull, and the hull's motion is the motion of its description (see <see cref="BodyDesc.Motion"/>): a
/// dynamic hull falls and coasts in the parent's physics system, a kinematic hull follows the motion samples of a rails
/// component of its entity, such as <see cref="KeplerMotion"/> or <see cref="SpinMotion"/>, and a static hull holds
/// still. The owner changes the motion later with <see cref="BodyHandle.SetMotion"/>. A frame without a body follows the
/// samples of its rails component, or its owner's transform write, or holds still (docs/plans/rails-components.md).
/// </remarks>
public sealed record FrameSpec(FrameBounds Bounds, GravityField Gravity = default);

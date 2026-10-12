using System.Numerics;

namespace Tertius.Simulation;

/// <summary>One row of the transform table.</summary>
internal struct TransformRow
{
    /// <summary>
    /// The semantic frame the entity is in: what <see cref="Self.Frame"/> shows. Null only for the owner of a root frame.
    /// </summary>
    public FrameHandle Frame;
    /// <summary>
    /// The frame whose physics system holds the entity's body: <see cref="Frame"/> or a numeric frame below it. An entity
    /// without a body has the home of what moves it, which is <see cref="Frame"/> unless it is attached. The row
    /// frame is always the home's semantic frame (see <c>FrameTree.SemanticOf</c>).
    /// </summary>
    public FrameHandle PhysicsHome;
    /// <summary>The pose in <see cref="Frame"/>.</summary>
    public DPose Pose;
    /// <summary>The local linear velocity relative to <see cref="PhysicsHome"/>, in its axes, which are those of <see cref="Frame"/>.</summary>
    public Vector3 LinearVelocity;
    /// <summary>Angular velocity in the axes of <see cref="Frame"/>.</summary>
    public Vector3 AngularVelocity;
}

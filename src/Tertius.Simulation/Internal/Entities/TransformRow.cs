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

    /// <summary>
    /// The position in <see cref="PhysicsHome"/>, in meters, when that is a numeric frame below <see cref="Frame"/>.
    /// <see cref="Pose"/>'s position is then this plus the home's offset in <see cref="Frame"/>. Far from the row frame's
    /// origin that sum rounds, while this stays exact, so reads between nearby entities start from it (see
    /// <see cref="HomePose"/>). When the home is the row frame, <see cref="Pose"/> is already in the home, and nothing
    /// keeps this field: a gameplay write of the pose leaves it as it was.
    /// </summary>
    public Double3 HomePosition;

    /// <summary>The local linear velocity relative to <see cref="PhysicsHome"/>, in its axes, which are those of
    /// <see cref="Frame"/>.</summary>
    public Vector3 LinearVelocity;

    /// <summary>Angular velocity in the axes of <see cref="Frame"/>.</summary>
    public Vector3 AngularVelocity;

    /// <summary>The pose in <see cref="PhysicsHome"/>. Numeric frames do not rotate, so the rotation is that of
    /// <see cref="Pose"/>.</summary>
    public readonly DPose HomePose => PhysicsHome == Frame ? Pose : new(HomePosition, Pose.Rotation);
}

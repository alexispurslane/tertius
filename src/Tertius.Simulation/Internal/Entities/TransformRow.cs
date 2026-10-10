using System.Numerics;

namespace Tertius.Simulation;

/// <summary>One row of the transform table.</summary>
internal struct TransformRow
{
    /// <summary>The frame the entity is in. Null only for the owner of a root frame.</summary>
    public FrameHandle Frame;
    /// <summary>The pose in <see cref="Frame"/>.</summary>
    public DPose Pose;
    /// <summary>Linear velocity in frame axes.</summary>
    public Vector3 LinearVelocity;
    /// <summary>Angular velocity in frame axes.</summary>
    public Vector3 AngularVelocity;
}

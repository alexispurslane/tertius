namespace Tertius.Simulation;

/// <summary>
/// The running entity's own rig pose, for writing: the back copy, which the sync point publishes with the render row
/// (docs/plans/skinning-and-morphs.md, "The rig pose in the simulation"). Take it from <see cref="Self.RigPose"/>.
/// </summary>
public readonly ref struct RigPoseWriter
{
    internal RigPoseWriter(Span<Pose> joints, Span<float> weights)
    {
        Joints = joints;
        Weights = weights;
    }

    /// <summary>Each joint's pose relative to its parent joint, or to the model's origin for a root joint, in the rig's order.</summary>
    public Span<Pose> Joints { get; }

    /// <summary>Each morph's weight, in the rig's order.</summary>
    public Span<float> Weights { get; }
}

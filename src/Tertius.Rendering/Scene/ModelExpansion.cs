using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

/// <summary>
/// The instances that a frame draws: the snapshot's, with each instance of a model replaced by one instance per part, in
/// part order, and the snapshot's rig poses renumbered to match (ARCHITECTURE.md, "Models"). Every collector of the frame
/// reads these, so each part culls, casts, moves and becomes an impostor as an ordinary instance.
/// </summary>
/// <remarks>
/// A snapshot without a model passes through with no copy. The rig pose of a model instance is dropped, so its parts draw
/// as uploaded.
/// </remarks>
internal sealed class ModelExpansion
{
    // The snapshot of the last expansion when it held no model, which then passes through.
    private RenderSnapshot? passThrough;
    private RenderInstance[] instances = new RenderInstance[64];
    private int instanceCount;
    private RenderRigPose[] rigPoses = new RenderRigPose[8];
    private int rigPoseCount;

    /// <summary>The instances of the last <see cref="Expand"/>, valid until the next.</summary>
    public ReadOnlySpan<RenderInstance> Instances =>
        passThrough is { } snapshot
            ? CollectionsMarshal.AsSpan(snapshot.Instances)
            : instances.AsSpan(0, instanceCount);

    /// <summary>
    /// The rig poses of the last <see cref="Expand"/>, each naming its instance in <see cref="Instances"/>. Their joint
    /// and weight ranges still index the snapshot's lists.
    /// </summary>
    public ReadOnlySpan<RenderRigPose> RigPoses =>
        passThrough is { } snapshot
            ? CollectionsMarshal.AsSpan(snapshot.RigPoses)
            : rigPoses.AsSpan(0, rigPoseCount);

    /// <summary>
    /// Expands the instances of <paramref name="snapshot"/> with <paramref name="models"/>, indexed by
    /// <see cref="ModelId.Value"/>. A model instance draws nothing when its model is not in <paramref name="models"/>, or
    /// when its scale differs along the axes, because a turned part would need a shear.
    /// </summary>
    public void Expand(RenderSnapshot snapshot, ReadOnlySpan<ModelDesc> models)
    {
        ReadOnlySpan<RenderInstance> source = CollectionsMarshal.AsSpan(snapshot.Instances);
        passThrough = HasModel(source) ? null : snapshot;
        if (passThrough is not null)
            return;
        instanceCount = 0;
        rigPoseCount = 0;
        ReadOnlySpan<RenderRigPose> poses = CollectionsMarshal.AsSpan(snapshot.RigPoses);
        int nextPose = 0;
        for (int index = 0; index < source.Length; index++)
        {
            ref readonly RenderInstance instance = ref source[index];
            // The rig poses come in instance order, so one walk pairs them with their instances.
            bool posed = nextPose < poses.Length && poses[nextPose].Instance == index;
            if (instance.Model.IsNone)
            {
                if (posed)
                    AddRigPose(poses[nextPose] with { Instance = instanceCount });
                Add(instance);
            }
            else
                AddParts(instance, models);
            if (posed)
                nextPose++;
        }
    }

    private static bool HasModel(ReadOnlySpan<RenderInstance> instances)
    {
        foreach (ref readonly RenderInstance instance in instances)
            if (!instance.Model.IsNone)
                return true;
        return false;
    }

    /// <summary>Adds one instance per part of the model of <paramref name="owner"/>, or none (see <see cref="Expand"/>).</summary>
    private void AddParts(in RenderInstance owner, ReadOnlySpan<ModelDesc> models)
    {
        bool held = (uint)owner.Model.Value < (uint)models.Length;
        if (!held || UniformFactor(owner.Scale) is not { } scale)
            return;
        // A previous scale that the batcher would not interpolate from counts as the current one, as it does there.
        bool interpolates =
            SceneBatcher.HasVolume(owner.PreviousScale)
            && UniformFactor(owner.PreviousScale) is { } previous
            && previous * scale > 0;
        float previousScale = interpolates ? owner.PreviousScale.X : scale;
        ReadOnlySpan<ModelPart> parts = models[owner.Model.Value].Parts.AsSpan();
        for (int index = 0; index < parts.Length; index++)
            Add(PartOf(owner: owner, part: parts[index], index: (ushort)index, scale: scale, previousScale: previousScale));
    }

    /// <summary>
    /// The instance of <paramref name="part"/> of the model that <paramref name="owner"/> draws at the uniform
    /// <paramref name="scale"/>, and at <paramref name="previousScale"/> before.
    /// </summary>
    private static RenderInstance PartOf(
        in RenderInstance owner,
        in ModelPart part,
        ushort index,
        float scale,
        float previousScale
    )
    {
        Pose current = Pose.Compose(
            parent: new(owner.Position, RotationOf(owner.Rotation)),
            local: part.Offset with { Position = part.Offset.Position * scale }
        );
        Pose previous = Pose.Compose(
            parent: new(owner.PreviousPosition, RotationOf(owner.PreviousRotation)),
            local: part.Offset with { Position = part.Offset.Position * previousScale }
        );
        return owner with
        {
            Part = index,
            Model = ModelId.None,
            Mesh = part.Mesh,
            Material = part.Material,
            Position = current.Position,
            Rotation = current.Rotation,
            Scale = part.Scale * scale,
            PreviousPosition = previous.Position,
            PreviousRotation = previous.Rotation,
            PreviousScale = part.Scale * previousScale,
            BoundingRadius = part.BoundingRadius * MathF.Abs(scale),
        };
    }

    /// <summary>The factor of a scale that is the same along every axis, or null for one that differs.</summary>
    private static float? UniformFactor(Vector3 scale)
    {
        bool uniform = scale.X == scale.Y && scale.Y == scale.Z;
        return uniform ? scale.X : null;
    }

    /// <summary>A rotation that is not one, such as the zero of an unset field, counts as the identity.</summary>
    private static Quaternion RotationOf(Quaternion rotation)
    {
        float lengthSquared = rotation.LengthSquared();
        bool isRotation = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
        return isRotation ? Quaternion.Normalize(rotation) : Quaternion.Identity;
    }

    private void Add(in RenderInstance instance)
    {
        if (instanceCount == instances.Length)
            Array.Resize(ref instances, instances.Length * 2);
        instances[instanceCount++] = instance;
    }

    private void AddRigPose(in RenderRigPose pose)
    {
        if (rigPoseCount == rigPoses.Length)
            Array.Resize(ref rigPoses, rigPoses.Length * 2);
        rigPoses[rigPoseCount++] = pose;
    }
}

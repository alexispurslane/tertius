using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius;

/// <summary>A rigid frame of a <see cref="Rig"/> (docs/plans/skinning-and-morphs.md, "The mesh data").</summary>
/// <param name="Name">The joint's name, unique among the rig's joints.</param>
/// <param name="Parent">The index of the parent joint, below the joint's own index, or null for a root joint.</param>
/// <param name="Rest">The joint pose at rest, relative to the parent joint or to the model's origin.</param>
public readonly record struct RigJoint(string Name, int? Parent, Pose Rest);

/// <summary>A named weight of a <see cref="Rig"/>.</summary>
/// <param name="Name">The morph's name, unique among the rig's morphs.</param>
/// <param name="Rest">The morph weight at rest, usually 0.</param>
public readonly record struct RigMorph(string Name, float Rest);

/// <summary>
/// The joints of a model, parents before children, and its morphs. The meshes of one model share one rig, and an entity's
/// <see cref="RigPose"/> poses it (docs/plans/skinning-and-morphs.md).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A joint's parent comes before it, so a walk in order meets every parent first.</item>
/// <item>No two joints share a name, and no two morphs share a name.</item>
/// <item>Every rest pose and rest weight is finite, and every rest rotation is a rotation.</item>
/// <item>A rig has at most <see cref="MaxJoints"/> joints.</item>
/// </list>
/// </remarks>
public sealed class Rig
{
    /// <summary>The most joints that a 16-bit joint index of <see cref="VertexSkin"/> names.</summary>
    public const int MaxJoints = 65_535;

    private readonly Dictionary<string, int> jointsByName;
    private readonly Dictionary<string, int> morphsByName;

    /// <summary>A rig of <paramref name="joints"/>, parents before children, and <paramref name="morphs"/>.</summary>
    /// <exception cref="ArgumentException">
    /// A parent that does not come before its joint, a name that repeats, a rest pose or weight that is not finite, a rest
    /// rotation that is not a rotation, or more than <see cref="MaxJoints"/> joints.
    /// </exception>
    public Rig(ImmutableArray<RigJoint> joints, ImmutableArray<RigMorph> morphs)
    {
        if (joints.Length > MaxJoints)
            throw new ArgumentException($"A rig has at most {MaxJoints} joints, not {joints.Length}.", nameof(joints));
        for (int index = 0; index < joints.Length; index++)
            CheckJoint(index, joints[index]);
        for (int index = 0; index < morphs.Length; index++)
            CheckMorph(index, morphs[index]);

        jointsByName = ByName(joints.Select(joint => joint.Name), nameof(joints));
        morphsByName = ByName(morphs.Select(morph => morph.Name), nameof(morphs));
        Joints = joints;
        Morphs = morphs;
        Rest = new([.. joints.Select(joint => joint.Rest)], [.. morphs.Select(morph => morph.Rest)]);
    }

    public ImmutableArray<RigJoint> Joints { get; }
    public ImmutableArray<RigMorph> Morphs { get; }

    /// <summary>The rest pose: every joint's <see cref="RigJoint.Rest"/>, as given, and every morph's <see cref="RigMorph.Rest"/>.</summary>
    public RigPose Rest { get; }

    /// <summary>The index of the joint of that name, or null when the rig has none.</summary>
    public int? JointNamed(string name) => jointsByName.TryGetValue(name, out int index) ? index : null;

    /// <summary>The index of the morph of that name, or null when the rig has none.</summary>
    public int? MorphNamed(string name) => morphsByName.TryGetValue(name, out int index) ? index : null;

    private static void CheckJoint(int index, in RigJoint joint)
    {
        bool parentBefore = joint.Parent is not { } parent || (parent >= 0 && parent < index);
        if (!parentBefore)
            throw new ArgumentException(
                $"The joint {index} ({joint.Name}) names the parent {joint.Parent}, which does not come before it.",
                "joints"
            );
        Vector3 position = joint.Rest.Position;
        Quaternion rotation = joint.Rest.Rotation;
        bool finite =
            float.IsFinite(position.X)
            && float.IsFinite(position.Y)
            && float.IsFinite(position.Z)
            && float.IsFinite(rotation.X)
            && float.IsFinite(rotation.Y)
            && float.IsFinite(rotation.Z)
            && float.IsFinite(rotation.W);
        if (!finite)
            throw new ArgumentException(
                $"The rest pose of the joint {index} ({joint.Name}) is not finite: {joint.Rest}.",
                "joints"
            );
        // The rule of the renderer's SceneBatcher.Interpolate: a rotation that it can normalize.
        float lengthSquared = rotation.LengthSquared();
        bool isRotation = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f;
        if (!isRotation)
            throw new ArgumentException(
                $"The rest rotation of the joint {index} ({joint.Name}) is not a rotation: {rotation}.",
                "joints"
            );
    }

    private static void CheckMorph(int index, in RigMorph morph)
    {
        if (!float.IsFinite(morph.Rest))
            throw new ArgumentException(
                $"The rest weight of the morph {index} ({morph.Name}) is not finite: {morph.Rest}.",
                "morphs"
            );
    }

    /// <summary>The index of each name. <paramref name="kind"/> names the parameter that refuses a name that repeats.</summary>
    private static Dictionary<string, int> ByName(IEnumerable<string> names, string kind)
    {
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((int index, string name) in names.Index())
            if (!byName.TryAdd(name, index))
                throw new ArgumentException($"Two {kind} have the name {name}.", kind);
        return byName;
    }
}

/// <summary>
/// A joint pose for every joint of a <see cref="Rig"/>, in the rig's order, and a weight for every morph. An entity's
/// rig pose is its state (docs/plans/skinning-and-morphs.md, "Terms").
/// </summary>
/// <param name="Joints">Each joint's pose relative to its parent joint, or to the model's origin for a root joint.</param>
/// <param name="Weights">Each morph's weight: 0 adds none of a target's deltas, and 1 adds all of them.</param>
public sealed record RigPose(ImmutableArray<Pose> Joints, ImmutableArray<float> Weights);

// The renderer copies skins into a GPU buffer, so the field order is the memory layout. The cleanup's member layout leaves
// a type with StructLayout alone.
/// <summary>
/// A vertex's four joint influences. An unused slot holds joint 0 with weight 0. The weights are each at least 0, with a
/// sum above 0, and the renderer scales them to sum to 1.
/// </summary>
/// <param name="Joint0">The rig's index of the first joint.</param>
/// <param name="Joint1">The rig's index of the second joint.</param>
/// <param name="Joint2">The rig's index of the third joint.</param>
/// <param name="Joint3">The rig's index of the fourth joint.</param>
/// <param name="Weights">The weights of the four joints, in slot order.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct VertexSkin(ushort Joint0, ushort Joint1, ushort Joint2, ushort Joint3, Vector4 Weights);

/// <summary>A mesh's skin. Its joints are the rig's joints, with no subset and no remap.</summary>
/// <param name="Vertices">One <see cref="VertexSkin"/> per vertex of the mesh.</param>
/// <param name="InverseBinds">One inverse bind per joint of the rig, in the rig's order.</param>
public sealed record MeshSkin(VertexSkin[] Vertices, ImmutableArray<Pose> InverseBinds);

/// <summary>
/// One morph's deltas for one mesh, in model space at the bind. Each array is either empty, which means all zero, or
/// holds one delta per vertex of the mesh. A tangent delta moves the tangent's xyz and never its sign.
/// </summary>
/// <param name="Morph">The index of the target's morph in the rig. Two targets of one mesh never share a morph.</param>
/// <param name="Positions">The position deltas.</param>
/// <param name="Normals">The normal deltas.</param>
/// <param name="Tangents">The tangent deltas.</param>
public sealed record MorphTarget(int Morph, Vector3[] Positions, Vector3[] Normals, Vector3[] Tangents);

/// <summary>The rigging of a mesh: its rig, its skin or none, and its morph targets.</summary>
/// <param name="Rig">The rig, which the meshes of one model share.</param>
/// <param name="Skin">The skin, or null for a mesh that only morphs.</param>
/// <param name="Morphs">The morph targets, at most one per morph of the rig.</param>
public sealed record MeshRigging(Rig Rig, MeshSkin? Skin, ImmutableArray<MorphTarget> Morphs);

using System.Buffers;
using System.Diagnostics;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

/// <summary>
/// The CPU side of a deforming mesh: its counts, its place in the deform store, and the math of its deformation, the C#
/// copy of Shaders/deform.comp (docs/plans/skinning-and-morphs.md, "The math, and its C# copy").
/// </summary>
internal sealed partial record MeshDeform
{
    /// <summary>The invocations of one group of the deform pass, one per vertex.</summary>
    public const int InvocationsPerGroup = 64;

    /// <summary>
    /// The most vertices of a deforming mesh: one dispatch of the deform pass covers them within the spec's least
    /// maxComputeWorkGroupCount[0], 65,535 groups (docs/plans/skinning-and-morphs.md, "Portable Vulkan").
    /// </summary>
    public const int MaxVertices = InvocationsPerGroup * 65_535;

    /// <summary>The most joints whose model poses <see cref="PoseInto"/> keeps on the stack. A larger rig rents them.</summary>
    private const int StackJoints = 128;

    /// <summary>The palette: the rig's skin poses for a mesh with a skin, one identity joint for a mesh without.</summary>
    private readonly Palette palette;

    /// <summary>The longest position delta of each morph of the rig, 0 for a morph without a target.</summary>
    private readonly ImmutableArray<float> longestDeltas;

    private MeshDeform(int vertexCount, Palette palette, ImmutableArray<float> longestDeltas, int joints)
    {
        VertexCount = vertexCount;
        Joints = joints;
        Morphs = longestDeltas.Length;
        PaletteJoints = palette switch
        {
            SkinnedPalette => joints,
            IdentityPalette => 1,
            null => throw new UnreachableException("Pack gives every deforming mesh a palette."),
        };
        this.palette = palette;
        this.longestDeltas = longestDeltas;
    }

    public int VertexCount { get; }

    /// <summary>The rig's joint count.</summary>
    public int Joints { get; }

    /// <summary>The rig's morph count.</summary>
    public int Morphs { get; }

    /// <summary>The joints whose skin poses the deformation reads: the rig's joints with a skin, and one identity joint without.</summary>
    public int PaletteJoints { get; }

    /// <summary>The pose rows of one job: three per palette joint, then the weights, four to a row.</summary>
    public int PoseRows => 3 * PaletteJoints + (Morphs + 3) / 4;

    /// <summary>The mesh's first deform vertex in the deform store. <see cref="Pack"/> leaves it 0.</summary>
    public int Source { get; init; }

    /// <summary>The mesh's first morph delta in the deform store. <see cref="Pack"/> leaves it 0.</summary>
    public int Deltas { get; init; }

    /// <summary>
    /// The deform vertices, the sparse deltas, the influence spheres and the longest deltas of <paramref name="mesh"/>, or
    /// null for a mesh without rigging. <see cref="UploadQueue.AddMesh"/> has checked the rigging.
    /// </summary>
    public static PackedDeform? Pack(MeshData mesh)
    {
        if (mesh.Rigging is not { } rigging)
            return null;
        Vertex[] vertices = mesh.Vertices;
        // A vertex's deltas come in morph order, whatever the order of the targets, because the sums follow it.
        MorphTarget[] targets = [.. rigging.Morphs.OrderBy(target => target.Morph)];
        var packed = new GpuDeformVertex[vertices.Length];
        var deltas = new List<GpuMorphDelta>();
        for (int index = 0; index < vertices.Length; index++)
        {
            int start = deltas.Count;
            foreach (MorphTarget target in targets)
                AddDelta(target: target, vertex: index, deltas: deltas);
            packed[index] = Packed(
                vertex: GeometryStore.Staged(vertices[index]),
                skin: rigging.Skin?.Vertices[index],
                morphStart: start,
                morphCount: deltas.Count - start
            );
        }

        int joints = rigging.Rig.Joints.Length;
        Palette palette = rigging.Skin switch
        {
            { } skin => new SkinnedPalette(
                Parents: [.. rigging.Rig.Joints.Select(joint => joint.Parent)],
                InverseBinds: skin.InverseBinds,
                Spheres: Spheres(vertices, skin, joints)
            ),
            null => new IdentityPalette(GeometryStore.Radius(vertices)),
        };
        var deform = new MeshDeform(
            vertexCount: vertices.Length,
            palette: palette,
            longestDeltas: LongestDeltas(rigging),
            joints: joints
        );
        return new(deform, packed, [.. deltas]);
    }

    /// <summary>Whether a rig pose of <paramref name="joints"/> joint poses and <paramref name="weights"/> weights fits this mesh.</summary>
    public bool Fits(int joints, int weights) => joints == Joints && weights == Morphs;

    /// <summary>
    /// Interpolates a rig pose that fits at <paramref name="alpha"/>, writes its <see cref="PoseRows"/> rows, and returns
    /// the posed bound, which is not finite when a skin pose's position is not.
    /// </summary>
    public float PoseInto(
        ReadOnlySpan<Pose> previousJoints,
        ReadOnlySpan<Pose> joints,
        ReadOnlySpan<float> previousWeights,
        ReadOnlySpan<float> weights,
        float alpha,
        Span<Vector4> rows
    )
    {
        float jointReach = palette switch
        {
            SkinnedPalette skinned => WritePalette(
                skinned: skinned,
                previousJoints: previousJoints,
                joints: joints,
                alpha: alpha,
                rows: rows
            ),
            IdentityPalette identity => WriteIdentity(identity, rows),
            null => throw new UnreachableException("Pack gives every deforming mesh a palette."),
        };
        float morphReach = WriteWeights(
            previous: previousWeights,
            current: weights,
            alpha: alpha,
            rows: rows.Slice(3 * PaletteJoints, (Morphs + 3) / 4)
        );
        return jointReach + morphReach;
    }

    /// <summary>
    /// One vertex after its morphs and its skin, as deform.comp writes it: <paramref name="deltas"/> are the vertex's own
    /// deltas, and <paramref name="rows"/> the job's pose rows of <paramref name="paletteJoints"/> palette joints.
    /// </summary>
    public static Vertex Deformed(
        in GpuDeformVertex source,
        ReadOnlySpan<GpuMorphDelta> deltas,
        ReadOnlySpan<Vector4> rows,
        int paletteJoints
    )
    {
        ReadOnlySpan<float> weights = MemoryMarshal.Cast<Vector4, float>(rows[(3 * paletteJoints)..]);
        Vector3 p = source.Position,
            n = source.Normal,
            t = new(source.Tangent.X, source.Tangent.Y, source.Tangent.Z);
        // Morph order, then slot order, as in the shader: a float sum depends on its order.
        foreach (ref readonly GpuMorphDelta delta in deltas)
        {
            float w = weights[(int)delta.Morph];
            p += w * delta.Position;
            n += w * delta.Normal;
            t += w * delta.Tangent;
        }

        Vector3 sp = Vector3.Zero,
            sn = Vector3.Zero,
            st = Vector3.Zero;
        for (int slot = 0; slot < 4; slot++)
        {
            int r = 3 * JointOf(source, slot);
            float a = source.Weights[slot];
            Vector4 row0 = rows[r],
                row1 = rows[r + 1],
                row2 = rows[r + 2];
            var point = new Vector4(p, 1);
            sp += a * new Vector3(Vector4.Dot(row0, point), Vector4.Dot(row1, point), Vector4.Dot(row2, point));
            sn += a * new Vector3(Dot3(row0, n), Dot3(row1, n), Dot3(row2, n));
            st += a * new Vector3(Dot3(row0, t), Dot3(row1, t), Dot3(row2, t));
        }

        return new()
        {
            Position = sp,
            Normal = NormalizedOr(sn),
            Tangent = new(NormalizedOr(st), source.Tangent.W),
            TexCoord = source.TexCoord,
        };
    }

    private static float Dot3(Vector4 row, Vector3 v) => row.X * v.X + row.Y * v.Y + row.Z * v.Z;

    /// <summary>The joint index of <paramref name="slot"/>, unpacked from its 16-bit half.</summary>
    private static int JointOf(in GpuDeformVertex source, int slot)
    {
        uint word = slot < 2 ? source.Joints01 : source.Joints23;
        return (int)((word >> (16 * (slot & 1))) & 0xFFFF);
    }

    /// <summary><paramref name="v"/> at unit length, or zero below the smallest normal float, as `normalizedOr` of Shaders/vectors.glsl.</summary>
    private static Vector3 NormalizedOr(Vector3 v)
    {
        float lengthSquared = Vector3.Dot(v, v);
        return lengthSquared >= ShaderInput.SmallestNormal ? v * (1 / MathF.Sqrt(lengthSquared)) : Vector3.Zero;
    }

    /// <summary>Writes the skin poses, parents first, and returns the largest reach of an influence sphere.</summary>
    private float WritePalette(
        in SkinnedPalette skinned,
        ReadOnlySpan<Pose> previousJoints,
        ReadOnlySpan<Pose> joints,
        float alpha,
        Span<Vector4> rows
    )
    {
        Pose[]? rented = Joints > StackJoints ? ArrayPool<Pose>.Shared.Rent(Joints) : null;
        // The rig's own count, not StackJoints: the runtime zeroes every stack byte, which doubled the time of a 2-joint rig.
        Span<Pose> model = rented is null ? stackalloc Pose[Joints] : rented;
        ImmutableArray<InfluenceSphere> spheres = skinned.Spheres;
        float reach = 0;
        bool finite = true;
        int sphere = 0;
        // Parents first, the rig's order: a joint's model pose composes its parent's.
        for (int joint = 0; joint < Joints; joint++)
        {
            Pose local = Interpolated(previousJoints[joint], joints[joint], alpha);
            model[joint] = skinned.Parents[joint] is { } parent ? Pose.Compose(model[parent], local) : local;
            var skinPose = Pose.Compose(model[joint], skinned.InverseBinds[joint]);
            WriteRows(skinPose, rows.Slice(3 * joint, 3));
            finite &= ShaderInput.IsFinite(skinPose.Position);
            if (sphere < spheres.Length && spheres[sphere].Joint == joint)
            {
                InfluenceSphere each = spheres[sphere++];
                reach = MathF.Max(reach, skinPose.TransformPoint(each.Center).Length() + each.Radius);
            }
        }

        if (rented is not null)
            ArrayPool<Pose>.Shared.Return(rented);
        // A joint that moves no vertex still sits in a slot of weight 0, and 0 times a NaN is a NaN.
        return finite ? reach : float.PositiveInfinity;
    }

    /// <summary>Writes the one identity joint of a mesh without a skin, and returns its reach, the mesh's radius.</summary>
    private static float WriteIdentity(in IdentityPalette identity, Span<Vector4> rows)
    {
        rows[0] = new(x: 1, y: 0, z: 0, w: 0);
        rows[1] = new(x: 0, y: 1, z: 0, w: 0);
        rows[2] = new(x: 0, y: 0, z: 1, w: 0);
        return identity.Radius;
    }

    /// <summary>Writes the weights four to a row, zero after the last, and returns the sum of each |weight| times its longest delta.</summary>
    private float WriteWeights(
        ReadOnlySpan<float> previous,
        ReadOnlySpan<float> current,
        float alpha,
        Span<Vector4> rows
    )
    {
        rows.Clear();
        Span<float> slots = MemoryMarshal.Cast<Vector4, float>(rows);
        float reach = 0;
        for (int morph = 0; morph < Morphs; morph++)
        {
            float before = previous[morph],
                now = current[morph];
            // Equal ends give the current value's bits at every alpha, so a still rig gives the same rows in every frame.
            float weight = ShaderInput.MorphWeight(before == now ? now : before + (now - before) * alpha);
            slots[morph] = weight;
            reach += MathF.Abs(weight) * longestDeltas[morph];
        }

        return reach;
    }

    /// <summary>The joint pose at <paramref name="alpha"/>: the current value where both ends are equal, so a still joint keeps its bits.</summary>
    private static Pose Interpolated(in Pose previous, in Pose current, float alpha)
    {
        Vector3 position =
            previous.Position == current.Position
                ? current.Position
                : Vector3.Lerp(previous.Position, current.Position, alpha);
        Quaternion rotation =
            previous.Rotation == current.Rotation
                ? SceneBatcher.Interpolate(current.Rotation, current.Rotation, 1)
                : SceneBatcher.Interpolate(previous.Rotation, current.Rotation, alpha);
        return new(position, rotation);
    }

    /// <summary>Writes <paramref name="pose"/> as three rows (R row r, position r), the layout of GpuInstance's model rows.</summary>
    private static void WriteRows(in Pose pose, Span<Vector4> rows)
    {
        Quaternion q = pose.Rotation;
        float xx = q.X * q.X,
            yy = q.Y * q.Y,
            zz = q.Z * q.Z,
            xy = q.X * q.Y,
            xz = q.X * q.Z,
            yz = q.Y * q.Z,
            wx = q.W * q.X,
            wy = q.W * q.Y,
            wz = q.W * q.Z;
        Vector3 position = pose.Position;
        rows[0] = new(x: 1 - 2 * (yy + zz), y: 2 * (xy - wz), z: 2 * (xz + wy), w: position.X);
        rows[1] = new(x: 2 * (xy + wz), y: 1 - 2 * (xx + zz), z: 2 * (yz - wx), w: position.Y);
        rows[2] = new(x: 2 * (xz - wy), y: 2 * (yz + wx), z: 1 - 2 * (xx + yy), w: position.Z);
    }

    /// <summary>Adds the deltas of <paramref name="target"/> for one vertex, unless all three are exactly zero.</summary>
    private static void AddDelta(MorphTarget target, int vertex, List<GpuMorphDelta> deltas)
    {
        Vector3 position = At(target.Positions, vertex),
            normal = At(target.Normals, vertex),
            tangent = At(target.Tangents, vertex);
        bool moves = position != Vector3.Zero || normal != Vector3.Zero || tangent != Vector3.Zero;
        if (moves)
            deltas.Add(
                new()
                {
                    Position = position,
                    Morph = (uint)target.Morph,
                    Normal = normal,
                    Tangent = tangent,
                }
            );
        return;

        // An empty array means all zero.
        static Vector3 At(Vector3[] each, int index) => each.Length == 0 ? Vector3.Zero : each[index];
    }

    /// <summary>A staged vertex with its morph list and its skin, or one identity joint for a mesh without a skin.</summary>
    private static GpuDeformVertex Packed(in Vertex vertex, VertexSkin? skin, int morphStart, int morphCount)
    {
        (uint joints01, uint joints23, Vector4 weights) = skin switch
        {
            { } each => (
                each.Joint0 | ((uint)each.Joint1 << 16),
                each.Joint2 | ((uint)each.Joint3 << 16),
                SummingToOne(each.Weights)
            ),
            null => (0u, 0u, new Vector4(x: 1, y: 0, z: 0, w: 0)),
        };
        return new()
        {
            Position = vertex.Position,
            MorphStart = (uint)morphStart,
            Normal = vertex.Normal,
            MorphCount = (uint)morphCount,
            Tangent = vertex.Tangent,
            TexCoord = vertex.TexCoord,
            Joints01 = joints01,
            Joints23 = joints23,
            Weights = weights,
        };
    }

    /// <summary>The weights scaled to sum to 1, in doubles, so that no sum of large weights overflows.</summary>
    private static Vector4 SummingToOne(Vector4 weights)
    {
        double sum = (double)weights.X + weights.Y + weights.Z + weights.W;
        return new(
            x: (float)(weights.X / sum),
            y: (float)(weights.Y / sum),
            z: (float)(weights.Z / sum),
            w: (float)(weights.W / sum)
        );
    }

    /// <summary>
    /// Each joint's influence sphere: the center of the bounding box of the bind positions with a weight above 0 on it,
    /// and the largest distance from that center. A joint that moves no vertex has none.
    /// </summary>
    private static ImmutableArray<InfluenceSphere> Spheres(Vertex[] vertices, MeshSkin skin, int joints)
    {
        var min = new Vector3[joints];
        var max = new Vector3[joints];
        bool[] moved = new bool[joints];
        for (int index = 0; index < vertices.Length; index++)
            foreach (int joint in Influences(skin.Vertices[index]))
            {
                Vector3 position = vertices[index].Position;
                min[joint] = moved[joint] ? Vector3.Min(min[joint], position) : position;
                max[joint] = moved[joint] ? Vector3.Max(max[joint], position) : position;
                moved[joint] = true;
            }

        float[] radii = new float[joints];
        for (int index = 0; index < vertices.Length; index++)
            foreach (int joint in Influences(skin.Vertices[index]))
            {
                Vector3 center = (min[joint] + max[joint]) / 2;
                radii[joint] = MathF.Max(radii[joint], Vector3.Distance(vertices[index].Position, center));
            }

        return
        [
            .. Enumerable
                .Range(0, joints)
                .Where(joint => moved[joint])
                .Select(joint => new InfluenceSphere(joint, (min[joint] + max[joint]) / 2, radii[joint])),
        ];
    }

    /// <summary>The joints of the slots with a weight above 0.</summary>
    private static IEnumerable<int> Influences(VertexSkin skin)
    {
        if (skin.Weights.X > 0)
            yield return skin.Joint0;
        if (skin.Weights.Y > 0)
            yield return skin.Joint1;
        if (skin.Weights.Z > 0)
            yield return skin.Joint2;
        if (skin.Weights.W > 0)
            yield return skin.Joint3;
    }

    /// <summary>The longest position delta of each morph of the rig.</summary>
    private static ImmutableArray<float> LongestDeltas(MeshRigging rigging)
    {
        float[] longest = new float[rigging.Rig.Morphs.Length];
        foreach (MorphTarget target in rigging.Morphs)
        foreach (Vector3 delta in target.Positions)
            longest[target.Morph] = MathF.Max(longest[target.Morph], delta.Length());
        return [.. longest];
    }

    /// <summary>
    /// The palette of a mesh with a skin, one skin pose per rig joint: each joint's parent and inverse bind, in the rig's
    /// order, and the influence spheres in joint order, one for each joint that moves a vertex.
    /// </summary>
    private readonly record struct SkinnedPalette(
        ImmutableArray<int?> Parents,
        ImmutableArray<Pose> InverseBinds,
        ImmutableArray<InfluenceSphere> Spheres
    );

    /// <summary>The palette of a mesh without a skin: one identity joint, and <paramref name="Radius"/>, the mesh's radius about its origin.</summary>
    private readonly record struct IdentityPalette(float Radius);

    /// <summary>A mesh's palette: a <see cref="SkinnedPalette"/> or an <see cref="IdentityPalette"/>.</summary>
    [GeneratedUnion(typeof(SkinnedPalette), typeof(IdentityPalette))]
    private readonly partial struct Palette;

    /// <summary>A sphere in model space at the bind that holds every vertex with a weight above 0 on its joint.</summary>
    private readonly record struct InfluenceSphere(int Joint, Vector3 Center, float Radius);
}

/// <summary>A deforming mesh packed for the deform store: its <see cref="MeshDeform"/>, its deform vertices and its deltas.</summary>
internal sealed record PackedDeform(MeshDeform Deform, GpuDeformVertex[] Vertices, GpuMorphDelta[] Deltas);

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

// C# mirrors of the structs of Shaders/deform.comp, in std430 (docs/plans/skinning-and-morphs.md, "The layouts", and its
// change by ter-3yy5, which added the card). Change both sides together; GpuDeformTypesTests checks the sizes and the
// offsets.

/// <summary>
/// A vertex of a deforming mesh as uploaded, with its morph list, its skin and its card number. 84 bytes of fields,
/// padded to 96, std430's alignment of the struct, 16.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 96)]
internal struct GpuDeformVertex
{
    public Vector3 Position;

    /// <summary>The vertex's first delta, counted from its mesh's first delta.</summary>
    public uint MorphStart;

    /// <summary>The unit normal, or zero.</summary>
    public Vector3 Normal;

    /// <summary>The number of the vertex's deltas, which come in morph order.</summary>
    public uint MorphCount;

    /// <summary>The unit tangent or zero in xyz, the bitangent sign in w.</summary>
    public Vector4 Tangent;

    public Vector2 TexCoord;

    /// <summary>The joints of slots 0 and 1: slot 0 in the low 16 bits.</summary>
    public uint Joints01;

    /// <summary>The joints of slots 2 and 3: slot 2 in the low 16 bits.</summary>
    public uint Joints23;

    /// <summary>The weights of the four slots, each at least 0, with a sum of 1.</summary>
    public Vector4 Weights;

    /// <summary>The number of the vertex's card, <see cref="GpuVertex.Card"/>, which the deformed copy keeps.</summary>
    public uint Card;
}

/// <summary>One morph's deltas for one vertex, and its morph's index. 48 bytes: std430 pads each vec3 to 16.</summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
internal struct GpuMorphDelta
{
    [FieldOffset(0)]
    public Vector3 Position;

    /// <summary>The index of the delta's morph in the rig, which picks its weight.</summary>
    [FieldOffset(12)]
    public uint Morph;

    [FieldOffset(16)]
    public Vector3 Normal;

    [FieldOffset(32)]
    public Vector3 Tangent;
}

/// <summary>
/// One job of the deform pass's job table, the GPU form of a <see cref="DeformJob"/>. 28 bytes
/// (docs/plans/skinning-and-morphs.md, "The layouts").
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuDeformJob
{
    /// <summary>The mesh's first deform vertex in the deform store.</summary>
    public uint Source;

    /// <summary>The mesh's first morph delta in the deform store.</summary>
    public uint Deltas;

    /// <summary>The copy's first vertex, counted from the deform region's base.</summary>
    public uint Target;

    /// <summary>The mesh's vertex count.</summary>
    public uint Count;

    /// <summary>The job's first pose row.</summary>
    public uint Palette;

    /// <summary>The job's first weight row: <see cref="Palette"/> plus three rows per palette joint.</summary>
    public uint Weights;

    /// <summary>The job's first workgroup of the frame's dispatch: the workgroups of every job before it.</summary>
    public uint FirstGroup;

    /// <summary>
    /// Writes the record of each of <paramref name="jobs"/> into <paramref name="records"/>, each job's workgroups after
    /// those of the job before it, and gives the frame's workgroups.
    /// </summary>
    /// <param name="meshes">The static meshes that the jobs name.</param>
    /// <param name="records">At least as long as <paramref name="jobs"/>.</param>
    public static uint FromJobs(ReadOnlySpan<DeformJob> jobs, ReadOnlySpan<MeshInfo> meshes, Span<GpuDeformJob> records)
    {
        uint groups = 0;
        for (int i = 0; i < jobs.Length; i++)
        {
            DeformJob job = jobs[i];
            MeshDeform deform =
                meshes[job.Mesh].Deform ?? throw new UnreachableException("Only a deforming mesh has a job.");
            records[i] = new()
            {
                Source = (uint)deform.Source,
                Deltas = (uint)deform.Deltas,
                Target = (uint)job.Target,
                Count = (uint)deform.VertexCount,
                Palette = (uint)job.Pose,
                Weights = (uint)(job.Pose + 3 * deform.PaletteJoints),
                FirstGroup = groups,
            };
            groups += DeformPass.Groups((uint)deform.VertexCount);
        }

        return groups;
    }
}

/// <summary>The push constants of the deform pass's one dispatch. 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DeformPushConstants
{
    /// <summary>The records of the job table.</summary>
    public uint JobCount;

    /// <summary>The frame's workgroups: those of the grid past them return at once.</summary>
    public uint Groups;
}

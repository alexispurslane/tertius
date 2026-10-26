using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

// C# mirrors of the structs of Shaders/deform.comp, in std430 (docs/plans/skinning-and-morphs.md, "The layouts"). Change
// both sides together; GpuDeformTypesTests checks the sizes and the offsets.

/// <summary>A vertex of a deforming mesh as uploaded, with its morph list and its skin. 80 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
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

/// <summary>The push constants of one dispatch of the deform pass, one job. 24 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DeformPushConstants
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
}

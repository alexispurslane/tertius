using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# mirrors of the deform pass's structs keep the std430 layouts of deform.comp
/// (docs/plans/skinning-and-morphs.md, "The layouts").
/// </summary>
public sealed class GpuDeformTypesTests
{
    private static int Offset<T>(string field) => (int)Marshal.OffsetOf<T>(field);

    [Fact]
    public void GpuDeformVertexIs96Bytes()
    {
        Unsafe.SizeOf<GpuDeformVertex>().Should().Be(96, "std430 rounds 84 bytes of members up to the vec4's 16");
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Position)).Should().Be(0);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.MorphStart)).Should().Be(12);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Normal)).Should().Be(16);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.MorphCount)).Should().Be(28);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Tangent)).Should().Be(32);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.TexCoord)).Should().Be(48);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Joints01)).Should().Be(56);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Joints23)).Should().Be(60);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Weights)).Should().Be(64);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Card)).Should().Be(80);
    }

    [Fact]
    public void GpuMorphDeltaIs48Bytes()
    {
        Unsafe.SizeOf<GpuMorphDelta>().Should().Be(48);
        Offset<GpuMorphDelta>(nameof(GpuMorphDelta.Position)).Should().Be(0);
        Offset<GpuMorphDelta>(nameof(GpuMorphDelta.Morph)).Should().Be(12);
        Offset<GpuMorphDelta>(nameof(GpuMorphDelta.Normal)).Should().Be(16);
        Offset<GpuMorphDelta>(nameof(GpuMorphDelta.Tangent)).Should().Be(32);
    }

    [Fact]
    public void GpuDeformJobIs28Bytes()
    {
        Unsafe.SizeOf<GpuDeformJob>().Should().Be(28);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.Source)).Should().Be(0);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.Deltas)).Should().Be(4);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.Target)).Should().Be(8);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.Count)).Should().Be(12);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.Palette)).Should().Be(16);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.Weights)).Should().Be(20);
        Offset<GpuDeformJob>(nameof(GpuDeformJob.FirstGroup)).Should().Be(24);
    }

    [Fact]
    public void DeformPushConstantsAre8Bytes()
    {
        Unsafe.SizeOf<DeformPushConstants>().Should().Be(8);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.JobCount)).Should().Be(0);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Groups)).Should().Be(4);
    }
}

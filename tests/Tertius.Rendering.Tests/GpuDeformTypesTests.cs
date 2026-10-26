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
    public void GpuDeformVertexIs80Bytes()
    {
        Unsafe.SizeOf<GpuDeformVertex>().Should().Be(80);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Position)).Should().Be(0);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.MorphStart)).Should().Be(12);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Normal)).Should().Be(16);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.MorphCount)).Should().Be(28);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Tangent)).Should().Be(32);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.TexCoord)).Should().Be(48);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Joints01)).Should().Be(56);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Joints23)).Should().Be(60);
        Offset<GpuDeformVertex>(nameof(GpuDeformVertex.Weights)).Should().Be(64);
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
    public void DeformPushConstantsAre24Bytes()
    {
        Unsafe.SizeOf<DeformPushConstants>().Should().Be(24);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Source)).Should().Be(0);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Deltas)).Should().Be(4);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Target)).Should().Be(8);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Count)).Should().Be(12);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Palette)).Should().Be(16);
        Offset<DeformPushConstants>(nameof(DeformPushConstants.Weights)).Should().Be(20);
    }
}

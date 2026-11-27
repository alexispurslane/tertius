namespace Tertius.Rendering.Tests;

/// <summary>The compiled mesh entry points declare only the motion-history inputs their consumers use.</summary>
public sealed class MeshVertexShaderTests
{
    [Fact]
    public void AMeshThatWritesNoMotionDeclaresNoHistoryBufferRead()
    {
        byte[] shader = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, "mesh_nomotion.vert");

        Spirv
            .BindingNumbersOfSet(shader, 0)
            .Should()
            .NotContain([DescriptorLayouts.MotionsBinding, DescriptorLayouts.KeptPositionsBinding]);
    }

    [Fact]
    public void TheGBufferMeshRetainsItsMotionRowsAndKeptPositionReads()
    {
        byte[] shader = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, "mesh.vert");

        Spirv
            .BindingNumbersOfSet(shader, 0)
            .Should()
            .Contain([DescriptorLayouts.MotionsBinding, DescriptorLayouts.KeptPositionsBinding]);
    }
}

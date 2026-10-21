using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>The blend state that each <see cref="BlendMode"/> gives a color attachment. No GPU needed.</summary>
public sealed class BlendModeTests
{
    private const ColorComponentFlags AllChannels =
        ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit;

    [Fact]
    public void NoBlendReplacesTheTarget()
    {
        PipelineColorBlendAttachmentState state = GraphicsPipelineDesc.BlendState(BlendMode.None);

        ((bool)state.BlendEnable).Should().BeFalse();
        state.ColorWriteMask.Should().Be(AllChannels);
    }

    [Fact]
    public void TheAlphaBlendMixesTheColorByTheSourceAlpha()
    {
        PipelineColorBlendAttachmentState state = GraphicsPipelineDesc.BlendState(BlendMode.Alpha);

        ((bool)state.BlendEnable).Should().BeTrue();
        (state.SrcColorBlendFactor, state.DstColorBlendFactor, state.ColorBlendOp)
            .Should().Be((BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOp.Add));
        (state.SrcAlphaBlendFactor, state.DstAlphaBlendFactor, state.AlphaBlendOp)
            .Should().Be((BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOp.Add));
        state.ColorWriteMask.Should().Be(AllChannels);
    }

    [Fact]
    public void TheAdditiveBlendAddsTheSourceColorAndKeepsTheTargetsAlpha()
    {
        PipelineColorBlendAttachmentState state = GraphicsPipelineDesc.BlendState(BlendMode.Additive);

        ((bool)state.BlendEnable).Should().BeTrue();
        (state.SrcColorBlendFactor, state.DstColorBlendFactor, state.ColorBlendOp)
            .Should().Be((BlendFactor.One, BlendFactor.One, BlendOp.Add));
        (state.SrcAlphaBlendFactor, state.DstAlphaBlendFactor, state.AlphaBlendOp)
            .Should().Be((BlendFactor.Zero, BlendFactor.One, BlendOp.Add));
        state.ColorWriteMask.Should().Be(AllChannels);
    }
}

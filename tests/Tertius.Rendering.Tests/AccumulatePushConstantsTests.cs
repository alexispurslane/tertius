namespace Tertius.Rendering.Tests;

/// <summary>
/// The push constants of an accumulation (<see cref="AccumulatePushConstants"/>): the draw's index since the last reset
/// reaches the shaders twice, as the full index modulo 2^32 for the fix's stencils (t1-3er4) and as the ring layer of the
/// region's drift test.
/// </summary>
public sealed class AccumulatePushConstantsTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(37L)]
    [InlineData(int.MaxValue + 5L)]
    [InlineData(uint.MaxValue + 3L)]
    public void ThePushConstantsCarryTheDrawIndexModuloTwoToTheThirtyTwoAndItsRingLayer(long drawIndex)
    {
        var push = AccumulatePushConstants.Of(
            parameters: SignalParameters.Specular(0.4f),
            reset: drawIndex == 0,
            drawIndex: drawIndex
        );

        push.DrawIndex.Should().Be(unchecked((uint)drawIndex));
        push.RegionSlot.Should().Be((uint)(drawIndex % TemporalSignal.RegionRingDraws));
    }
}

namespace Tertius.Rendering.Tests;

/// <summary>Black and subnormal scene luminance share a finite floor on CPUs and GPUs that flush subnormals.</summary>
[Collection(GpuCollection.Name)]
public sealed class SceneMeterFloatTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x00400000u)]
    [InlineData(0x007fffffu)]
    public void BlackAndPositiveSubnormalLuminancesUseTheBlackBin(uint bits) =>
        ExposureMeter.BinOf(BitConverter.UInt32BitsToSingle(bits)).Should().Be(0);

    [Fact]
    public void TheGpuKeepsFiniteBinsFromBlackThroughTheLargestSceneLuminance()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 16,
            height: 16,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            ScreenTraceProbe probe = ScreenTraceProbe.On(renderer, "scene_meter_probe.comp");
            renderer.Render(Gpu.Snapshot(System.Numerics.Vector3.UnitZ), 1f);

            probe
                .Read()
                .Should()
                .OnlyContain(
                    value => value == 1,
                    "black, preserved or flushed subnormals, and normal endpoints must use the specified bins"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

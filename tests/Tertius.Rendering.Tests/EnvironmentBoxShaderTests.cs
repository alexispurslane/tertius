using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>Real Vulkan execution of analytic enclosure lookup, including finite directional fallbacks.</summary>
[Collection(GpuCollection.Name)]
public sealed class EnvironmentBoxShaderTests
{
    [Theory]
    [InlineData(0, 1, 1, -4)]
    [InlineData(1, 1, 1, 4)]
    [InlineData(2, 1, 1, -4)]
    [InlineData(3, 1, 0, 0)]
    [InlineData(4, 0, 0, -1)]
    [InlineData(5, 0, 0, -1)]
    [InlineData(6, 0, 0, -1)]
    [InlineData(7, 0, 0, -1)]
    [InlineData(8, 0, 0, -1)]
    [InlineData(9, 0, 0, -1)]
    [InlineData(10, 0, 0, -1)]
    [InlineData(11, 0, 0, -1)]
    [InlineData(12, 2, 1, -4)]
    [InlineData(13, 0, 0, -1)]
    [InlineData(14, 0, 0, 1)]
    [InlineData(15, 0, 0, 1)]
    [InlineData(16, 1, 1, -4)]
    [InlineData(17, 0, 0, -1)]
    [InlineData(19, -1, -0.1f, -1)]
    public void TheLookupUsesTheForwardIntersectionOrTheFiniteOriginalDirection(
        int scenario,
        float expectedX,
        float expectedY,
        float expectedZ
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 64,
            height: 36,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "environment_box_probe.comp");
            renderer.Render(Gpu.Snapshot(Vector3.UnitY), 1);
            float[] values = probe.Read();
            var actual = new Vector3(values[scenario * 4], values[scenario * 4 + 1], values[scenario * 4 + 2]);
            Vector3
                .Distance(actual, Vector3.Normalize(new(expectedX, expectedY, expectedZ)))
                .Should()
                .BeLessThan(1f / 1024 + 0.000001f, "RGBA16F readback can round each component by one half-float step");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACommonRigidTurnPreservesTheLookupInTheMapsAxes()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 64,
            height: 36,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = 32,
            }
        );
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "environment_box_probe.comp");
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY);
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            snapshot.Chain.Add(new(1, Quaternion.Identity, Quaternion.Identity));
            snapshot.Chain.Add(new(2, turn, turn));
            renderer.Render(snapshot, 1);
            float[] values = probe.Read();
            var actual = new Vector3(values[18 * 4], values[18 * 4 + 1], values[18 * 4 + 2]);

            Vector3.Distance(actual, Vector3.Normalize(new(1, 1, -4))).Should().BeLessThan(1f / 1024 + 0.000001f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

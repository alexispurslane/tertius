using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>The real shader's exclusive viewport bounds and finite ray segment stop the portable walk.</summary>
[Collection(GpuCollection.Name)]
public sealed class ScreenWalkBoundaryTests
{
    private const int Width = 64,
        Height = 36;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    [InlineData(5, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 0)]
    public void TheShaderKeepsOnlyCellsInsideBothExclusiveViewportBounds(int channel, int expected) =>
        Values()[channel].Should().Be(expected);

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    public void TheShaderHitsTheNextWallPixelOnlyWithinItsSegmentAndStepCap(int channel, int expected) =>
        Values()[4 * (Height / 2 * Width + Width / 2) + channel].Should().Be(expected);

    private static float[] Values()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitY);
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f)),
                    position: new(0, 0, -20),
                    scale: new(60, 40, 1),
                    chainDepth: TestChains.CamerasFrame
                )
            );
            var probe = ScreenTraceProbe.On(renderer, "screen_walk_boundary_behavior_probe.comp");

            renderer.Render(scene, 1f);

            return probe.Read();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

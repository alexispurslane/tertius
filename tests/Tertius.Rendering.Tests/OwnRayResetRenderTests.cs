using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>A populated frame followed by empty sky must not retain either kind's own-ray count.</summary>
[Collection(GpuCollection.Name)]
public sealed class OwnRayResetRenderTests
{
    [Fact]
    public void ASkyFrameClearsTheOwnRayCountsOfThePreviousSurfaceFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 64,
            height: 36,
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
            renderer.Render(scene, 1f);
            (int reflection, int diffuse) = renderer.ReadOwnRayCounts();
            reflection.Should().BeGreaterThan(0);
            diffuse.Should().BeGreaterThan(0);
            scene.Instances.Clear();
            scene.Time += 1.0 / 60;

            renderer.Render(scene, 1f);

            renderer.ReadOwnRayCounts().Should().Be((0, 0));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

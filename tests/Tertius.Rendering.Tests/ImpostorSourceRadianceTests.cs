using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>The persistent atlas retains bright scene radiance before display exposure.</summary>
[Collection(GpuCollection.Name)]
public sealed class ImpostorSourceRadianceTests
{
    [Fact]
    public void AnImpostorCaptureRetainsEmissionAboveTheHalfFloatRange()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 90,
            impostors: true,
            exposure: new FixedExposure(-20),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = ReflectionTesting.Dark();
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                    material: BrdfTesting.Glow(renderer, new(262144)),
                    position: new(0, 0, -12),
                    scale: Vector3.One,
                    entity: 1,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            for (int frame = 0; frame < 3; frame++)
                renderer.Render(scene, 1f);

            renderer.LastFrameStats.Impostors.Should().Be(1, "the captured sphere is drawn as a billboard");
            float[] tile = renderer.ReadImpostorTile(0).Rgba;
            int center = 4 * (256 * 512 + 256);
            tile[center + 3].Should().Be(1, "the center is entirely covered by the sphere");
            tile[center].Should().BeApproximately(262144, 1, "the atlas stores scene emission before display exposure");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

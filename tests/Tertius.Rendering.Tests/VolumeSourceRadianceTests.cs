namespace Tertius.Rendering.Tests;

/// <summary>A white metal reflects the volume's incoming broad radiance with unit albedo.</summary>
[Collection(GpuCollection.Name)]
public sealed class VolumeSourceRadianceTests
{
    [Fact]
    public void AWhiteMetalReturnsTheBroadRadianceArrivingAtItsSurface()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 64,
            height: 64,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            lightVolume: true
        );
        try
        {
            var incident = ScreenTraceProbe.On(renderer, "volume_incident_probe.comp");
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            RenderSnapshot scene = ReflectionTesting.Dark();
            scene.Instances.Add(
                ReflectionTesting.Block(
                    box: box,
                    material: renderer.CreateMaterial(ReflectionTesting.Mirror(roughness: 1)),
                    center: new(0, 0, -1.5f),
                    size: new(3, 3, 0.08f)
                )
            );
            scene.Instances.Add(
                ReflectionTesting.Block(
                    box: box,
                    material: BrdfTesting.Glow(renderer, new(8, 0, 0)),
                    center: new(0, 0, 0.75f),
                    size: new(2, 2, 0.08f)
                )
            );
            renderer.Render(scene, 1f);

            int center = 4 * (32 * 64 + 32);
            float arriving = incident.Read()[center];
            arriving.Should().BeGreaterThan(0.005f, "the offscreen emitter supplies broad reflected light");
            renderer
                .ReadHdr()[center]
                .Should()
                .BeApproximately(
                    arriving,
                    arriving * 0.01f,
                    "a white metal has unit albedo and no diffuse term, so it returns the incoming radiance once"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

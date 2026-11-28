using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>A traced hit retains its surface's scene radiance independently of camera exposure.</summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientReflectionSourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-64)]
    [InlineData(64)]
    public void AMirrorSampleKeepsTheAmbientLightOfTheSurfaceItHits(float stops)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            exposure: new FixedExposure(stops),
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            lightVolume: false
        );
        try
        {
            var center = new Vector3(0, -0.5f, -12);
            RenderSnapshot scene = Gray(0.5f);
            scene.Instances.Add(Floor(renderer, Mirror()));
            scene.Instances.Add(
                Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0)),
                    center: center,
                    size: new(3, 2, 0.2f)
                )
            );
            scene.Time += 1.0 / 60;

            renderer.Render(scene, 1f);

            float source = At(renderer.ReadHdr(), PixelOf(center)).X;
            float sample = At(renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0)), MirrorPixel(center)).X;
            source.Should().BeGreaterThan(0.4f, "the panel is lit by the environment without emission or direct light");
            sample
                .Should()
                .BeApproximately(
                    source,
                    0.05f * source,
                    "a mirror hit carries the surface's scene radiance independently of camera exposure"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

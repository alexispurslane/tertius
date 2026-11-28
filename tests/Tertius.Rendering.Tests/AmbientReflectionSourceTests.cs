using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>A traced hit converts the ambient-lit surface from scene radiance into the sample's exposure encoding once.</summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientReflectionSourceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
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
                    source * MathF.Pow(2, stops),
                    0.05f * source * MathF.Pow(2, stops),
                    "a mirror hit carries the surface's ambient radiance with exactly its producing exposure"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

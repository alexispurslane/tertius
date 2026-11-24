using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The resolve's weight of a block's reflection ray at a pixel that shares it: the pixel's own density along the ray, not
/// the density that the tracing pixel recorded (ticket ter-xo83), and for a guided ray the pixel's own mixture at the share
/// that drew the ray (ter-fe63).
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ResolveSharedReflectionTests
{
    /// <summary>A roughness of about 0.2 that 8 bits hold, below the hand-over band: the specular sample is the reflection ray's alone.</summary>
    private const float Roughness = 51 / 255f;

    [Theory]
    [InlineData("shared_reflection_setup_low.comp")]
    [InlineData("shared_reflection_setup_high.comp")]
    public void APixelWeighsASharedReflectionRayAsItsOwnRayWhateverDensityTheTracerRecorded(string sharedSetup)
    {
        float[] own = SpecularSamples("shared_reflection_setup_own.comp");
        float[] shared = SpecularSamples(sharedSetup);

        int compared = 0;
        for (int at = 0; at < own.Length; at += 4)
        {
            if (own[at] <= 0)
                continue;
            for (int channel = 0; channel < 3; channel++)
                shared[at + channel]
                    .Should()
                    .BeApproximately(
                        own[at + channel],
                        0.005f * own[at + channel],
                        "the shared ray counts with the pixel's own density, which the own ray records to 1/256 stop"
                    );
            compared++;
        }
        compared.Should().BeGreaterThan(50_000, "the floor fills the lower half of the image");
    }

    [Fact]
    public void APixelWeighsAGuidedBlockRayByItsOwnMixtureAtTheShareThatDrewTheRay()
    {
        float[] own = SpecularSamples("shared_reflection_setup_own_guided.comp");
        float[] shared = SpecularSamples("shared_reflection_setup_guided.comp");

        int compared = 0;
        for (int at = 0; at < own.Length; at += 4)
        {
            if (own[at] <= 0)
                continue;
            shared[at]
                .Should()
                .BeApproximately(
                    own[at],
                    0.005f * own[at],
                    "the block's ray was drawn at a share of 1/8, which the pixel's mixture takes from the block guide, "
                        + "though the pixel's own share is 0 in this unlit scene"
                );
            compared++;
        }
        compared.Should().BeGreaterThan(50_000, "the floor fills the lower half of the image");
    }

    /// <summary>The specular sample of every pixel of the floor after the resolve of the rays that <paramref name="setup"/> writes.</summary>
    private static float[] SpecularSamples(string setup)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = Dark();
            scene.Instances.Add(Floor(renderer, Mirror(Roughness)));
            ScreenTraceProbe.On(renderer, setup);
            var resolve = ScreenTraceProbe.On(renderer, "screen_trace_resolve.comp");
            renderer.Render(scene, 1f);
            return resolve.Read();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

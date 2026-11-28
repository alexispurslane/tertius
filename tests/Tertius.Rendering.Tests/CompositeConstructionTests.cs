namespace Tertius.Rendering.Tests;

/// <summary>Failed composite construction releases each fallback image it already allocated, with Vulkan validation on.</summary>
[Collection(GpuCollection.Name)]
public sealed class CompositeConstructionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACompositeThatCannotFinishConstructionReleasesItsFallbackImages(bool failGuideAllocation)
    {
        VulkanRenderer renderer = Gpu.Headless(width: 32, height: 32, warmUpFrames: 0);
        try
        {
            var probe = new FailedConstruction(renderer.Allocator, failGuideAllocation);
            renderer.Graph.Add(FrameGraph.Stage.Composite, probe);

            renderer.Render(ExposureScenes.Sunlit(), 1);

            probe.Attempts.Should().Be(1, "the failure path must run on the real device");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private sealed class FailedConstruction(DeviceAllocator allocator, bool failGuideAllocation) : RenderPass
    {
        public int Attempts { get; private set; }
        public override string Name => "failed composite construction";

        public override void Record(FrameContext context) { }

        public override void Prepare(FrameContext context)
        {
            int live = allocator.LiveAllocations;
            using var shaders = new ShaderLibrary(
                context.Device,
                failGuideAllocation ? ShaderLibrary.EngineShaders : typeof(object).Assembly
            );
            // One allocation admits the scene-radiance fallback, then refuses the distance-guide fallback.
            // The shaderless assembly instead fails pipeline preparation after both fallbacks have been made.
            allocator.AllocationsBeforeFailure = failGuideAllocation ? 1 : null;
            try
            {
                Action make = () =>
                    new CompositePass(
                        device: context.Device,
                        allocator: allocator,
                        shaders: shaders,
                        layouts: context.Layouts,
                        constants: new ShaderConstants(EnvironmentMap: false),
                        scatter: false,
                        tracer: null,
                        targets: context.Targets
                    ).Dispose();
                if (failGuideAllocation)
                    make.Should().Throw<VulkanException>();
                else
                    make.Should().Throw<FileNotFoundException>().WithMessage("*lighting.vert*");
            }
            finally
            {
                allocator.AllocationsBeforeFailure = null;
            }
            allocator.LiveAllocations.Should().Be(live, "failed construction must free every completed fallback");
            allocator.AssertInvariants();
            Attempts++;
        }
    }
}

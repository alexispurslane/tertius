using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The image of the reflections' trace that <see cref="ReflectionTargets"/> holds by the trace's resolution, across a
/// resize, and the steps of <see cref="ReflectionPass"/> that it allows (docs/plans/screen-space-reflections.md, "The
/// half-resolution trace"). Validation is on.
/// </summary>
public sealed class ReflectionTraceTests
{
    [Fact]
    public void AResizeRemakesTheHalfResolutionTraceAtHalfTheNewSizeRoundedUp()
    {
        using var plumbing = new Plumbing(TraceResolution.Half);

        plumbing.Resize(new(801, 451));

        plumbing
            .Reflections.Trace.Value.Should()
            .BeOfType<HalfTrace>("the trace keeps its resolution")
            .Which.Traced.Extent.Should()
            .Be(new Extent2D(401, 226));
    }

    [Fact]
    public void AResizeKeepsTheFullResolutionTraceWithNoImageOfItsOwn()
    {
        using var plumbing = new Plumbing(TraceResolution.Full);

        plumbing.Resize(new(801, 451));

        plumbing.Reflections.Trace.Value.Should().BeOfType<FullTrace>("the trace keeps its resolution");
    }

    [Fact]
    public void AResizeOfTheHalfResolutionTraceReportsNothingToValidation()
    {
        ValidationLog log;
        using (var plumbing = new Plumbing(TraceResolution.Half))
        {
            log = plumbing.Device.Log;
            plumbing.Resize(new(801, 451));
        }

        log.Snapshot().Should().BeEmpty("the set must point at the new trace image, and the old one must be released");
    }

    [Fact]
    public void AnUpsampleOverAFullResolutionTraceIsRefusedWhenThePassIsMade()
    {
        using var plumbing = new Plumbing(TraceResolution.Full);
        using var shaders = new ShaderLibrary(plumbing.Device, ShaderLibrary.EngineShaders);

        FluentActions
            .Invoking(() =>
                new ReflectionPass(
                    device: plumbing.Device,
                    shaders: shaders,
                    layouts: plumbing.Layouts,
                    targets: plumbing.Reflections,
                    setting: ConeTracedReflections.Default,
                    constants: new ShaderConstants(EnvironmentMap: false),
                    step: ReflectionStep.Upsample
                )
            )
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage("*half resolution*");
    }

    /// <summary>A validating headless device with the descriptor layouts and the render targets of 640 × 360 with reflections.</summary>
    private sealed class Plumbing : IDisposable
    {
        public Plumbing(TraceResolution resolution)
        {
            Device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
            Allocator = new DeviceAllocator(Device);
            Layouts = new DescriptorLayouts(device: Device, framesInFlight: 2, glowLevels: 0);
            Targets = new RenderTargets(
                device: Device,
                allocator: Allocator,
                layouts: Layouts,
                extent: new(640, 360),
                glowLevels: 0,
                smaaTables: null,
                reflections: resolution
            );
        }

        public VulkanDevice Device { get; }
        public DeviceAllocator Allocator { get; }
        public DescriptorLayouts Layouts { get; }
        public RenderTargets Targets { get; }
        public ReflectionTargets Reflections => Targets.Reflections!;

        /// <summary>Resizes the targets, as the renderer does, with the device idle.</summary>
        public void Resize(Extent2D extent)
        {
            Device.WaitIdle();
            Targets.Resize(extent);
        }

        public void Dispose()
        {
            Targets.Dispose();
            Layouts.Dispose();
            Allocator.Dispose();
            Device.Dispose();
        }
    }
}

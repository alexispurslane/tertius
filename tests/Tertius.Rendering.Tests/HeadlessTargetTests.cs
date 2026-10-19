using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>The headless output: an offscreen image that stands in for a window.</summary>
[Collection(GpuCollection.Name)]
public sealed class HeadlessTargetTests
{
    [Fact]
    public void AResizeToTheSameSizeKeepsTheImage()
    {
        HeadlessTarget? made = null;
        VulkanRenderer renderer = VulkanRenderer.CreateOffscreen(
            new RendererOptions { Validation = true, Width = 64, Height = 32, Impostors = false },
            (device, allocator, extent) => made = new HeadlessTarget(device, allocator, extent, bgra: false));
        try
        {
            HeadlessTarget output = made!;
            // The headless output ignores the frame resources that Acquire takes.
            GpuImage before = output.Acquire(null!)!;

            output.Resize(output.Extent);

            output.Acquire(null!).Should().BeSameAs(before);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

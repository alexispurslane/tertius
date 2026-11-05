namespace Tertius.Rendering.Tests;

/// <summary>The headless output: an offscreen image that stands in for a window.</summary>
[Collection(GpuCollection.Name)]
public sealed class HeadlessTargetTests
{
    [Fact]
    public void AResizeToTheSameSizeKeepsTheImage()
    {
        HeadlessTarget? made = null;
        var renderer = VulkanRenderer.CreateOffscreen(
            new()
            {
                Validation = true,
                Width = 64,
                Bloom = new NoBloom(),
                Height = 32,
                Impostors = false,
                Upscaling = new NoUpscaling(),
            },
            (device, allocator, extent) => made = new(device: device, allocator: allocator, extent: extent, bgra: false)
        );
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

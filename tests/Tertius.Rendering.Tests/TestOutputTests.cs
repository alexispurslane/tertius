using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The test output is the headless output plus two test features, frame skips and refused captures. Everything else
/// is the headless output's own behavior, so a test through the test output exercises the real output.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class TestOutputTests
{
    [Fact]
    public void AResizeToTheSameSizeKeepsTheImageAsTheHeadlessOutputDoes()
    {
        TestOutput? made = null;
        VulkanRenderer renderer = VulkanRenderer.CreateOffscreen(
            new RendererOptions { Validation = true, Width = 64, Height = 32, Impostors = false },
            (device, allocator, extent) => made = new TestOutput(device, allocator, extent, Format.R8G8B8A8Srgb));
        try
        {
            TestOutput output = made!;
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

using Silk.NET.Vulkan;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Whether a window's swapchain can be captured. A capture copies the swapchain image and hands its sink 4 bytes per
/// pixel, red, green and blue with sRGB encoding, so the images must allow transfer reads and hold 8-bit color.
/// </summary>
public sealed class SwapchainCaptureTests
{
    private const ImageUsageFlags Readable = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit;

    [Theory]
    [InlineData(Format.B8G8R8A8Srgb)]
    [InlineData(Format.R8G8B8A8Srgb)]
    [InlineData(Format.B8G8R8A8Unorm)]
    [InlineData(Format.R8G8B8A8Unorm)]
    public void ASwapchainOfEightBitColorWhoseImagesAllowTransferReadsCanBeCaptured(Format format) =>
        SwapchainTarget.CanCaptureImages(Readable, format).Should().BeTrue();

    [Fact]
    public void ASwapchainWhoseImagesDoNotAllowTransferReadsCannotBeCaptured() =>
        SwapchainTarget.CanCaptureImages(ImageUsageFlags.ColorAttachmentBit, Format.B8G8R8A8Srgb).Should().BeFalse();

    [Theory]
    [InlineData(Format.A2B10G10R10UnormPack32)]
    [InlineData(Format.R16G16B16A16Sfloat)]
    public void ASwapchainWhoseFormatIsNotEightBitColorCannotBeCaptured(Format format) =>
        SwapchainTarget.CanCaptureImages(Readable, format).Should().BeFalse();
}

namespace Tertius.Rendering.Tests;

/// <summary>
/// What the motion target asks of a device and of the motion rows (docs/plans/temporal-renderer.md, "Portable Vulkan"):
/// the renderer refuses a device with fewer than 8 color attachments or fragment outputs, and a motion row is the 64 bytes
/// that the shaders read. No GPU needed.
/// </summary>
public sealed class MotionTargetTests
{
    [Fact]
    public void ADeviceWithEightColorAttachmentsDrawsTheGBuffer() =>
        RenderTargets.ColorAttachmentProblem(maxColorAttachments: 8, maxFragmentOutputAttachments: 8).Should().BeNull();

    [Fact]
    public void ADeviceWithFewerThanEightColorAttachmentsIsRefused() =>
        RenderTargets
            .ColorAttachmentProblem(maxColorAttachments: 7, maxFragmentOutputAttachments: 8)
            .Should()
            .Contain("maxColorAttachments");

    [Fact]
    public void ADeviceWhoseFragmentStageWritesFewerThanEightOutputsIsRefused() =>
        RenderTargets
            .ColorAttachmentProblem(maxColorAttachments: 8, maxFragmentOutputAttachments: 4)
            .Should()
            .Contain("maxFragmentOutputAttachments");

    [Fact]
    public void AMotionRowTakesSixtyFourBytes() =>
        System.Runtime.CompilerServices.Unsafe.SizeOf<GpuMotion>().Should().Be(64);
}

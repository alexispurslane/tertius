using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// What a device must offer for the surface record of an environment face (<see cref="FaceSurface"/>): the surfaces draw
/// its images, and the light pass fetches them.
/// </summary>
public sealed class FaceSurfaceTests
{
    private const FormatFeatureFlags RecordUses = FormatFeatureFlags.ColorAttachmentBit | FormatFeatureFlags.SampledImageBit;

    [Fact]
    public void ADeviceThatDrawsAndSamplesTheRecordsFormatCanHoldTheRecord()
    {
        FaceSurface.FormatProblem(RecordUses).Should().BeNull();
    }

    [Theory]
    [InlineData(FormatFeatureFlags.ColorAttachmentBit)]
    [InlineData(FormatFeatureFlags.SampledImageBit)]
    public void ADeviceThatLacksOneUseOfTheRecordsFormatIsRefusedWithThatUse(FormatFeatureFlags missing)
    {
        FaceSurface
            .FormatProblem(RecordUses & ~missing)
            .Should()
            .Be($"{FaceSurface.ImageFormat} lacks {missing} in optimal tiling.");
    }

    [Fact]
    public void TheRecordFitsTheColorAttachmentsThatTheRendererAsksOfEveryDevice()
    {
        // The spec promises 4 color attachments per pass. The renderer already refuses a device with fewer than 8
        // (RenderTargets.ColorAttachmentProblem), so the record needs no guard of its own while it fits in 8.
        FaceSurface.Count.Should().BeLessThanOrEqualTo((int)RenderTargets.RequiredColorAttachments);
    }
}

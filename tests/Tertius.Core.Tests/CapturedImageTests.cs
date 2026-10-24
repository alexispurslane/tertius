using AwesomeAssertions;

namespace Tertius.Core.Tests;

/// <summary>
/// The pixels of one captured frame, as the renderer hands them to a capture sink: four bytes for each pixel of a frame
/// of at least one pixel, nothing more and nothing less.
/// </summary>
public sealed class CapturedImageTests
{
    [Fact]
    public void AnImageCarriesItsPixelsAndItsSize()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24];

        var image = new CapturedImage(pixels, 3, 2);

        image.Width.Should().Be(3);
        image.Height.Should().Be(2);
        image.Rgba.ToArray().Should().Equal(pixels);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(25)]
    public void AnImageWhosePixelsAreNotFourBytesForEachPixelOfItsSizeIsRefused(int bytes)
    {
        FluentActions.Invoking(() => { _ = new CapturedImage(new byte[bytes], 3, 2); })
            .Should().Throw<ArgumentException>().WithParameterName("rgba");
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, 0)]
    [InlineData(-1, 2)]
    [InlineData(3, -1)]
    public void AnImageOfNoPixelsIsRefused(int width, int height)
    {
        FluentActions.Invoking(() => { _ = new CapturedImage([], width, height); })
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ASizeWhoseByteCountOverflowsAnIntegerIsRefused()
    {
        // 65,536 × 65,536 × 4 bytes is 2^34, which wraps to 0 in 32-bit arithmetic and so would match an empty span.
        FluentActions.Invoking(() => { _ = new CapturedImage([], 65_536, 65_536); })
            .Should().Throw<ArgumentException>().WithParameterName("rgba");
    }
}

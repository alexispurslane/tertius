using StbImageSharp;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The PNG capture sink: it writes the pixels that it takes as a PNG file at its path, with every pixel opaque, on the
/// thread that hands it the pixels and before <see cref="ICaptureSink.Take"/> returns.
/// </summary>
public sealed class PngCaptureSinkTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("tertius-png-sink-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    // Three by two pixels, each with a different alpha byte, none of them 255. A new array for each call, so that no test
    // sees what another test did to its array.
    private static byte[] Pixels() =>
    [
        200, 10, 20, 0, 30, 220, 40, 7, 50, 60, 240, 128,
        1, 2, 3, 254, 90, 100, 110, 33, 255, 255, 255, 64
    ];

    private static ImageResult Decode(string path) =>
        ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);

    private static void Write(string path, byte[] rgba, int width, int height) =>
        new PngCaptureSink(path).Take(new(rgba, width, height));

    [Fact]
    public void ThePngHoldsTheColorsOfTheImageWithEveryPixelOpaque()
    {
        string path = Path.Combine(folder, "image.png");

        Write(path, Pixels(), 3, 2);

        ImageResult png = Decode(path);
        png.Width.Should().Be(3);
        png.Height.Should().Be(2);
        byte[] expected = Pixels();
        for (int i = 3; i < expected.Length; i += 4)
            expected[i] = 255;
        png.Data.Should().Equal(expected);
    }

    [Fact]
    public void TheSinkMakesTheFolderOfItsFileWhenTheFolderIsMissing()
    {
        string path = Path.Combine(folder, "a", "b", "image.png");

        Write(path, Pixels(), 3, 2);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void TheSinkReplacesAnOlderFileAtItsPath()
    {
        string path = Path.Combine(folder, "image.png");
        File.WriteAllBytes(path, new byte[100_000]);

        Write(path, Pixels(), 3, 2);

        new FileInfo(path).Length.Should().BeLessThan(1000,
            "the new image replaces the old file, with none of the old bytes left after it");
        Decode(path).Width.Should().Be(3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ASinkWithNoPathIsRefused(string path)
    {
        FluentActions.Invoking(() => new PngCaptureSink(path)).Should().Throw<ArgumentException>();
    }
}

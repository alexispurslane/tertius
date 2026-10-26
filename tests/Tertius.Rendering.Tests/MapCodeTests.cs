namespace Tertius.Rendering.Tests;

/// <summary>
/// The map code in the alpha of the G-buffer's base color: the value that each map writes, and the map that each stored
/// code names, under either rounding that Vulkan allows (docs/plans/environment-maps-per-frame.md, decision 20).
/// </summary>
public sealed class MapCodeTests
{
    /// <summary>
    /// The two 8-bit codes nearest a value, which the specification lets a conversion to UNORM return: the value is
    /// clamped to [0, 1] first ("Conversion from Floating-Point to Normalized Fixed-Point").
    /// </summary>
    private static (int Low, int High) NearestCodes(float value)
    {
        float scaled = Math.Clamp(value, 0f, 1f) * 255f;
        return (Low: (int)MathF.Floor(scaled), High: (int)MathF.Ceiling(scaled));
    }

    [Fact]
    public void MapZeroWritesOneAsEveryLitPixelDidBefore() =>
        BitConverter.SingleToInt32Bits(MapCode.Of(0)).Should().Be(BitConverter.SingleToInt32Bits(1f));

    [Fact]
    public void EveryMapDecodesFromEitherOfTheTwoCodesNearestItsValue()
    {
        for (int map = 0; map < EnvironmentChain.MaxMaps; map++)
        {
            (int low, int high) = NearestCodes(MapCode.Of(map));

            MapCode.MapOf(low).Should().Be(map, $"map {map} rounded down to code {low}");
            MapCode.MapOf(high).Should().Be(map, $"map {map} rounded up to code {high}");
        }
    }

    [Fact]
    public void TheCodeNamesAHundredAndTwentyEightMaps()
    {
        (int Low, int High) codes = NearestCodes(MapCode.Of(127));
        codes.Low.Should().Be(1);
        codes.High.Should().Be(2);
        MapCode.MapOf(1).Should().Be(127);
        MapCode.MapOf(2).Should().Be(127);
        NearestCodes(MapCode.Of(128)).High.Should().Be(0, "map 128 would write the unlit code");
    }

    [Fact]
    public void NoServedMapWritesTheUnlitCode()
    {
        for (int map = 0; map < EnvironmentChain.MaxMaps; map++)
            NearestCodes(MapCode.Of(map)).Low.Should().BePositive($"map {map}");
    }
}

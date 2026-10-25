namespace Tertius.Rendering.Tests;

/// <summary>
/// The fixed spatial noise of GTAO: XeGTAO's Hilbert index over a 64 × 64 tile, driving the R2 sequence
/// (docs/plans/ambient-occlusion.md, "The noise").
/// </summary>
public sealed class OcclusionNoiseTests
{
    [Fact]
    public void TheHilbertIndexVisitsEachTexelOfASixtyFourSquareTileOnce()
    {
        const int area = OcclusionNoise.TileSize * OcclusionNoise.TileSize;
        bool[] visited = new bool[area];
        for (uint y = 0; y < OcclusionNoise.TileSize; y++)
        for (uint x = 0; x < OcclusionNoise.TileSize; x++)
        {
            uint index = OcclusionNoise.HilbertIndex(x, y);

            index.Should().BeLessThan(area, $"texel ({x}, {y})");
            visited[index].Should().BeFalse($"texel ({x}, {y}) takes index {index}, which another texel took");
            visited[index] = true;
        }
    }

    [Fact]
    public void ConsecutiveHilbertIndicesAreNeighboringTexels()
    {
        // The curve's order keeps near indices on near texels: each step moves one texel in one axis.
        const int area = OcclusionNoise.TileSize * OcclusionNoise.TileSize;
        var texels = new (int X, int Y)[area];
        for (uint y = 0; y < OcclusionNoise.TileSize; y++)
        for (uint x = 0; x < OcclusionNoise.TileSize; x++)
            texels[OcclusionNoise.HilbertIndex(x, y)] = ((int)x, (int)y);

        for (int index = 1; index < area; index++)
        {
            int moved =
                Math.Abs(texels[index].X - texels[index - 1].X) + Math.Abs(texels[index].Y - texels[index - 1].Y);
            moved.Should().Be(1, $"from index {index - 1} at {texels[index - 1]} to {index} at {texels[index]}");
        }
    }

    [Fact]
    public void TheNoiseRepeatsEverySixtyFourTexelsInEachAxis()
    {
        const uint tile = OcclusionNoise.TileSize;
        foreach ((uint x, uint y) in new[] { (0u, 0u), (5u, 9u), (63u, 1u), (17u, 40u) })
        {
            OcclusionNoise.At(x + tile, y).Should().Be(OcclusionNoise.At(x, y), $"texel ({x}, {y}) and one tile right");
            OcclusionNoise.At(x, y + tile).Should().Be(OcclusionNoise.At(x, y), $"texel ({x}, {y}) and one tile down");
            OcclusionNoise.At(x + 3 * tile, y + 7 * tile).Should().Be(OcclusionNoise.At(x, y));
        }
    }

    [Fact]
    public void TheTexelAtTheOriginTakesTheFirstPointOfTheR2Sequence() =>
        OcclusionNoise.At(0, 0).Should().Be((0.5f, 0.5f));

    [Fact]
    public void TheNoiseOfATileSpreadsEvenlyOverTheUnitSquare()
    {
        // 4,096 points in 64 cells of an 8 × 8 grid: 64 per cell for a perfectly even spread.
        const int cells = 8;
        int[,] counts = new int[cells, cells];
        for (uint y = 0; y < OcclusionNoise.TileSize; y++)
        for (uint x = 0; x < OcclusionNoise.TileSize; x++)
        {
            (float slice, float step) = OcclusionNoise.At(x, y);
            counts[(int)(slice * cells), (int)(step * cells)]++;
        }

        counts.Cast<int>().Should().AllSatisfy(count => count.Should().BeInRange(60, 68));
    }
}

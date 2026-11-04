namespace Tertius.Rendering.Tests;

/// <summary>
/// The CPU side of the calibration probes (docs/research/temporal-renderer/gpu-calibration.md, "The probes"): the walk
/// of the streamed texture probe, and the tolerances of the checks against the CPU, which must catch dropped work.
/// </summary>
public sealed class GpuCalibrationReferenceTests
{
    [Fact]
    public void TheFmaProbesCheckTellsAResultThatSkippedOnePassOfTheLoopFromTheFullResult()
    {
        // One pass of the unrolled loop is 8 steps. Every invocation's result must move by more than the check's
        // tolerance, so that a compiler that dropped work cannot pass it.
        for (int id = 0; id < 1024; id++)
        {
            float full = CalibrationReference.Fma(id, iterations: 1024);
            float skipped = CalibrationReference.Fma(id, iterations: 1024 - 8);
            MathF.Abs(full - skipped).Should().BeGreaterThan(1e-5f * MathF.Abs(full));
        }
    }

    [Fact]
    public void TheTextureProbesCheckTellsAResultThatSkippedOneSampleFromTheFullResult()
    {
        // Every texel channel is at least 64 / 255, so every sample adds at least 1.
        TextureWalk walk = TextureWalk.Cached(side: 32, gridX: 4096, gridY: 256, samples: 512);
        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 32; x++)
            CalibrationReference
                .CornerSum(x, y, side: 32)
                .Should()
                .BeGreaterThan(CalibrationReference.TextureTolerance(walk));
    }

    [Theory]
    [InlineData(256, 16)]
    [InlineData(16384, 256)]
    public void TheStreamedTextureWalkFeedsEachTexelOfItsImageIntoExactlyOneSample(int side, int gridY)
    {
        // A bilinear sample at a corner of even coordinates reads the 2 x 2 texels up and left of it, and those blocks
        // tile the image. So each texel feeds one sample when the walk visits every even corner exactly once.
        TextureWalk walk = TextureWalk.Streamed(side, gridY);
        int corners = side / 2;
        var visited = new System.Collections.BitArray(corners * corners);
        long visits = 0;
        bool even = true;
        bool repeated = false;

        for (int y = 0; y < walk.GridY; y++)
        for (int x = 0; x < walk.GridX; x++)
        for (int sample = 0; sample < walk.Samples; sample++)
        {
            (int cornerX, int cornerY) = walk.Corner(x, y, sample);
            even &= cornerX % 2 == 0 && cornerY % 2 == 0;
            int index = cornerY / 2 * corners + cornerX / 2;
            repeated |= visited[index];
            visited[index] = true;
            visits++;
        }

        (even, repeated, visits).Should().Be((true, false, (long)corners * corners));
    }
}

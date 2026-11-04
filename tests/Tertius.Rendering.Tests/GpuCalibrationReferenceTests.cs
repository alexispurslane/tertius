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
            MathF.Abs(full - skipped).Should().BeGreaterThan(CalibrationReference.FmaTolerance * MathF.Abs(full));
        }
    }

    [Fact]
    public void TheFmaProbesCheckAcceptsTheResultOfADriverThatRoundsEachProductBeforeItsAdd()
    {
        // GLSL lets a driver compute an fma() that no precise variable consumes as a product and an add with two
        // roundings. The check must pass on such a driver too, such as one of the Linux drivers.
        for (int id = 0; id < 1024; id++)
        {
            float fused = CalibrationReference.Fma(id, iterations: 1024);
            float unfused = UnfusedFma(id, iterations: 1024);
            MathF
                .Abs(fused - unfused)
                .Should()
                .BeLessThanOrEqualTo(CalibrationReference.FmaTolerance * MathF.Abs(fused));
        }
    }

    [Fact]
    public void TheTextureProbesCheckTellsAResultThatSkippedOneSampleFromTheFullResult()
    {
        // Every texel channel is at least 64 / 255, so every sample adds at least 1.
        var walk = TextureWalk.Cached(side: 32, gridX: 4096, gridY: 256, samples: 512);
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
        var walk = TextureWalk.Streamed(side, gridY);
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

    // CalibrationReference.Fma with each step rounded after its product and after its add.
    private static float UnfusedFma(int id, uint iterations)
    {
        float start = 1f + (id & 1023) * (1f / 1024f);
        Span<float> components = stackalloc float[4];
        for (int chain = 0; chain < CalibrationReference.Chains; chain++)
        {
            float x = start + chain % 4 * 0.125f + chain / 4 * 0.5f;
            for (uint step = 0; step < iterations; step++)
            {
                float product = x * CalibrationReference.A;
                x = product + CalibrationReference.B;
            }

            components[chain % 4] += x;
        }

        return components[0] + components[1] + components[2] + components[3];
    }
}

namespace Tertius.Rendering.Tests;

/// <summary>
/// The rounding of a history's stores (half_store.glsl, ticket t8-69v7): nearestHalf gives the half float nearest to a
/// value, with ties to even, as a 32-bit float. A store of it in a 16-bit float image is then exact on every driver,
/// whatever rounding the driver's store takes. The probe runs the function on the GPU (nearest_half_probe.comp), and the
/// check compares each result with .NET's conversion to <see cref="Half"/>, which rounds to the nearest with ties to even.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class HalfStoreTests
{
    /// <summary>The half float spacing between 1 and 2.</summary>
    private const float Step = 1f / 1024;

    /// <summary>The least normal half float, 2^−14.</summary>
    private const float LeastNormal = 6.103515625e-05f;

    /// <summary>The least subnormal half float, 2^−24.</summary>
    private const float LeastSubnormal = 5.9604645e-08f;

    /// <summary>
    /// The values of <see cref="NearestHalfGivesTheNearestHalfFloatWithTiesToEven"/>, by kind: zero, half floats, powers of
    /// two and their neighbors, ties, the least normal half float, subnormal half floats, values below every half float,
    /// and light of the size that a history holds.
    /// </summary>
    private static readonly float[] NamedValues =
    [
        // Zero of both signs, and half floats, which stay as they are.
        0f,
        -0f,
        1f,
        -1f,
        0.5f,
        2f,
        1024f,
        1 + Step,
        -(1 + 3 * Step),
        // The largest half float, and a value below the tie between it and 65,536.
        65504f,
        65519f,
        // The float next to a power of two on each side. Below 2 the half floats lie half as far apart as above it.
        MathF.BitIncrement(1f),
        MathF.BitDecrement(1f),
        MathF.BitDecrement(2f),
        -MathF.BitDecrement(2f),
        MathF.BitIncrement(0.25f),
        // 2 − 2^−12 lies above the tie between 2 − 2^−11 and 2, so it rounds up into the next binade.
        2 - Step / 4,
        2 - Step / 2,
        // Ties: 1 + 2^−11 lies between 1, whose last bit is even, and 1 + 2^−10. 1 + 3 · 2^−11 lies between
        // 1 + 2^−10 and 1 + 2^−9, whose last bit is even.
        1 + Step / 2,
        -(1 + Step / 2),
        1 + 3 * Step / 2,
        -(1 + 3 * Step / 2),
        // The floats next to a tie round away from it.
        MathF.BitIncrement(1 + Step / 2),
        MathF.BitDecrement(1 + 3 * Step / 2),
        MathF.BitIncrement(1 + 3 * Step / 2),
        // The least normal half float, the tie below it, and the floats next to it.
        LeastNormal,
        -LeastNormal,
        LeastNormal - LeastSubnormal / 2,
        MathF.BitDecrement(LeastNormal),
        MathF.BitIncrement(LeastNormal),
        // Subnormal half floats, and the ties and values between them.
        LeastSubnormal,
        -LeastSubnormal,
        3 * LeastSubnormal,
        1023 * LeastSubnormal,
        LeastSubnormal / 2,
        MathF.BitIncrement(LeastSubnormal / 2),
        MathF.BitDecrement(LeastSubnormal / 2),
        3 * LeastSubnormal / 2,
        5 * LeastSubnormal / 2,
        -5 * LeastSubnormal / 2,
        // Values below every half float, among them a subnormal 32-bit float, round to zero.
        1e-30f,
        -1e-30f,
        1e-40f,
        // Values of the size of a history's light: MaxRadiance, and light of every size.
        32256f,
        0.1f,
        0.4f,
        3.14159f,
        1000.3f,
        -77.77f,
    ];

    [Fact]
    public void NearestHalfGivesTheNearestHalfFloatWithTiesToEven()
    {
        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.NearestHalf(NamedValues);

        bench.Run(probe);

        probe.Check().Should().BeNull();
        string.Join("\n", bench.Device.Log.Snapshot()).Should().BeEmpty();
    }

    [Fact]
    public void NearestHalfGivesTheNearestHalfFloatForEveryMagnitudeThatAHistoryStores()
    {
        // 32-bit floats of every exponent from 2^−26, below the least subnormal half float, to 2^15, the binade of the
        // largest half float, with seeded mantissas and both signs. The last 13 bits of the mantissas take every kind of
        // pattern that a rounding can meet: zeros, a tie, and the floats next to the tie.
        var random = new Random(69);
        uint[] tails = [0u, 1u, 0x0FFFu, 0x1000u, 0x1001u, 0x1FFFu];
        List<float> values = [];
        for (int exponent = -26; exponent <= 15; exponent++)
        for (int draw = 0; draw < 96; draw++)
        {
            uint mantissa = (uint)random.Next(1 << 23);
            if (draw < 2 * tails.Length)
                mantissa = (mantissa & ~0x1FFFu) | tails[draw % tails.Length];
            uint sign = draw / tails.Length % 2 == 0 ? 0u : 0x8000_0000u;
            float value = BitConverter.UInt32BitsToSingle(sign | (uint)(exponent + 127) << 23 | mantissa);
            if (MathF.Abs(value) < 65520f)
                values.Add(value);
        }

        using var bench = new CalibrationBench(validation: true);
        CalibrationProbe probe = bench.NearestHalf([.. values]);

        bench.Run(probe);

        probe.Check().Should().BeNull();
    }
}

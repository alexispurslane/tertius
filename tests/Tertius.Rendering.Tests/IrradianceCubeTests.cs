using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the irradiance cube's convolution (docs/plans/environment-map.md, "The irradiance"): D(n) from the
/// radiance map's level of 32 texels, against the exact cosine-weighted sum over level 0 of a map of 128 texels, which is
/// what the brute-force D of many cosine-weighted reads converges to.
/// </summary>
public sealed class IrradianceCubeTests
{
    /// <summary>
    /// The tolerance on D, as a share of the largest D of the map: the diffuse light toward its one small bright light
    /// (docs/plans/environment-map.md, "The irradiance").
    /// </summary>
    private const double Tolerance = 0.03;

    /// <summary>The face size of the maps of these tests, the default of the renderer.</summary>
    private const int MapSize = 128;

    /// <summary>The direction of the small light of these tests: off every axis and every face's diagonal.</summary>
    private static readonly Vector3 TowardTheLight = Vector3.Normalize(new(0.3f, 0.5f, 0.8f));

    /// <summary>
    /// Level 0 of a radiance map of <paramref name="faceSize"/> texels, layer then row then column, RGBA, with each texel
    /// the mean of <paramref name="radiance"/> over 4 × 4 points of it, so that a small light's edge falls between texels
    /// as its share of each texel says.
    /// </summary>
    private static Half[] Map(int faceSize, Func<Vector3, Vector3> radiance)
    {
        const int points = 4;
        var texels = new Half[CubeFaces.Count * faceSize * faceSize * 4];
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < faceSize; y++)
        for (int x = 0; x < faceSize; x++)
        {
            Vector3 sum = Vector3.Zero;
            for (int j = 0; j < points; j++)
            for (int i = 0; i < points; i++)
                sum += radiance(
                    CubeFaces.Direction(
                        (CubeFace)layer,
                        (x + (i + 0.5f) / points) / faceSize,
                        (y + (j + 0.5f) / points) / faceSize
                    )
                );
            Vector3 value = sum / (points * points);
            int texel = ((layer * faceSize + y) * faceSize + x) * 4;
            texels[texel] = (Half)value.X;
            texels[texel + 1] = (Half)value.Y;
            texels[texel + 2] = (Half)value.Z;
            texels[texel + 3] = (Half)0;
        }

        return texels;
    }

    /// <summary>
    /// The next level of a map of <paramref name="faceSize"/> texels: each texel the mean of its four children weighted
    /// by their solid angles, as environment_downsample.comp makes the radiance map's mips.
    /// </summary>
    private static Half[] Downsampled(Half[] level, int faceSize)
    {
        int half = faceSize / 2;
        var next = new Half[CubeFaces.Count * half * half * 4];
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < half; y++)
        for (int x = 0; x < half; x++)
        for (int channel = 0; channel < 4; channel++)
        {
            double sum = 0;
            double weight = 0;
            for (int j = 0; j < 2; j++)
            for (int i = 0; i < 2; i++)
            {
                double solidAngle = CubeFaces.TexelSolidAngle(2 * x + i, 2 * y + j, faceSize);
                sum += (double)level[((layer * faceSize + 2 * y + j) * faceSize + 2 * x + i) * 4 + channel] * solidAngle;
                weight += solidAngle;
            }

            next[((layer * half + y) * half + x) * 4 + channel] = (Half)(sum / weight);
        }

        return next;
    }

    /// <summary>The level of <paramref name="size"/> texels of <paramref name="map"/>, a level 0 of <see cref="MapSize"/>.</summary>
    private static Half[] LevelOf(Half[] map, int size)
    {
        Half[] level = map;
        for (int faceSize = MapSize; faceSize > size; faceSize /= 2)
            level = Downsampled(level, faceSize);
        return level;
    }

    /// <summary>
    /// The exact D at the unit <paramref name="normal"/>: the sum of every texel of level 0 times its solid angle and the
    /// clamped cosine, over the sum of the weights, in doubles. The red channel.
    /// </summary>
    private static double ExactRed(Half[] map, int faceSize, Vector3 normal)
    {
        double sum = 0;
        double weight = 0;
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < faceSize; y++)
        for (int x = 0; x < faceSize; x++)
        {
            Vector3 direction = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / faceSize, (y + 0.5f) / faceSize);
            double cosine = Vector3.Dot(normal, direction);
            if (cosine <= 0)
                continue;
            double w = cosine * CubeFaces.TexelSolidAngle(x, y, faceSize);
            sum += (double)map[((layer * faceSize + y) * faceSize + x) * 4] * w;
            weight += w;
        }

        return sum / weight;
    }

    /// <summary>A small light: radiance that sends an irradiance of 1 from a disc 2° in radius around <paramref name="center"/>.</summary>
    private static Func<Vector3, Vector3> SmallLight(Vector3 center)
    {
        double cosRadius = Math.Cos(2 * Math.PI / 180);
        float radiance = (float)(1 / (2 * Math.PI * (1 - cosRadius)));
        return direction => Vector3.Dot(direction, center) >= cosRadius ? new(radiance) : Vector3.Zero;
    }

    private static IEnumerable<Vector3> SpreadOfNormals()
    {
        var random = new Random(11);
        for (int index = 0; index < 200; index++)
            yield return Vector3.Normalize(
                new(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1)
            );
    }

    [Fact]
    public void UnderOneSmallBrightLightDIsWithinTheToleranceOfTheExactSumTowardAcrossAndAtRightAnglesToIt()
    {
        Half[] map = Map(MapSize, SmallLight(TowardTheLight));
        Half[] source = LevelOf(map, IrradianceCube.SourceFaceSize);
        var aside = Vector3.Normalize(Vector3.Cross(TowardTheLight, Vector3.UnitX));
        double peak = ExactRed(map, MapSize, TowardTheLight);

        foreach (
            Vector3 normal in (ReadOnlySpan<Vector3>)
                [TowardTheLight, -TowardTheLight, aside, Vector3.Cross(TowardTheLight, aside)]
        )
        {
            double expected = ExactRed(map, MapSize, normal);
            double convolved = IrradianceCube.Convolve(source, IrradianceCube.SourceFaceSize, normal).X;
            Math.Abs(convolved - expected)
                .Should()
                .BeLessThanOrEqualTo(Tolerance * peak, "D at {0} is {1}, against the exact {2}", normal, convolved, expected);
        }
    }

    [Fact]
    public void UnderARadianceOfOneAboveTheHorizonAndABlackGroundDIsOnePlusNyOverTwo()
    {
        Half[] map = Map(MapSize, direction => direction.Y > 0 ? Vector3.One : Vector3.Zero);
        Half[] source = LevelOf(map, IrradianceCube.SourceFaceSize);

        foreach (Vector3 normal in SpreadOfNormals())
            IrradianceCube
                .Convolve(source, IrradianceCube.SourceFaceSize, normal)
                .X.Should()
                .BeApproximately((1 + normal.Y) / 2, (float)Tolerance, "the hemisphere's diffuse light at {0}", normal);
    }

    [Fact]
    public void AUniformMapGivesAUniformDEqualToItsRadiance()
    {
        Half[] source = Map(IrradianceCube.SourceFaceSize, _ => new(2, 3, 4));

        foreach (Vector3 normal in SpreadOfNormals())
            Vector3
                .Distance(
                    IrradianceCube.Convolve(source, IrradianceCube.SourceFaceSize, normal).AsVector3(),
                    new(2, 3, 4)
                )
                .Should()
                .BeLessThan(
                    1e-5f * new Vector3(2, 3, 4).Length(),
                    "the weights of the mean add up to 1 at {0}, within the rounding of single floats",
                    normal
                );
    }

    [Fact]
    public void TheOpenSkyShareOfDIsTheCosineWeightedShareOfTheHemisphere()
    {
        // A map whose alpha is 1 above the horizon: the open-sky share of D is (1 + n_y) / 2, as its color would be.
        Half[] source = Map(IrradianceCube.SourceFaceSize, _ => Vector3.Zero);
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < IrradianceCube.SourceFaceSize; y++)
        for (int x = 0; x < IrradianceCube.SourceFaceSize; x++)
        {
            const int size = IrradianceCube.SourceFaceSize;
            Vector3 direction = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size);
            source[((layer * size + y) * size + x) * 4 + 3] = (Half)(direction.Y > 0 ? 1 : 0);
        }

        foreach (Vector3 normal in SpreadOfNormals())
            IrradianceCube
                .Convolve(source, IrradianceCube.SourceFaceSize, normal)
                .W.Should()
                .BeApproximately((1 + normal.Y) / 2, (float)Tolerance, "the open-sky share at {0}", normal);
    }

    [Theory]
    [InlineData(16, 0)]
    [InlineData(32, 0)]
    [InlineData(128, 2)]
    [InlineData(1024, 5)]
    public void TheCubeConvolvesTheLevelOfTheMapWhoseFacesHold32Texels(int mapFaceSize, int level) =>
        IrradianceCube.SourceLevel(mapFaceSize).Should().Be(level);
}

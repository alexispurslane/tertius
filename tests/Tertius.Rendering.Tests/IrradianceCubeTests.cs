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
                sum +=
                    (double)level[((layer * faceSize + 2 * y + j) * faceSize + 2 * x + i) * 4 + channel] * solidAngle;
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
    /// The exact D of level 0 of a map, the red channel: at a unit normal, the sum of
    /// every texel times its solid angle and the clamped cosine, over the sum of the weights, in doubles. Each texel's
    /// direction and solid angle are computed once.
    /// </summary>
    private sealed class ExactRed
    {
        private readonly Vector3[] directions;
        private readonly double[] solidAngles;
        private readonly double[] weightedRed;

        public ExactRed(Half[] map, int faceSize)
        {
            int texels = CubeFaces.Count * faceSize * faceSize;
            directions = new Vector3[texels];
            solidAngles = new double[texels];
            weightedRed = new double[texels];
            for (int texel = 0; texel < texels; texel++)
            {
                int layer = texel / (faceSize * faceSize);
                int x = texel % faceSize;
                int y = texel / faceSize % faceSize;
                directions[texel] = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / faceSize, (y + 0.5f) / faceSize);
                solidAngles[texel] = CubeFaces.TexelSolidAngle(x, y, faceSize);
                weightedRed[texel] = (double)map[4 * texel] * solidAngles[texel];
            }
        }

        public double At(Vector3 normal)
        {
            double sum = 0;
            double weight = 0;
            for (int texel = 0; texel < directions.Length; texel++)
            {
                double cosine = Vector3.Dot(normal, directions[texel]);
                if (cosine <= 0)
                    continue;
                sum += weightedRed[texel] * cosine;
                weight += solidAngles[texel] * cosine;
            }

            return sum / weight;
        }
    }

    /// <summary>
    /// A small light: radiance that sends an irradiance of 1 from a disc <paramref name="degrees"/> in radius around
    /// <paramref name="center"/>.
    /// </summary>
    private static Func<Vector3, Vector3> SmallLight(Vector3 center, double degrees = 2)
    {
        double cosRadius = Math.Cos(degrees * Math.PI / 180);
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

    /// <summary>
    /// Level 0 of the irradiance cube of <paramref name="source"/>, a level of <paramref name="sourceSize"/> texels:
    /// <see cref="IrradianceCube.FaceSize"/> texels a side, each D at its center's direction, layer then row then column.
    /// </summary>
    private static Vector3[] Cube(Half[] source, int sourceSize)
    {
        const int size = IrradianceCube.FaceSize;
        var cube = new Vector3[CubeFaces.Count * size * size];
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            cube[(layer * size + y) * size + x] = IrradianceCube
                .Convolve(
                    source,
                    sourceSize,
                    CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / size, (y + 0.5f) / size)
                )
                .AsVector3();
        return cube;
    }

    /// <summary>
    /// D at the unit <paramref name="normal"/> as a reader's bilinear read of <paramref name="cube"/> gives it. A texel of
    /// the read's 2 × 2 that lies past its face's edge comes from the face that the texel's center direction reaches, as
    /// Vulkan's cube edge handling takes it from the neighboring face.
    /// </summary>
    private static Vector3 Read(Vector3[] cube, Vector3 normal)
    {
        const int size = IrradianceCube.FaceSize;
        (CubeFace face, float s, float t) = CubeFaces.Locate(normal);
        float u = s * size - 0.5f;
        float v = t * size - 0.5f;
        int x0 = (int)MathF.Floor(u);
        int y0 = (int)MathF.Floor(v);

        Vector3 Texel(int x, int y)
        {
            if (x is >= 0 and < size && y is >= 0 and < size)
                return cube[((int)face * size + y) * size + x];
            (CubeFace other, float os, float ot) = CubeFaces.Locate(
                CubeFaces.Direction(face, (x + 0.5f) / size, (y + 0.5f) / size)
            );
            int ox = Math.Clamp((int)(os * size), 0, size - 1);
            int oy = Math.Clamp((int)(ot * size), 0, size - 1);
            return cube[((int)other * size + oy) * size + ox];
        }

        return Vector3.Lerp(
            Vector3.Lerp(Texel(x0, y0), Texel(x0 + 1, y0), u - x0),
            Vector3.Lerp(Texel(x0, y0 + 1), Texel(x0 + 1, y0 + 1), u - x0),
            v - y0
        );
    }

    /// <summary>
    /// The normals at which the tests compare D under a light toward <paramref name="light"/>: 24 around each ring at 0°,
    /// 5°, 10° and on to 180° from it, the worst ring at right angles among them.
    /// </summary>
    private static IEnumerable<Vector3> RingsAround(Vector3 light)
    {
        var aside = Vector3.Normalize(Vector3.Cross(light, MathF.Abs(light.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
        var third = Vector3.Cross(light, aside);
        for (int degrees = 0; degrees <= 180; degrees += 5)
        {
            float polar = degrees * MathF.PI / 180;
            for (int step = 0; step < 24; step++)
            {
                float azimuth = step * MathF.PI / 12;
                yield return Vector3.Normalize(
                    MathF.Cos(polar) * light
                        + MathF.Sin(polar) * (MathF.Cos(azimuth) * aside + MathF.Sin(azimuth) * third)
                );
            }
        }
    }

    /// <summary>
    /// The worst error of the cube's bilinear read, as a share of the largest D, at <see cref="RingsAround"/> each of 10
    /// small lights of <paramref name="degrees"/> in random directions, each alone on a black map of
    /// <see cref="MapSize"/>, with the cube convolved from the map's level of <paramref name="sourceSize"/> texels.
    /// </summary>
    private static double WorstReadError(int sourceSize, double degrees)
    {
        var random = new Random(5);
        double worst = 0;
        for (int light = 0; light < 10; light++)
        {
            var center = Vector3.Normalize(
                new(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1)
            );
            Half[] map = Map(MapSize, SmallLight(center, degrees));
            Vector3[] cube = Cube(LevelOf(map, sourceSize), sourceSize);
            var exact = new ExactRed(map, MapSize);
            double peak = exact.At(center);
            foreach (Vector3 normal in RingsAround(center))
                worst = Math.Max(worst, Math.Abs(Read(cube, normal).X - exact.At(normal)) / peak);
        }

        return worst;
    }

    [Fact]
    public void UnderASmallBrightLightInAnyOfTenDirectionsTheCubesReadIsWithinTheToleranceAtEveryNormal() =>
        WorstReadError(IrradianceCube.SourceFaceSize, degrees: 2)
            .Should()
            .BeLessThanOrEqualTo(Tolerance, "the cube of 16 over a source of 32 holds a light of 2° (measured 2.07 %)");

    /// <summary>
    /// A map of 16 texels a side, the smallest setting, has no level of 32, so the cube convolves its level 0 of 16, whose
    /// texels span about 5.6°. Under a light of 2° that misses the tolerance: Known limit 2 of
    /// docs/plans/screen-space-illumination.md states the measured 3.53 %, and this test keeps it within 4 %.
    /// </summary>
    [Fact]
    public void OnAMapOf16TexelsASmallBrightLightStaysWithinTheKnownLimit() =>
        WorstReadError(sourceSize: 16, degrees: 2)
            .Should()
            .BeLessThanOrEqualTo(0.04, "a source of 16 texels holds a light of 2° within 4 % (measured 3.53 %)");

    [Fact]
    public void UnderOneSmallBrightLightDIsWithinTheToleranceOfTheExactSumTowardAcrossAndAtRightAnglesToIt()
    {
        Half[] map = Map(MapSize, SmallLight(TowardTheLight));
        Half[] source = LevelOf(map, IrradianceCube.SourceFaceSize);
        var aside = Vector3.Normalize(Vector3.Cross(TowardTheLight, Vector3.UnitX));
        var exact = new ExactRed(map, MapSize);
        double peak = exact.At(TowardTheLight);

        foreach (
            Vector3 normal in (ReadOnlySpan<Vector3>)
                [TowardTheLight, -TowardTheLight, aside, Vector3.Cross(TowardTheLight, aside)]
        )
        {
            double expected = exact.At(normal);
            double convolved = IrradianceCube.Convolve(source, IrradianceCube.SourceFaceSize, normal).X;
            Math.Abs(convolved - expected)
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance * peak,
                    "D at {0} is {1}, against the exact {2}",
                    normal,
                    convolved,
                    expected
                );
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

    [Fact]
    public void ASourceThatIsNotSixFacesOfItsSizeIsRefused()
    {
        var fiveFaces = new Half[5 * IrradianceCube.SourceFaceSize * IrradianceCube.SourceFaceSize * 4];

        Action convolve = () => IrradianceCube.Convolve(fiveFaces, IrradianceCube.SourceFaceSize, Vector3.UnitY);

        convolve.Should().Throw<ArgumentException>();
    }
}

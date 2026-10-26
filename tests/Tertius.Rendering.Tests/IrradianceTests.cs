using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The irradiance of the map as nine spherical harmonics: the basis, the projection of a cube onto it, the clamped
/// evaluation, the mean radiance and the change between two irradiances (docs/plans/environment-map.md,
/// "The irradiance").
/// </summary>
public sealed class IrradianceTests
{
    /// <summary>Â_l, the clamped cosine's coefficient of the band of each of the nine coefficients.</summary>
    private static readonly double[] CosineBands =
    [
        Math.PI,
        2 * Math.PI / 3,
        2 * Math.PI / 3,
        2 * Math.PI / 3,
        Math.PI / 4,
        Math.PI / 4,
        Math.PI / 4,
        Math.PI / 4,
        Math.PI / 4,
    ];

    /// <summary>Level 0 of a radiance map, layer then row then column, RGBA, with each texel's center given by <paramref name="radiance"/>.</summary>
    private static Half[] Cube(int faceSize, Func<Vector3, Vector3> radiance)
    {
        var texels = new Half[CubeFaces.Count * faceSize * faceSize * 4];
        for (int layer = 0; layer < CubeFaces.Count; layer++)
        for (int y = 0; y < faceSize; y++)
        for (int x = 0; x < faceSize; x++)
        {
            Vector3 direction = CubeFaces.Direction((CubeFace)layer, (x + 0.5f) / faceSize, (y + 0.5f) / faceSize);
            Vector3 value = radiance(direction);
            int texel = ((layer * faceSize + y) * faceSize + x) * 4;
            texels[texel] = (Half)value.X;
            texels[texel + 1] = (Half)value.Y;
            texels[texel + 2] = (Half)value.Z;
            texels[texel + 3] = (Half)1;
        }

        return texels;
    }

    private static Vector3 Unclamped(in Irradiance irradiance, Vector3 normal)
    {
        Span<double> basis = stackalloc double[Irradiance.Coefficients];
        Irradiance.Basis(normal, basis);
        Vector3 sum = Vector3.Zero;
        for (int index = 0; index < Irradiance.Coefficients; index++)
            sum += irradiance[index] * (float)basis[index];
        return sum;
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
    public void TheBasisIsOrthonormalOverTheSphere()
    {
        const int rows = 400;
        const int columns = 800;
        double[,] products = new double[Irradiance.Coefficients, Irradiance.Coefficients];
        Span<double> basis = stackalloc double[Irradiance.Coefficients];
        for (int row = 0; row < rows; row++)
        {
            double theta = (row + 0.5) * Math.PI / rows;
            double area = Math.Sin(theta) * (Math.PI / rows) * (2 * Math.PI / columns);
            for (int column = 0; column < columns; column++)
            {
                double phi = (column + 0.5) * 2 * Math.PI / columns;
                var direction = new Vector3(
                    (float)(Math.Sin(theta) * Math.Cos(phi)),
                    (float)(Math.Sin(theta) * Math.Sin(phi)),
                    (float)Math.Cos(theta)
                );
                Irradiance.Basis(direction, basis);
                for (int i = 0; i < Irradiance.Coefficients; i++)
                for (int j = 0; j < Irradiance.Coefficients; j++)
                    products[i, j] += basis[i] * basis[j] * area;
            }
        }

        for (int i = 0; i < Irradiance.Coefficients; i++)
        for (int j = 0; j < Irradiance.Coefficients; j++)
            products[i, j].Should().BeApproximately(i == j ? 1 : 0, 1e-4, $"the product of Y_{i} and Y_{j}");
    }

    [Fact]
    public void TheBasisFollowsTheDocsOrder()
    {
        Span<double> basis = stackalloc double[Irradiance.Coefficients];
        var direction = Vector3.Normalize(new(1, 2, 3));
        (double x, double y, double z) = (direction.X, direction.Y, direction.Z);

        Irradiance.Basis(direction, basis);

        double[] expected =
        [
            0.282095,
            0.488603 * y,
            0.488603 * z,
            0.488603 * x,
            1.092548 * x * y,
            1.092548 * y * z,
            0.315392 * (3 * z * z - 1),
            1.092548 * x * z,
            0.546274 * (x * x - y * y),
        ];
        for (int index = 0; index < Irradiance.Coefficients; index++)
            basis[index].Should().BeApproximately(expected[index], 1e-6, $"Y_{index}");
    }

    [Fact]
    public void AUniformMapProjectsOntoTheFirstCoefficientAlone()
    {
        var irradiance = Irradiance.Project(Cube(16, _ => new(2, 2, 2)), 16);

        Vector3 first = irradiance[0];
        first.X.Should().BeApproximately((float)(Math.PI * 2 * 4 * Math.PI * 0.2820948), 1e-4f);
        for (int index = 1; index < Irradiance.Coefficients; index++)
            irradiance[index].Length().Should().BeLessThan(1e-5f, $"coefficient {index}, by the cube's symmetry");
    }

    [Fact]
    public void ItsIrradianceIsPiTimesItsRadianceInEveryDirection()
    {
        var irradiance = Irradiance.Project(Cube(16, _ => new(2, 3, 4)), 16);

        foreach (Vector3 normal in SpreadOfNormals())
            Vector3.Distance(irradiance.At(normal), MathF.PI * new Vector3(2, 3, 4)).Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void AWhiteUpperHalfGivesTheHemispheresDiffuseLightExactly()
    {
        // At W = 64 the horizon falls between rows 31 and 32 of the side faces, so the cube splits exactly. The
        // texel-center sums of the projection then err by 4e-5, and by 1.6e-4 at W = 32: the error falls as 1 / W².
        var irradiance = Irradiance.Project(Cube(64, direction => direction.Y > 0 ? Vector3.One : Vector3.Zero), 64);

        foreach (Vector3 normal in SpreadOfNormals())
        {
            Vector3 diffuse = irradiance.At(normal) / MathF.PI;
            float hemisphere = (1 + normal.Y) / 2;
            diffuse.X.Should().BeApproximately(hemisphere, 1e-4f, $"normal {normal}");
            diffuse.Y.Should().BeApproximately(hemisphere, 1e-4f, $"normal {normal}");
            diffuse.Z.Should().BeApproximately(hemisphere, 1e-4f, $"normal {normal}");
        }
    }

    [Fact]
    public void TheProjectionOfAKnownCubeRecoversTheCoefficientsThatFilledIt()
    {
        double[] weights = [3, 0.4, -0.7, 0.2, 0.5, -0.3, 0.6, 0.1, -0.8];
        Vector3 Radiance(Vector3 direction)
        {
            Span<double> basis = stackalloc double[Irradiance.Coefficients];
            Irradiance.Basis(direction, basis);
            double sum = 0;
            for (int index = 0; index < Irradiance.Coefficients; index++)
                sum += weights[index] * basis[index];
            return new((float)sum, (float)(2 * sum), (float)-sum);
        }

        var irradiance = Irradiance.Project(Cube(32, Radiance), 32);

        for (int index = 0; index < Irradiance.Coefficients; index++)
        {
            Vector3 radiance = irradiance[index] / (float)CosineBands[index];
            radiance.X.Should().BeApproximately((float)weights[index], 1e-3f, $"coefficient {index}");
            radiance.Y.Should().BeApproximately((float)(2 * weights[index]), 2e-3f, $"coefficient {index}");
            radiance.Z.Should().BeApproximately((float)-weights[index], 1e-3f, $"coefficient {index}");
        }
    }

    [Fact]
    public void LightFromOneDirectionPeaksTowardItAndTheClampKeepsItsDipAtZero()
    {
        // One bright texel next to the center of face +Y: light from almost straight up.
        const int faceSize = 32;
        Half[] cube = Cube(faceSize, _ => Vector3.Zero);
        int texel = ((2 * faceSize + 16) * faceSize + 16) * 4;
        cube[texel] = cube[texel + 1] = cube[texel + 2] = (Half)1000;
        var irradiance = Irradiance.Project(cube, faceSize);

        Vector3 light = CubeFaces.Direction(CubeFace.PositiveY, 16.5f / faceSize, 16.5f / faceSize);
        Vector3 toward = irradiance.At(light);
        float angle = 122 * MathF.PI / 180;
        var dip = new Vector3(MathF.Sin(angle), MathF.Cos(angle), 0);

        foreach (Vector3 normal in SpreadOfNormals())
            irradiance.At(normal).X.Should().BeLessThanOrEqualTo(toward.X, $"normal {normal}");
        Unclamped(irradiance, dip).X.Should().BeNegative("band 2 rings below zero about 122° from the light");
        irradiance.At(dip).Should().Be(Vector3.Zero);
    }

    [Fact]
    public void TheMeanRadianceOfAUniformMapIsItsRadiance()
    {
        var irradiance = Irradiance.Project(Cube(16, _ => new(2, 3, 4)), 16);

        Vector3.Distance(irradiance.MeanRadiance, new(2, 3, 4)).Should().BeLessThan(1e-5f);
    }

    [Fact]
    public void TwoProjectionsOfOneCubeGiveTheSameBits()
    {
        Half[] cube = Cube(32, direction => new(direction.X * direction.X, 1 + direction.Y, MathF.Abs(direction.Z)));

        var first = Irradiance.Project(cube, 32);
        var second = Irradiance.Project(cube, 32);

        for (int index = 0; index < Irradiance.Coefficients; index++)
            second[index].Should().Be(first[index], $"coefficient {index}");
    }

    [Fact]
    public void TheChangeBetweenTwoIrradiancesIsAShareOfTheLargerTotal()
    {
        var before = new Irradiance([new(6, 8, 0), .. new Vector3[8]]);
        var after = new Irradiance([new(6, 8, 0), default, default, default, new(3, 4, 0), .. new Vector3[4]]);

        // The change is the length of the one changed coefficient, 5, and the larger total is 10 + 5.
        Irradiance.Change(before, after).Should().BeApproximately(1f / 3, 1e-6f);
        Irradiance.Change(after, before).Should().BeApproximately(1f / 3, 1e-6f);
    }

    [Fact]
    public void NoChangeIsZero()
    {
        var irradiance = new Irradiance([new(6, 8, 0), new(1, 2, 3), .. new Vector3[7]]);

        Irradiance.Change(irradiance, irradiance).Should().Be(0);
        Irradiance.Change(default, default).Should().Be(0, "a map with no light has not changed either");
    }

    [Fact]
    public void AnIrradianceOfOtherThanNineCoefficientsIsRefused()
    {
        Action eight = () => _ = new Irradiance(new Vector3[8]);

        eight.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AProjectionOfACubeOfAnotherSizeIsRefused()
    {
        Action wrong = () => Irradiance.Project(new Half[CubeFaces.Count * 16 * 16 * 4], 32);

        wrong.Should().Throw<ArgumentException>();
    }
}

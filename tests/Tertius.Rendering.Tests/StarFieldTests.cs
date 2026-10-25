using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the star field: the stars of the cells that the sky reads, the width and the window of a reflected
/// star, the fade into the mean star radiance, and the light that each keeps (docs/plans/environment-map.md,
/// "The reflected stars").
/// </summary>
public sealed class StarFieldTests
{
    /// <summary>p: the angle of one pixel of the main view at 1080p and a 60° vertical field of view, in rad.</summary>
    private const float PixelAngle = 9.7e-4f;

    private const float Cell = StarField.CellWidth;

    private static readonly Vector3 Warm = new(1f, 0.82f, 0.62f);
    private static readonly Vector3 Cool = new(0.72f, 0.82f, 1f);

    private static float Fract(float value) => value - MathF.Floor(value);

    private static Vector3 CellOf(Vector3 direction) =>
        new(
            MathF.Floor(direction.X * StarField.CellsPerUnit),
            MathF.Floor(direction.Y * StarField.CellsPerUnit),
            MathF.Floor(direction.Z * StarField.CellsPerUnit)
        );

    /// <summary>The cells of the box from <paramref name="low"/> to <paramref name="high"/>, both included.</summary>
    private static IEnumerable<Vector3> Cells(Vector3 low, Vector3 high)
    {
        for (float z = low.Z; z <= high.Z; z++)
        for (float y = low.Y; y <= high.Y; y++)
        for (float x = low.X; x <= high.X; x++)
            yield return new(x, y, z);
    }

    /// <summary>The stars of the cells that the sky reads within <paramref name="reach"/> cells of <paramref name="direction"/>.</summary>
    private static IEnumerable<Star> SkyStarsNear(Vector3 direction, float reach)
    {
        Vector3 center = CellOf(direction);
        foreach (Vector3 cell in Cells(center - new Vector3(reach), center + new Vector3(reach)))
            if (StarField.SkyReads(cell) && StarField.OfCell(cell) is { } star)
                yield return star;
    }

    /// <summary>The stars of the sky with no other star of the sky within <paramref name="clearance"/> rad.</summary>
    private static IEnumerable<(Vector3 Cell, Star Star)> IsolatedStarsAround(Vector3 axis, float clearance)
    {
        Vector3 center = CellOf(axis);
        foreach (Vector3 cell in Cells(center - new Vector3(20), center + new Vector3(20)))
        {
            if (!StarField.SkyReads(cell) || StarField.OfCell(cell) is not { } star)
                continue;
            bool alone = SkyStarsNear(star.Direction, 3)
                .All(other => other == star || Vector3.Distance(other.Direction, star.Direction) > clearance);
            if (alone)
                yield return (cell, star);
        }
    }

    /// <summary>The part of the reflected stars that the mean star radiance gives, by the doc's formula.</summary>
    private static Vector3 MeanPart(Vector3 direction, float width)
    {
        float fade = StarField.Fade(width);
        return (1 - (1 - fade) * StarField.KeptShare(width)) * StarField.MeanRadiance(direction, PixelAngle);
    }

    private static Vector3 Fibonacci(int index, int count)
    {
        double golden = Math.PI * (3 - Math.Sqrt(5));
        double z = 1 - (2.0 * index + 1) / count;
        double ring = Math.Sqrt(1 - z * z);
        double azimuth = golden * index;
        return new((float)(ring * Math.Cos(azimuth)), (float)(ring * Math.Sin(azimuth)), (float)z);
    }

    [Fact]
    public void TheHashGivesTheShadersValuesInFloat()
    {
        // hash33 of lighting.frag, one float operation at a time in the shader's order.
        static Vector3 ShaderHash(Vector3 p)
        {
            float x = Fract(p.X * 0.1031f);
            float y = Fract(p.Y * 0.1030f);
            float z = Fract(p.Z * 0.0973f);
            float dot = x * (y + 33.33f) + y * (x + 33.33f) + z * (z + 33.33f);
            x += dot;
            y += dot;
            z += dot;
            return new(Fract((x + y) * z), Fract((x + x) * y), Fract((y + x) * x));
        }

        foreach (Vector3 cell in Cells(new(147, -3, 20), new(150, 3, 24)))
            StarField.Hash33(cell).Should().Be(ShaderHash(cell), $"cell {cell}");
    }

    [Fact]
    public void AStarIsTheShadersStarOfItsCell()
    {
        int stars = 0;
        foreach (Vector3 cell in Cells(new(140, -10, -10), new(150, 10, 10)))
        {
            Vector3 hash = StarField.Hash33(cell);
            Star? star = StarField.OfCell(cell);
            if (hash.X > 0.02f)
            {
                star.Should().BeNull($"cell {cell} holds no star");
                continue;
            }

            stars++;
            var direction = Vector3.Normalize(cell + StarField.Hash33(cell + new Vector3(17)));
            Vector3 peak = Vector3.Lerp(Warm, Cool, hash.Z) * (0.05f + 3f * MathF.Pow(hash.Y, 8));
            Star found = star!.Value;
            Vector3.Distance(found.Direction, direction).Should().BeLessThan(1e-6f, $"cell {cell}");
            Vector3.Distance(found.Peak, peak).Should().BeLessThan(1e-6f, $"cell {cell}");
        }

        stars.Should().BeGreaterThan(50, "the box holds about 4,600 cells");
    }

    [Fact]
    public void TheSkyReadsTheCellOfEachOfItsDirections()
    {
        var random = new Random(3);
        for (int index = 0; index < 10_000; index++)
        {
            var direction = Vector3.Normalize(
                new(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1)
            );
            StarField.SkyReads(CellOf(direction)).Should().BeTrue($"direction {direction}");
        }
    }

    [Fact]
    public void TheSkyReadsNoCellThatLiesWhollyInsideOrOutsideTheSphere()
    {
        StarField.SkyReads(new(0, 0, 0)).Should().BeFalse();
        StarField.SkyReads(new(148, 0, 0)).Should().BeFalse("the cell reaches to 149");
        StarField.SkyReads(new(151, 0, 0)).Should().BeFalse("the cell starts at 151");
        StarField.SkyReads(new(-151, 0, 0)).Should().BeTrue("the cell reaches from −151 to −150");
        StarField.SkyReads(new(106, 106, 6)).Should().BeFalse("its nearest corner lies 150.03 from the eye");
        StarField
            .SkyReads(new(106, 106, 5))
            .Should()
            .BeTrue("its nearest corner lies 149.99 and its farthest 151.4 away");
    }

    [Theory]
    [InlineData(0.045f)]
    [InlineData(0.07f)]
    [InlineData(0.1f)]
    [InlineData(0.2f)]
    [InlineData(0.316f)]
    public void AReflectedStarHasThePeakOfTheGgxLobe(float roughness)
    {
        var lobe = new GgxLobe(roughness);
        float width = StarField.LobeWidth(footprint: 0, roughness: roughness);

        double peak = Math.Pow(PixelAngle / width, 2);

        // The lobe's peak over the reflected directions is D(1) / 4 per sr, and the star's light is π p².
        double lobePeak = Math.PI * PixelAngle * PixelAngle * lobe.Distribution(1) / 4;
        peak.Should().BeApproximately(lobePeak, lobePeak * 1e-5);
    }

    [Fact]
    public void ThePixelsFootprintWidensAReflectedStarAsASecondBlur()
    {
        float alpha = (float)new GgxLobe(0.045).Alpha;

        StarField
            .LobeWidth(footprint: 3e-3f, roughness: 0.045f)
            .Should()
            .BeApproximately(MathF.Sqrt(9e-6f + 4 * alpha * alpha), 1e-9f);
        StarField
            .LobeWidth(footprint: 3e-3f, roughness: 0f)
            .Should()
            .Be(StarField.LobeWidth(footprint: 3e-3f, roughness: 0.045f), "the roughness is floored first");
    }

    [Theory]
    [InlineData(PixelAngle)]
    [InlineData(0.5f * Cell)]
    [InlineData(Cell)]
    [InlineData(1.5f * Cell)]
    [InlineData(1.9f * Cell)]
    public void AReflectedStarKeepsItsLightAtEveryWidth(float width)
    {
        (Vector3 _, Star star) = IsolatedStarsAround(Vector3.Normalize(new(1, 0.3f, 0.2f)), 3 * Cell).First();
        var right = Vector3.Normalize(Vector3.Cross(star.Direction, Vector3.UnitY));
        var up = Vector3.Cross(right, star.Direction);
        float fade = StarField.Fade(width);

        // Midpoint quadrature over the tangent plane at the star, out to 1.2 c, where its window has closed.
        const int steps = 600;
        double reach = 1.2 * Cell;
        double step = 2 * reach / steps;
        double r = 0,
            g = 0,
            b = 0;
        for (int row = 0; row < steps; row++)
        for (int column = 0; column < steps; column++)
        {
            double u = -reach + (column + 0.5) * step;
            double v = -reach + (row + 0.5) * step;
            var direction = Vector3.Normalize(star.Direction + (float)u * right + (float)v * up);
            double solidAngle = step * step / Math.Pow(1 + u * u + v * v, 1.5);
            Vector3 single =
                (StarField.Reflected(direction, width, PixelAngle) - MeanPart(direction, width)) / (1 - fade);
            r += single.X * solidAngle;
            g += single.Y * solidAngle;
            b += single.Z * solidAngle;
        }

        // The window keeps the share k(w) of the star's light b · π p².
        double light = Math.PI * PixelAngle * PixelAngle * StarField.KeptShare(width);
        r.Should().BeApproximately(star.Peak.X * light, star.Peak.X * light * 1e-3);
        g.Should().BeApproximately(star.Peak.Y * light, star.Peak.Y * light * 1e-3);
        b.Should().BeApproximately(star.Peak.Z * light, star.Peak.Z * light * 1e-3);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(2.52)]
    [InlineData(10)]
    [InlineData(100)]
    public void TheWindowKeepsTheShareThatTheClosedFormGives(double share)
    {
        // a = c² / w².
        float width = (float)(Cell / Math.Sqrt(share));
        double w = width;
        double c = Cell;

        // K(θ) = exp(−θ² / w²) (1 − θ² / c²)² over the plane, divided by the Gaussian's π w².
        const int steps = 400_000;
        double step = c / steps;
        double integral = 0;
        for (int index = 0; index < steps; index++)
        {
            double theta = (index + 0.5) * step;
            double window = 1 - theta * theta / (c * c);
            integral += Math.Exp(-theta * theta / (w * w)) * window * window * 2 * Math.PI * theta * step;
        }

        double kept = integral / (Math.PI * w * w);
        ((double)StarField.KeptShare(width)).Should().BeApproximately(kept, kept * 1e-6);
    }

    [Fact]
    public void TheReflectionReadsNoCellThatTheSkyNeverReads()
    {
        // A star of a cell that the sphere misses, in the block of a reflection at its center, with no star of the sky
        // near it: the reflection there is the mean alone.
        int checkedStars = 0;
        foreach (Vector3 cell in Cells(new(138, -40, -40), new(152, 40, 40)))
        {
            if (StarField.SkyReads(cell) || StarField.OfCell(cell) is not { } star)
                continue;
            Vector3 offset = CellOf(star.Direction) - cell;
            bool inBlock = MathF.Abs(offset.X) <= 1 && MathF.Abs(offset.Y) <= 1 && MathF.Abs(offset.Z) <= 1;
            bool noSkyStarNear = SkyStarsNear(star.Direction, 3)
                .All(other => Vector3.Distance(other.Direction, star.Direction) > 2 * Cell);
            if (!inBlock || !noSkyStarNear)
                continue;

            checkedStars++;
            StarField
                .Reflected(star.Direction, Cell, PixelAngle)
                .Should()
                .Be(MeanPart(star.Direction, Cell), $"the sky never reads cell {cell}");
        }

        checkedStars.Should().BeGreaterThan(5);
    }

    [Fact]
    public void TheStarDensityFollowsTheSumOfTheDirectionsAbsoluteComponents()
    {
        // The places of stars: each cell that the sky reads holds a star with the chance StarShare, which
        // TheMeanStarRadianceIsTheLightOfEveryStarSpreadOverTheSphere checks. A cap of 5° holds only about 11 stars
        // along an axis, so a count of its stars would measure their Poisson noise, about 30 %, not their density.
        static int PlacesInCap(Vector3 axis)
        {
            float cosine = MathF.Cos(5 * MathF.PI / 180);
            Vector3 center = CellOf(axis);
            return Cells(center - new Vector3(16), center + new Vector3(16))
                .Count(cell =>
                    StarField.SkyReads(cell) && Vector3.Dot(Vector3.Normalize(cell + new Vector3(0.5f)), axis) >= cosine
                );
        }

        var diagonal = Vector3.Normalize(Vector3.One);

        double ratio = (double)PlacesInCap(diagonal) / PlacesInCap(Vector3.UnitX);

        double expected =
            StarField.MeanRadiance(diagonal, PixelAngle).X / StarField.MeanRadiance(Vector3.UnitX, PixelAngle).X;
        expected.Should().BeApproximately(Math.Sqrt(3), 1e-5);
        ratio.Should().BeApproximately(expected, 0.05 * expected);
    }

    [Fact]
    public void TheMeanStarRadianceIsTheLightOfEveryStarSpreadOverTheSphere()
    {
        double r = 0,
            g = 0,
            b = 0;
        foreach (Vector3 cell in Cells(new(-151), new(150)))
        {
            if (!StarField.SkyReads(cell) || StarField.OfCell(cell) is not { } star)
                continue;
            r += star.Peak.X;
            g += star.Peak.Y;
            b += star.Peak.Z;
        }

        double light = Math.PI * PixelAngle * PixelAngle / (4 * Math.PI);
        const int count = 100_000;
        Vector3 mean = Vector3.Zero;
        for (int index = 0; index < count; index++)
            mean += StarField.MeanRadiance(Fibonacci(index, count), PixelAngle) / count;

        (r * light).Should().BeApproximately(mean.X, mean.X * 0.01);
        (g * light).Should().BeApproximately(mean.Y, mean.Y * 0.01);
        (b * light).Should().BeApproximately(mean.Z, mean.Z * 0.01);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(2.5f)]
    [InlineData(20f)]
    public void BeyondTwoCellsTheReflectedStarsAreTheirMeanAlone(float cells)
    {
        for (int index = 0; index < 20_000; index++)
        {
            Vector3 direction = Fibonacci(index, 20_000);
            StarField
                .Reflected(direction, cells * Cell, PixelAngle)
                .Should()
                .Be(StarField.MeanRadiance(direction, PixelAngle), $"direction {direction}");
        }
    }

    [Theory]
    [InlineData(0.63f)]
    [InlineData(1f)]
    public void NarrowerThanACellTheReflectedStarsShowSingleStars(float cells)
    {
        float width = cells * Cell;
        float meanPeak =
            StarField.MeanBrightness * (StarField.MeanTint.X + StarField.MeanTint.Y + StarField.MeanTint.Z);
        int bright = 0;
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.Normalize(Vector3.One) })
        foreach ((Vector3 _, Star star) in IsolatedStarsAround(axis, 2 * Cell))
        {
            Vector3 reflected = StarField.Reflected(star.Direction, width, PixelAngle);
            Vector3 mean = StarField.MeanRadiance(star.Direction, PixelAngle);
            reflected.X.Should().BeGreaterThan(mean.X, $"every star stands above the mean, {star}");
            if (star.Peak.X + star.Peak.Y + star.Peak.Z < meanPeak)
                continue;
            bright++;
            (reflected.X + reflected.Y + reflected.Z)
                .Should()
                .BeGreaterThanOrEqualTo(4 * (mean.X + mean.Y + mean.Z), $"a star as bright as the mean one, {star}");
        }

        bright.Should().BeGreaterThan(5);
    }

    /// <summary>The mean of the reflected stars over 4 million directions (tests/STYLE.md, "Long tests").</summary>
    public sealed class MeanOverTheSphere
    {
        [Theory]
        [InlineData(0.045f)]
        [InlineData(0.058f)]
        [InlineData(0.07f)]
        [InlineData(0.082f)]
        [InlineData(0.2f)]
        [InlineData(1f)]
        public void TheReflectedStarsKeepTheMeanRadianceOnAverageAtEveryRoughness(float roughness)
        {
            const int count = 4_000_000;
            const int chunks = 64;
            float width = StarField.LobeWidth(PixelAngle, roughness);
            var reflected = new Vector3[chunks];
            var means = new Vector3[chunks];

            // Each chunk sums its own directions in order, and the chunks add up in order, so the result is the same
            // on every run.
            Parallel.For(
                0,
                chunks,
                chunk =>
                {
                    Vector3 reflectedSum = Vector3.Zero;
                    Vector3 meanSum = Vector3.Zero;
                    for (int index = chunk; index < count; index += chunks)
                    {
                        Vector3 direction = Fibonacci(index, count);
                        reflectedSum += StarField.Reflected(direction, width, PixelAngle);
                        meanSum += StarField.MeanRadiance(direction, PixelAngle);
                    }

                    reflected[chunk] = reflectedSum;
                    means[chunk] = meanSum;
                }
            );
            Vector3 reflectedTotal = reflected.Aggregate(Vector3.Zero, (sum, part) => sum + part);
            Vector3 meanTotal = means.Aggregate(Vector3.Zero, (sum, part) => sum + part);

            reflectedTotal.X.Should().BeApproximately(meanTotal.X, 0.02f * meanTotal.X);
            reflectedTotal.Y.Should().BeApproximately(meanTotal.Y, 0.02f * meanTotal.Y);
            reflectedTotal.Z.Should().BeApproximately(meanTotal.Z, 0.02f * meanTotal.Z);
        }
    }
}

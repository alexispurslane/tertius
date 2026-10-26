using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The strand's octahedral code of two bytes in the shading target (docs/plans/skin-and-hair.md, "The shading target").
/// </summary>
public sealed class StrandCodeTests
{
    /// <summary>
    /// A Fibonacci lattice of <paramref name="count"/> unit directions, even over the sphere, and the six axes and the
    /// twelve directions between two axes, where the octahedral fold has its edges.
    /// </summary>
    private static IEnumerable<Vector3> DenseDirections(int count)
    {
        double goldenAngle = Math.PI * (3 - Math.Sqrt(5));
        for (int i = 0; i < count; i++)
        {
            double y = 1 - 2 * (i + 0.5) / count;
            double ring = Math.Sqrt(1 - y * y);
            double angle = goldenAngle * i;
            yield return Vector3.Normalize(
                new((float)(ring * Math.Cos(angle)), (float)y, (float)(ring * Math.Sin(angle)))
            );
        }
        float[] signs = [-1, 1];
        foreach (float a in signs)
        {
            yield return new(a, 0, 0);
            yield return new(0, a, 0);
            yield return new(0, 0, a);
            foreach (float b in signs)
            {
                yield return Vector3.Normalize(new(a, b, 0));
                yield return Vector3.Normalize(new(a, 0, b));
                yield return Vector3.Normalize(new(0, a, b));
            }
        }
    }

    [Fact]
    public void EveryUnitStrandSurvivesTheEightBitCodeWithinOneDegree()
    {
        double largestErrorDegrees = 0;
        foreach (Vector3 strand in DenseDirections(200_000))
        {
            (byte x, byte y) = StrandCode.Encode(strand);
            Vector3 decoded = StrandCode.Decode(x, y);
            double cosine = Math.Clamp(Vector3.Dot(strand, decoded), -1, 1);
            largestErrorDegrees = Math.Max(largestErrorDegrees, Math.Acos(cosine) * 180 / Math.PI);
        }

        // Measured on 2026-10-26: 0.94°, at the lattice's 200,000 directions.
        largestErrorDegrees.Should().BeLessThan(1, "one degree is the bound of the design for the 8-bit code");
    }
}

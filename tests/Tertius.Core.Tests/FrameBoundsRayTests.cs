using System.Numerics;

namespace Tertius.Core.Tests;

/// <summary>
/// Where a line crosses a frame's bubble, which a ray cast needs to cross from one frame to another. The line is
/// origin + t · direction, and the bubble is the ellipsoid of the frame's radii about the frame's origin. Each test works
/// out the answer with algebra in doubles.
/// </summary>
public sealed class FrameBoundsRayTests
{
    [Fact]
    public void ALineThroughTheCenterOfASphereEntersAndLeavesOneRadiusFromTheCenter()
    {
        var bubble = FrameBounds.Sphere(5);

        (double entry, double exit) = Crossing(bubble, origin: new(-10, 0, 0), direction: Double3.UnitX);

        Assert.Equal(5, entry, 1e-12);
        Assert.Equal(15, exit, 1e-12);
    }

    [Fact]
    public void ALineAlongEachAxisOfAnEllipsoidCrossesItAtThatAxisRadius()
    {
        var bubble = new FrameBounds(new(4, 2, 1));

        (double x0, double x1) = Crossing(bubble, origin: new(-10, 0, 0), direction: Double3.UnitX);
        (double y0, double y1) = Crossing(bubble, origin: new(0, -10, 0), direction: Double3.UnitY);
        (double z0, double z1) = Crossing(bubble, origin: new(0, 0, 10), direction: -Double3.UnitZ);

        Assert.Equal((6.0, 14.0), (Math.Round(x0, 12), Math.Round(x1, 12)));
        Assert.Equal((8.0, 12.0), (Math.Round(y0, 12), Math.Round(y1, 12)));
        Assert.Equal((9.0, 11.0), (Math.Round(z0, 12), Math.Round(z1, 12)));
    }

    [Fact]
    public void ALineThatPassesBesideTheBubbleDoesNotCrossIt()
    {
        var bubble = FrameBounds.Sphere(5);

        Assert.Null(bubble.IntersectLine(origin: new(-10, 5.001, 0), direction: Double3.UnitX));
    }

    [Fact]
    public void ALineFromInsideTheBubbleEntersBehindItsOrigin()
    {
        var bubble = FrameBounds.Sphere(5);

        (double entry, double exit) = Crossing(bubble, origin: new(1, 0, 0), direction: Double3.UnitZ);

        // The line x = 1, y = 0 meets x² + z² = 25 at z = ±√24.
        Assert.Equal(-Math.Sqrt(24), entry, 1e-12);
        Assert.Equal(Math.Sqrt(24), exit, 1e-12);
    }

    [Fact]
    public void TheLineParameterCountsInLengthsOfTheDirection()
    {
        var bubble = FrameBounds.Sphere(5);

        (double entry, double exit) = Crossing(bubble, origin: new(-10, 0, 0), direction: new(2, 0, 0));

        Assert.Equal(2.5, entry, 1e-12);
        Assert.Equal(7.5, exit, 1e-12);
    }

    [Fact]
    public void ASlantedLineCrossesAnEllipsoidAtTwoPointsOnItsSurface()
    {
        var radii = new Vector3(40, 20, 10);
        var bubble = new FrameBounds(radii);
        var origin = new Double3(-60, -15, 3);
        Double3 direction = new Double3(3, 1, 0.2).Normalized();

        (double entry, double exit) = Crossing(bubble, origin: origin, direction: direction);

        Assert.True(entry < exit);
        foreach (double t in new[] { entry, exit })
        {
            Double3 p = origin + direction * t;
            double onSurface = p.X * p.X / (40.0 * 40) + p.Y * p.Y / (20.0 * 20) + p.Z * p.Z / (10.0 * 10);
            Assert.Equal(1, onSurface, 1e-12);
        }

        Double3 middle = origin + direction * (0.5 * (entry + exit));
        Assert.True(middle.X * middle.X / 1600 + middle.Y * middle.Y / 400 + middle.Z * middle.Z / 100 < 1);
    }

    /// <summary>Where the line crosses the bubble, after checking that it does.</summary>
    private static (double Entry, double Exit) Crossing(FrameBounds bubble, Double3 origin, Double3 direction)
    {
        (double Entry, double Exit)? crossing = bubble.IntersectLine(origin, direction);
        Assert.NotNull(crossing);
        return crossing.GetValueOrDefault();
    }
}

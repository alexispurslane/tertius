using System.Numerics;
using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>Checks of the shuttle's panes of glass (<see cref="HullPane"/>) that the tests of the shuttle and of the long script share.</summary>
public static class PaneChecks
{
    /// <summary>The unit normal of the pane's hole, out of the hull: from the middle of its inner hole toward its outer hole.</summary>
    public static Double3 Normal(HullPane pane)
    {
        Double3 normal = Double3.Cross((Double3)pane.Outer[1] - pane.Outer[0], (Double3)pane.Outer[2] - pane.Outer[0])
            .Normalized();
        return Double3.Dot(normal, Middle(pane.Outer) - Middle(pane.Inner)) > 0 ? normal : -normal;
    }

    /// <summary>The middle of the corners of a hole.</summary>
    public static Double3 Middle(Vector3[] hole) =>
        hole.Aggregate(Double3.Zero, (sum, corner) => sum + corner) / hole.Length;

    /// <summary>
    /// True when the segment from <paramref name="from"/> to <paramref name="to"/>, in the shuttle's axes, crosses the plane
    /// of the flat, convex <paramref name="hole"/> inside the hole.
    /// </summary>
    public static bool CrossesTheHole(Vector3[] hole, Double3 from, Double3 to)
    {
        Double3 origin = hole[0];
        Double3 normal = Double3.Cross(hole[1] - origin, hole[2] - origin).Normalized();
        double before = Double3.Dot(normal, from - origin), after = Double3.Dot(normal, to - origin);
        if (!(before * after < 0))
            return false;
        Double3 hit = from + (to - from) * (before / (before - after));
        int sign = 0;
        for (int i = 0; i < hole.Length; i++)
        {
            Double3 a = hole[i], b = hole[(i + 1) % hole.Length];
            int side = Math.Sign(Double3.Dot(Double3.Cross(b - a, hit - a), normal));
            if (side != 0 && sign != 0 && side != sign)
                return false;
            sign = side == 0 ? sign : side;
        }

        return true;
    }

    /// <summary>True when the segment from <paramref name="from"/> to <paramref name="to"/> crosses both skins inside the holes of
    /// <paramref name="pane"/>.</summary>
    public static bool Through(HullPane pane, Double3 from, Double3 to) =>
        CrossesTheHole(pane.Outer, from, to) && CrossesTheHole(pane.Inner, from, to);
}

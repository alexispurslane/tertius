using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The interior shape of the ship in the scene <c>orbit</c>: the boxes that physics uses inside the ship's own frame. The
/// boxes must fill the hull's walls and the airlock chamber's walls, without overlaps, and leave both doorways open. The
/// expected volume comes from the sizes that the scene's doc states, not from the scene's code.
/// </summary>
public sealed class ShipInteriorTests
{
    // The sizes that the scene's doc states, in meters.
    private const double HullWidth = 20, HullHeight = 8, HullLength = 60, HullWall = 0.5;
    private const double ChamberDepth = 3, ChamberWidth = 3, ChamberWall = 0.2;
    private const double DoorwayWidth = 1.2, DoorwayHeight = 2.1;
    private static readonly BoundingBox[] Interior = [.. DemoScenes.ShipInterior()];

    /// <summary>The volume of <paramref name="box"/>, in m³.</summary>
    private static double Volume(BoundingBox box) =>
        ((double)box.Max.X - box.Min.X) * ((double)box.Max.Y - box.Min.Y) * ((double)box.Max.Z - box.Min.Z);

    /// <summary>The volume that <paramref name="a"/> and <paramref name="b"/> share, in m³: 0 if they only touch or stand apart.</summary>
    private static double Overlap(BoundingBox a, BoundingBox b)
    {
        static double Shared(float min0, float max0, float min1, float max1) =>
            Math.Max(0, (double)Math.Min(max0, max1) - Math.Max(min0, min1));

        return Shared(min0: a.Min.X, max0: a.Max.X, min1: b.Min.X, max1: b.Max.X) * Shared(min0: a.Min.Y, max0: a.Max.Y, min1: b.Min.Y, max1: b.Max.Y) *
               Shared(min0: a.Min.Z, max0: a.Max.Z, min1: b.Min.Z, max1: b.Max.Z);
    }

    [Fact]
    public void NoTwoInteriorBoxesOverlap()
    {
        for (int i = 0; i < Interior.Length; i++)
        for (int j = i + 1; j < Interior.Length; j++)
            Overlap(Interior[i], Interior[j]).Should()
                .BeLessThanOrEqualTo(1e-6, $"the boxes {Interior[i]} and {Interior[j]} only touch");
    }

    [Fact]
    public void TheInteriorIsTheHullsWallsAndTheChambersWallsLessTheirDoorways()
    {
        // The hull's walls are the hull less the cabin, and the hatch goes through the port wall.
        const double cabinHeight = HullHeight - 2 * HullWall;
        const double hullWalls = HullWidth * HullHeight * HullLength -
                                 (HullWidth - 2 * HullWall) * cabinHeight * (HullLength - 2 * HullWall);
        const double hatch = HullWall * DoorwayHeight * DoorwayWidth;
        // The chamber stands against the port wall, so it has walls on its other three sides, as tall as the cabin. The
        // inner doorway goes through the wall across from the port wall.
        const double chamberWalls =
            ((ChamberDepth + ChamberWall) * (ChamberWidth + 2 * ChamberWall) - ChamberDepth * ChamberWidth) *
            cabinHeight;
        const double innerDoorway = ChamberWall * DoorwayHeight * DoorwayWidth;

        Interior.Sum(Volume).Should().BeApproximately(hullWalls - hatch + chamberWalls - innerDoorway, 1e-4);
    }

    [Fact]
    public void NoInteriorBoxReachesIntoTheHatchOrTheInnerDoorway()
    {
        foreach (BoundingBox box in Interior)
        {
            Overlap(box, DemoScenes.Hatch).Should().BeLessThanOrEqualTo(1e-6, $"the box {box} leaves the hatch open");
            Overlap(box, DemoScenes.InnerDoorway).Should()
                .BeLessThanOrEqualTo(1e-6, $"the box {box} leaves the inner doorway open");
        }
    }
}

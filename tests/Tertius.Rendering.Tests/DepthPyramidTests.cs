using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the depth pyramid: its level count and sizes, the surface of each pixel of level 0, and the min-max
/// bounds of inverse depth of its cells (docs/plans/screen-space-reflections.md, "The depth pyramid" and "The pixel's
/// surface").
/// </summary>
public sealed class DepthPyramidTests
{
    /// <summary>A floor 1.5 m below the eye that reaches 1 km in every direction.</summary>
    private static readonly SceneRectangle[] Floor = [new(new(0, -1.5f, 0), new(1_000, 0, 0), new(0, 0, 1_000))];

    /// <summary>A camera of 64 × 48 pixels that looks 0.3 rad below the horizon, so the floor fills its lower part.</summary>
    private static CameraView FloorCamera => ScreenTesting.Camera(width: 64, height: 48, pitchDown: 0.3f);
    [Fact]
    public void TheTopLevelOfA1920By1080ScreenIsEleven() => DepthPyramid.LevelCount(1920, 1080).Should().Be(11);

    [Fact]
    public void AOnePixelScreenHasOneLevel() => DepthPyramid.LevelCount(1, 1).Should().Be(1);

    [Fact]
    public void EachLevelHalvesItsSidesRoundingUp()
    {
        (int Width, int Height)[] sizes =
        [
            (1920, 1080),
            (960, 540),
            (480, 270),
            (240, 135),
            (120, 68),
            (60, 34),
            (30, 17),
            (15, 9),
            (8, 5),
            (4, 3),
            (2, 2),
            (1, 1),
        ];

        for (int level = 0; level < sizes.Length; level++)
            DepthPyramid.LevelSize(1920, 1080, level).Should().Be(sizes[level], $"level {level}");
    }

    [Fact]
    public void ACellHoldsTheNearestAndTheFarthestInverseDepthOfItsSurfacePixels()
    {
        float[] inverseDepths = [0.1f, 0.5f, 0.3f, 0.3f, 0.25f, 0.2f, 0.3f, 0.3f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 4, height: 2);

        pyramid.Bounds(1, 0, 0).Should().Be(new CellBounds(Near: 0.5f, Far: 0.1f));
        pyramid.Bounds(0, 2, 1).Should().Be(CellBounds.OfSurface(0.3f));
    }

    [Fact]
    public void SkyAddsNoSurfaceToACell()
    {
        float[] inverseDepths = [0.1f, 0, 0.25f, 0.2f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 2, height: 2);

        pyramid.Bounds(1, 0, 0).Should().Be(new CellBounds(Near: 0.25f, Far: 0.1f));
    }

    [Fact]
    public void ACellOfOnlySkyHasEmptyBoundsAndAnEmptySlab()
    {
        float[] inverseDepths = [0, 0, 0, 0, 0.2f, 0.2f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 2, height: 3);
        CellBounds sky = pyramid.Bounds(1, 0, 0);

        sky.Should().Be(new CellBounds(Near: 0, Far: CellBounds.EmptyFar));
        (float low, float high) = sky.Slab(CellBounds.ThicknessShare);
        low.Should().BeGreaterThan(high, "no inverse depth lies in the slab of a cell without a surface");
    }

    [Fact]
    public void ACellOutsideItsLevelHoldsNoSurface()
    {
        float[] inverseDepths = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 3, height: 2);

        pyramid.Bounds(0, -1, 0).Should().Be(CellBounds.Empty);
        pyramid.Bounds(0, 3, 1).Should().Be(CellBounds.Empty);
        pyramid.Bounds(0, 0, 2).Should().Be(CellBounds.Empty);
        pyramid.Bounds(1, 2, 0).Should().Be(CellBounds.Empty);
        pyramid.Bounds(1, 0, -1).Should().Be(CellBounds.Empty);
    }

    [Fact]
    public void TheLastCellOfAnOddLevelCoversTheRemainingPixels()
    {
        // 5 × 3 pixels: level 1 has 3 × 2 cells, and its last cell holds the one pixel (4, 2).
        float[] inverseDepths = [.. Enumerable.Repeat(0.1f, 15)];
        inverseDepths[2 * 5 + 4] = 0.7f;
        inverseDepths[0 * 5 + 4] = 0.4f;
        inverseDepths[1 * 5 + 4] = 0.5f;

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 5, height: 3);

        pyramid.Bounds(1, 2, 1).Should().Be(CellBounds.OfSurface(0.7f));
        pyramid.Bounds(1, 2, 0).Should().Be(new CellBounds(Near: 0.5f, Far: 0.4f));
        pyramid.Bounds(2, 1, 0).Should().Be(new CellBounds(Near: 0.7f, Far: 0.4f));
    }

    [Fact]
    public void TheTopCellBoundsEveryPixel()
    {
        var random = new Random(7);
        float[] inverseDepths =
        [
            .. Enumerable.Range(0, 37 * 23).Select(_ => random.Next(4) == 0 ? 0 : random.NextSingle()),
        ];
        float[] surfaces = [.. inverseDepths.Where(inverseDepth => inverseDepth > 0)];

        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 37, height: 23);

        pyramid.TopLevel.Should().Be(6);
        pyramid.Bounds(pyramid.TopLevel, 0, 0).Should().Be(new CellBounds(Near: surfaces.Max(), Far: surfaces.Min()));
    }

    [Fact]
    public void AThicknessShareOfOneTenthFillsATenthOfTheDepthBehindASurface()
    {
        (float low, float high) = CellBounds.OfSurface(1 / 5f).Slab(CellBounds.ThicknessShare);

        (1 / high).Should().BeApproximately(5, 1e-5f);
        (1 / low).Should().BeApproximately(5.5f, 1e-5f, "a surface 5 m away fills the depths to 5.5 m");
    }

    [Fact]
    public void AFloorPixelWhoseNeighborsContinueItsPlaneBoundsThePlaneAtTheCornersOfItsSquare()
    {
        CameraView camera = FloorCamera;

        CellBounds bounds = ScreenTesting.Pyramid(camera, Floor).Bounds(0, 32, 40);

        float[] corners = [.. Corners(32, 40).Select(corner => FloorInverseDepth(camera, corner))];
        bounds.Near.Should().BeApproximately(corners.Max(), 1e-5f * corners.Max(), "the plane is nearest at a lower corner");
        bounds.Far.Should().BeApproximately(corners.Min(), 1e-5f * corners.Min(), "and farthest at an upper corner");
    }

    [Fact]
    public void TowardTheSkyAPixelsPlaneStopsAtItsCenterLine()
    {
        CameraView camera = FloorCamera;
        // The floor ends 20 m ahead, and the sky lies beyond its far edge.
        SceneRectangle[] floor = [new(new(0, -1.5f, -10), new(1_000, 0, 0), new(0, 0, 10))];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, floor);
        int edge = Enumerable.Range(0, camera.Height).First(y => pyramid.Surface(32, y) is not null);

        CellBounds bounds = pyramid.Bounds(0, 32, edge);

        pyramid.Surface(32, edge - 1).Should().BeNull("the row above the floor's far edge sees the sky");
        float centerLine = FloorInverseDepth(camera, new(32, edge + 0.5f));
        bounds.Far.Should().BeApproximately(centerLine, 1e-5f * centerLine, "past its center line the surface keeps the plane's value there");
    }

    [Fact]
    public void AtTheScreensEdgeAPixelsPlaneReachesThePixelsEdge()
    {
        CameraView camera = FloorCamera;

        CellBounds bounds = ScreenTesting.Pyramid(camera, Floor).Bounds(0, 32, camera.Height - 1);

        float bottomEdge = FloorInverseDepth(camera, new(32, camera.Height));
        bounds.Near.Should().BeApproximately(bottomEdge, 1e-5f * bottomEdge, "no depth edge lies beyond the screen");
    }

    [Theory]
    [InlineData(0.008f, true)]
    [InlineData(-0.008f, true)]
    [InlineData(0.012f, false)]
    [InlineData(-0.012f, false)]
    public void ANeighborWithinOnePercentOfThePlaneInInverseDepthContinuesIt(float offPlane, bool continues)
    {
        CameraView camera = FloorCamera;
        (float[] distances, Vector3[] normals) = ScreenTesting.Surfaces(camera, Floor);
        // The neighbor above stands off the floor's plane by the share offPlane of its inverse depth.
        distances[39 * camera.Width + 32] /= 1 + offPlane;

        CellBounds bounds = DepthPyramid.Of(camera, distances, normals).Bounds(0, 32, 40);

        float reach = FloorInverseDepth(camera, new(32, continues ? 40 : 40.5f));
        bounds.Far.Should().BeApproximately(reach, 1e-5f * reach, "the plane reaches the pixel's top edge only toward a neighbor that continues it");
    }

    [Fact]
    public void APlaneThatPassesTheCenterOfAFarNeighborByChanceStopsAtItsCenterLineTowardIt()
    {
        // A near pixel sees a steep surface whose plane runs through the center point of the wall pixel below it, as a
        // surface seen at a grazing angle can at its outline. The wall's plane faces the eye and never passes back
        // through the near pixel's center, so the two pixels see two surfaces with a depth edge between them.
        CameraView camera = ScreenTesting.Camera(width: 64, height: 48);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];
        (float[] distances, Vector3[] normals) = ScreenTesting.Surfaces(camera, wall);
        Vector3 nearPoint = Vector3.Normalize(camera.ViewRayThrough(new(20.5f, 30.5f))) * 5;
        Vector3 wallPoint = camera.ViewRayThrough(new(20.5f, 31.5f)) * 10;
        distances[30 * camera.Width + 20] = 5;
        normals[30 * camera.Width + 20] = Vector3.Normalize(Vector3.Cross(wallPoint - nearPoint, camera.Right));

        PixelSurface surface = DepthPyramid.Of(camera, distances, normals).Surface(20, 30)!.Value;

        surface.High.Y.Should().Be(30.5f, "the plane stops at the center line toward a neighbor whose plane does not reach back");
    }

    [Fact]
    public void APixelThatFacesTheEyeBoundsOnlyItsCentersInverseDepth()
    {
        CameraView camera = ScreenTesting.Camera(width: 64, height: 48);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];

        CellBounds bounds = ScreenTesting.Pyramid(camera, wall).Bounds(0, 20, 30);

        bounds.Should().Be(CellBounds.OfSurface(0.1f), "a plane that faces the eye has one inverse depth over the pixel");
    }

    [Fact]
    public void ALevelAboveZeroBoundsTheSurfacesOfItsPixels()
    {
        CameraView camera = FloorCamera;

        CellBounds cell = ScreenTesting.Pyramid(camera, Floor).Bounds(1, 16, 20);

        float near = FloorInverseDepth(camera, new(32, 42));
        float far = FloorInverseDepth(camera, new(32, 40));
        cell.Near.Should().BeApproximately(near, 1e-5f * near, "the cell's pixels' planes reach its bottom edge");
        cell.Far.Should().BeApproximately(far, 1e-5f * far, "and its top edge");
    }

    [Fact]
    public void AnEdgeOnPixelKeepsItsCentersInverseDepthOverItsWholeSquare()
    {
        // The screen's middle pixel sees the wall, but its geometric normal stands at a right angle to its view ray. Its
        // plane then holds that ray, and the plane's inverse depth would change without bound across the pixel.
        CameraView camera = ScreenTesting.Camera(width: 65, height: 49);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];
        (float[] distances, Vector3[] normals) = ScreenTesting.Surfaces(camera, wall);
        normals[24 * camera.Width + 32] = Vector3.UnitY;

        CellBounds bounds = DepthPyramid.Of(camera, distances, normals).Bounds(0, 32, 24);

        bounds.Should().Be(CellBounds.OfSurface(0.1f), "an edge-on plane keeps its center's inverse depth, with no gradient");
    }

    [Fact]
    public void APlaneStopsAtItsCenterLineTowardAnEdgeOnNeighbor()
    {
        // A wall that leans back, so that its inverse depth falls by about 0.24 % per row upward, well within 1 %. The
        // screen's middle pixel sees it with a normal at a right angle to its view ray: an edge-on plane, whose center
        // lies on the plane of the pixel below, and whose one inverse depth holds that pixel's center within 1 %.
        CameraView camera = ScreenTesting.Camera(width: 65, height: 49);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, -2))];
        (float[] distances, Vector3[] normals) = ScreenTesting.Surfaces(camera, wall);
        normals[24 * camera.Width + 32] = Vector3.UnitY;

        CellBounds bounds = DepthPyramid.Of(camera, distances, normals).Bounds(0, 32, 25);

        float centerLine = (float)(1 / ScreenTesting.DepthAlong(camera.ViewRayThrough(new(32.5f, 25.5f)), wall)!.Value);
        bounds.Far.Should().BeApproximately(centerLine, 1e-5f * centerLine, "no plane continues into an edge-on neighbor");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APyramidOfAFrameRefusesOneDistanceOrOneNormalTooFew(bool fewerDistances)
    {
        CameraView camera = FloorCamera;
        (float[] distances, Vector3[] normals) = ScreenTesting.Surfaces(camera, Floor);

        Action build = () =>
            DepthPyramid.Of(camera, fewerDistances ? distances[1..] : distances, fewerDistances ? normals : normals[1..]);

        build.Should().Throw<ArgumentException>("the pyramid needs one distance and one normal for each pixel");
    }

    /// <summary>The four corners of a pixel's square, in pixels.</summary>
    private static Vector2[] Corners(int x, int y) => [new(x, y), new(x + 1, y), new(x, y + 1), new(x + 1, y + 1)];

    /// <summary>The inverse view depth of the floor 1.5 m below the eye on the view ray through a screen position.</summary>
    private static float FloorInverseDepth(in CameraView camera, Vector2 position) =>
        (float)(1 / ScreenTesting.DepthAlong(camera.ViewRayThrough(position), Floor)!.Value);
}

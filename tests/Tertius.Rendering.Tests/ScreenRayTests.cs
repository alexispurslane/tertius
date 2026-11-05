using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of a screen ray: its image on the screen, where it ends, its origin and its surface fade, and the walk's
/// hits on a planet at the ray's vanishing point (docs/plans/screen-space-reflections.md, "The ray's image on the
/// screen"). <see cref="ScreenWalkTests"/> holds the walk's other tests. The scenes are synthetic: rectangles seen by a
/// camera built by hand.
/// </summary>
public sealed class ScreenRayTests
{
    private const int Width = 160;
    private const int Height = 120;
    private const int MaxSteps = 64;
    private static readonly Vector3 FloorNormal = Vector3.UnitY;

    /// <summary>The image of a ray from a bare origin, whose own view depth is the depth that the trace expects of it.</summary>
    private static ScreenRay RayOf(Vector3 origin, Vector3 direction, in CameraView camera) =>
        ScreenRay.Of(
            origin: origin,
            direction: direction,
            viewDepth: Vector3.Dot(origin, camera.Forward),
            camera: camera
        );

    [Fact]
    public void ARayThatLeavesTheScreenEndsAtTheScreenEdge()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);

        ScreenRay ray = RayOf(new(-1, 0.2f, -4), Vector3.UnitX, camera);
        RayTrace trace = ray.Walk(sky, Vector3.UnitX, MaxSteps);

        trace.TryGetValue(out RayLeftTheSegment left).Should().BeTrue($"nothing lies in its way, not {trace}");
        left.End.Should().Be(SegmentEnd.ScreenEdge);
        (ray.Start + ray.Step * ray.End).X.Should().BeApproximately(Width, 1e-2f);
    }

    [Fact]
    public void ARayTowardTheEyeEndsAtTheNearPlane()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        var origin = new Vector3(0.1f, 0, -2);
        // Past the near plane, 5 cm away, close to the middle of the screen.
        var target = new Vector3(0.01f, 0.003f, -0.04f);

        ScreenRay ray = RayOf(origin, Vector3.Normalize(target - origin), camera);

        ray.EndsAt.Should().Be(SegmentEnd.NearPlane);
        (ray.StartInverseDepth + ray.StepInverseDepth * ray.End)
            .Should()
            .BeApproximately(1 / DepthRanges.NearPlane, 1e-3f);
    }

    [Fact]
    public void ARayAwayFromTheEyeWhoseVanishingPointIsOnTheScreenEndsThere()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        var direction = Vector3.Normalize(new(0.2f, 0.1f, -1));
        Vector2 vanishingPoint = camera.PixelOf(direction);

        ScreenRay ray = RayOf(new(-1, 0, -3), direction, camera);
        Vector2 end = ray.Start + ray.Step * ray.End;

        ray.EndsAt.Should().Be(SegmentEnd.VanishingPoint);
        end.X.Should().BeApproximately(vanishingPoint.X, 0.05f);
        end.Y.Should().BeApproximately(vanishingPoint.Y, 0.05f);
        (ray.StartInverseDepth + ray.StepInverseDepth * ray.End).Should().BeApproximately(0, 1e-6f);
    }

    [Fact]
    public void ARayNearlyParallelToTheImagePlaneKeepsItsDepthAndRunsTheWayItsImageRuns()
    {
        // A camera off the axes, so that the camera-relative components of a point far along the ray round.
        CameraView camera = ScreenTesting.Camera(width: 640, height: 360, yaw: 0.7f, pitchDown: 0.4f);
        Vector3 origin = camera.Forward * 5 - camera.Right - camera.Up * 0.5f;
        float originInverseDepth = camera.InverseDepthOf(origin);
        int tested = 0;

        for (int turn = 0; turn < 64; turn++)
        for (int tilt = 1; tilt <= 16; tilt++)
        {
            float angle = turn * MathF.Tau / 64;
            var direction = Vector3.Normalize(
                camera.Right * MathF.Cos(angle) + camera.Up * MathF.Sin(angle) - camera.Forward * (tilt * 4e-8f)
            );
            // Toward the eye by less than 10⁻⁶ of the ray's length: the second point of the old rule lay over 2.5 · 10⁶ m out.
            if (Vector3.Dot(direction, camera.Forward) is >= 0 or < -1e-6f)
                continue;
            tested++;

            ScreenRay ray = RayOf(origin, direction, camera);
            Vector2 image = camera.PixelOf(origin + direction * 0.1f) - ray.Start;

            Vector2
                .Dot(ray.Step, image)
                .Should()
                .BePositive("the segment runs the way the ray's image runs, at {0} rad and tilt {1}", angle, tilt);
            (ray.StartInverseDepth + ray.StepInverseDepth * ray.End)
                .Should()
                .BeApproximately(
                    originInverseDepth,
                    1e-4f * originInverseDepth,
                    "the ray keeps its depth across the screen, at {0} rad and tilt {1}",
                    angle,
                    tilt
                );
        }

        tested.Should().BeGreaterThan(100, "most of the directions come toward the eye by a hair");
    }

    [Fact]
    public void ARayFromANearFloorHitsAPlanetAtItsVanishingPoint()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] scene =
        [
            new(new(0, -0.5f, -25), new(10, 0, 0), new(0, 0, 25)),
            new(new(0, 0, -1e7f), new(1e8f, 0, 0), new(0, 1e8f, 0)),
        ];
        // The center of the floor pixel of the point (0.2, −0.5, −1.9), about 2 m away.
        Vector2 pixel = Floor(camera.PixelOf(new(0.2f, -0.5f, -1.9f))) + new Vector2(0.5f);
        Vector3 viewRay = camera.ViewRayThrough(pixel);
        float depth = (float)ScreenTesting.DepthAlong(viewRay, scene)!.Value;
        Vector3 point = viewRay * depth;
        var direction = Vector3.Reflect(Vector3.Normalize(point), FloorNormal);
        Vector2 vanishingPoint = camera.PixelOf(direction);

        RayTrace trace = ScreenRay
            .FromSurface(
                point: point,
                viewDepth: depth,
                geometricNormal: FloorNormal,
                direction: direction,
                camera: camera
            )
            .Walk(ScreenTesting.Pyramid(camera, scene), direction, MaxSteps);

        Vector3.Distance(point, Vector3.Zero).Should().BeInRange(1.9f, 2.1f);
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the planet covers the vanishing point, not {trace}");
        hit.Pixel.Should().Be(((int)MathF.Floor(vanishingPoint.X), (int)MathF.Floor(vanishingPoint.Y)));
        hit.EntryShare.Should().Be(0);
    }

    // The rounding of the ray's inverse depth near its vanishing point is larger than the slab of a surface 10,000 km
    // away (docs/plans/screen-space-reflections.md, "Precision and determinism"). Of these 3,613 rays, the walk leaves
    // 209 at the vanishing point without meeting the slab, and gives 531 hits an entry share of 0.05 to 0.59.
    [Fact(Skip = "The walk misjudges a far slab near a vanishing point; skipped until ter-186q fixes it.")]
    public void EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanetThroughTheFrontOfItsSlab() =>
        PlanetHitsOfFloorRays()
            .Where(ray => ray.Trace is not RayHit { EntryShare: 0 } hit || hit.Pixel != ray.VanishingPixel)
            .Select(ray => ray.ToString())
            .Should()
            .BeEmpty("the planet covers the vanishing point of each ray, and each ray comes to it from in front");

    /// <summary>
    /// The walks of the mirror rays of a floor 1.5 m below the eye, from every 7th pixel of every 3rd row whose vanishing
    /// point lies on the screen, under a planet that covers the sky, and the pixel of each ray's vanishing point.
    /// </summary>
    private static List<PlanetRay> PlanetHitsOfFloorRays()
    {
        CameraView camera = ScreenTesting.Camera(width: 640, height: 360, pitchDown: 0.1f);
        SceneRectangle[] scene =
        [
            new(new(0, -1.5f, -25), new(30, 0, 0), new(0, 0, 25)),
            new(new(0, 0, -1e7f), new(1e8f, 0, 0), new(0, 1e8f, 0)),
        ];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, scene);
        var rays = new List<PlanetRay>();
        for (int y = 0; y < camera.Height; y += 3)
        for (int x = 0; x < camera.Width; x += 7)
        {
            Vector3 viewRay = camera.ViewRayThrough(new(x + 0.5f, y + 0.5f));
            if (ScreenTesting.DepthAlong(viewRay, scene) is not { } depth || depth > 100)
                continue;
            Vector3 point = viewRay * (float)depth;
            var direction = Vector3.Reflect(Vector3.Normalize(point), FloorNormal);
            Vector2 vanishingPoint = camera.PixelOf(direction);
            bool onScreen = vanishingPoint.X is > 1 and < 639 && vanishingPoint.Y is > 1 and < 359;
            if (!onScreen)
                continue;
            RayTrace trace = ScreenRay
                .FromSurface(
                    point: point,
                    viewDepth: (float)depth,
                    geometricNormal: FloorNormal,
                    direction: direction,
                    camera: camera
                )
                .Walk(pyramid, direction, ScreenTrace.MaxStepsLimit);
            rays.Add(new((x, y), trace, ((int)MathF.Floor(vanishingPoint.X), (int)MathF.Floor(vanishingPoint.Y))));
        }

        rays.Count.Should().BeGreaterThan(1000);
        return rays;
    }

    /// <summary>The walk of the mirror ray of floor pixel <paramref name="Pixel"/>, and the pixel of its vanishing point.</summary>
    private readonly record struct PlanetRay((int X, int Y) Pixel, RayTrace Trace, (int X, int Y) VanishingPixel);

    private static Vector2 Floor(Vector2 position) => new(MathF.Floor(position.X), MathF.Floor(position.Y));

    [Fact]
    public void AFlatSurfacesOriginLiesTwoPixelFootprintsAboveIt()
    {
        CameraView camera = ScreenTesting.Camera(width: 64, height: 48, pitchDown: 0.5f);
        SceneRectangle[] floor = [new(new(0, -1.5f, 0), new(100, 0, 0), new(0, 0, 100))];
        Vector3 viewRay = camera.ViewRayThrough(new(40.5f, 40.5f));
        float depth = (float)ScreenTesting.DepthAlong(viewRay, floor)!.Value;
        Vector3 point = viewRay * depth;
        float footprint = depth * camera.PixelAngle;

        var ray = ScreenRay.FromSurface(
            point: point,
            viewDepth: depth,
            geometricNormal: FloorNormal,
            direction: Vector3.Normalize(new(0.3f, 0.5f, -1)),
            camera: camera
        );
        Vector3 origin = camera.ViewRayThrough(ray.Start) / ray.StartInverseDepth;

        Vector3 offset = origin - point;
        offset.Y.Should().BeApproximately(2 * footprint, 1e-3f * footprint);
        offset.X.Should().BeApproximately(0, 1e-3f * footprint);
        offset.Z.Should().BeApproximately(0, 1e-3f * footprint);
    }

    [Theory]
    // A pixel footprint of 0.58 view depths in a window 2 px tall at 60°, 0.5 in one 4 px tall at 90°, and 0.64 at 179°
    // in 640 × 360: two footprints reach the view depth, so an uncapped offset puts the origin on or behind the eye.
    [InlineData(640, 2, 60f)]
    [InlineData(640, 4, 90f)]
    [InlineData(640, 360, 179f)]
    public void TheOriginOfASurfaceThatFacesTheEyeLiesHalfItsViewDepthDeepWhenTwoFootprintsReachTheEye(
        int width,
        int height,
        float fovDegrees
    )
    {
        CameraView camera = ScreenTesting.Camera(width: width, height: height, fov: fovDegrees * MathF.PI / 180);
        const float viewDepth = 5;
        Vector3 point = camera.Forward * viewDepth;

        var ray = ScreenRay.FromSurface(
            point: point,
            viewDepth: viewDepth,
            geometricNormal: -camera.Forward,
            direction: Vector3.Normalize(0.2f * camera.Right - camera.Forward),
            camera: camera
        );

        ray.StartInverseDepth.Should()
            .BeApproximately(
                2 / viewDepth,
                1e-5f / viewDepth,
                "the offset is at most half the view depth, so the origin stays in front of the eye"
            );
    }

    [Fact]
    public void InAViewNearlyAHalfTurnWideEveryRayWithASegmentRunsFromDepthsNearItsSurfacesViewDepth()
    {
        // The largest field of view below π that the batcher takes, in 16:9, turned off the axes so that the camera's
        // axes round: the view rays at the screen's sides are about 10^7 long, and the rounding of their sums passes
        // their forward part of 1, so a surface point's computed depth can be 0 or below.
        float fov = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(MathF.PI) - 1);
        CameraView camera = ScreenTesting.Camera(width: 640, height: 360, yaw: 0.3f, pitchDown: 0.2f, fov: fov);
        const float viewDepth = 1;
        var nearTheEye = new List<string>();
        int rays = 0,
            empty = 0;

        for (int y = 0; y < camera.Height; y += 9)
        for (int x = 0; x < camera.Width; x += 16)
        {
            // As reflection_trace.comp starts it: the point from the pixel's distance, the view depth from the distance
            // over the view ray's length.
            Vector3 viewRay = camera.ViewRayThrough(new(x + 0.5f, y + 0.5f));
            Vector3 point = Vector3.Normalize(viewRay) * (viewDepth * viewRay.Length());
            var ray = ScreenRay.FromSurface(
                point: point,
                viewDepth: viewDepth,
                geometricNormal: -camera.Forward,
                direction: Vector3.Reflect(Vector3.Normalize(viewRay), -camera.Forward),
                camera: camera
            );
            rays++;
            if (ray.EndsAt == SegmentEnd.NoSegment)
            {
                empty++;
                continue;
            }
            float second = ray.StartInverseDepth + ray.StepInverseDepth;
            bool deepEnough = ray.StartInverseDepth is > 0 and <= 4 / viewDepth && second is > 0 and <= 8 / viewDepth;
            if (!deepEnough)
                nearTheEye.Add($"({x}, {y}): inverse depths {ray.StartInverseDepth} and {second}");
        }

        nearTheEye
            .Should()
            .BeEmpty(
                "a ray runs only from an origin at least a quarter of its view depth deep, through a second point at least "
                    + "an eighth deep, of {0} rays",
                rays
            );
        empty.Should().BePositive("the rounding puts some origins near the eye");
    }

    [Fact]
    public void ARayIntoItsOwnSurfaceHasNoSurfaceFadeAndAGrazingOneHalf()
    {
        float grazing = ScreenRay.SurfaceFadeCosine / 2;

        ScreenRay.SurfaceFade(Vector3.Normalize(new(0, -0.3f, -1)), FloorNormal).Should().Be(0);
        ScreenRay
            .SurfaceFade(new(MathF.Sqrt(1 - grazing * grazing), grazing, 0), FloorNormal)
            .Should()
            .BeApproximately(0.5f, 1e-5f);
        ScreenRay.SurfaceFade(FloorNormal, FloorNormal).Should().Be(1);
    }
}

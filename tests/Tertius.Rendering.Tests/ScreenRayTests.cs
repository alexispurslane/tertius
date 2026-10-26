using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the reflection trace: the ray's image on the screen, where it ends, and its walk through the depth
/// pyramid with the slab rule (docs/plans/screen-space-reflections.md, "The ray's image on the screen" and "The trace
/// through the pyramid"). The scenes are synthetic: rectangles seen by a camera built by hand.
/// </summary>
public sealed class ScreenRayTests
{
    private const int Width = 160;
    private const int Height = 120;
    private const int MaxSteps = 64;
    private static readonly Vector3 FloorNormal = Vector3.UnitY;

    [Fact]
    public void ARayRisingFromAFlatFloorDoesNotHitTheFloor()
    {
        SceneRectangle[] floor = [new(new(0, -1.5f, 0), new(1_000, 0, 0), new(0, 0, 1_000))];
        var hits = new List<string>();
        int rays = 0;

        foreach (float pitchDegrees in new[] { 10f, 35f, 60f })
        {
            CameraView camera = ScreenTesting.Camera(width: 64, height: 48, pitchDown: pitchDegrees * MathF.PI / 180);
            DepthPyramid pyramid = ScreenTesting.Pyramid(camera, floor);
            for (int y = 0; y < camera.Height; y++)
            for (int x = 0; x < camera.Width; x++)
            {
                Vector3 viewRay = camera.ViewRayThrough(new(x + 0.5f, y + 0.5f));
                if (ScreenTesting.DepthAlong(viewRay, floor) is not { } depth)
                    continue;
                Vector3 point = viewRay * (float)depth;
                foreach (Vector3 direction in RisingDirections())
                {
                    var ray = ScreenRay.FromSurface(
                        point: point,
                        viewDepth: (float)depth,
                        geometricNormal: FloorNormal,
                        direction: direction,
                        camera: camera
                    );
                    rays++;
                    if (ray.Trace(pyramid, MaxSteps) is RayHit hit)
                        hits.Add($"pitch {pitchDegrees}°, pixel ({x}, {y}), direction {direction}: {hit}");
                }
            }
        }

        rays.Should().BeGreaterThan(100_000, "the floor fills most of each screen");
        hits.Should().BeEmpty("a ray that rises from a flat floor stays in front of it");
    }

    /// <summary>The image of a ray from a bare origin, whose own view depth is the depth that the trace expects of it.</summary>
    private static ScreenRay RayOf(Vector3 origin, Vector3 direction, in CameraView camera) =>
        ScreenRay.Of(
            origin: origin,
            direction: direction,
            viewDepth: Vector3.Dot(origin, camera.Forward),
            camera: camera
        );

    /// <summary>Unit directions 5° to 80° above the floor, all around.</summary>
    private static IEnumerable<Vector3> RisingDirections()
    {
        foreach (float elevation in new[] { 5f, 20f, 45f, 80f })
            for (int azimuth = 0; azimuth < 8; azimuth++)
            {
                float up = elevation * MathF.PI / 180;
                float around = azimuth * MathF.PI / 4;
                yield return new(MathF.Cos(up) * MathF.Cos(around), MathF.Sin(up), MathF.Cos(up) * MathF.Sin(around));
            }
    }

    [Fact]
    public void ARayHitsTheFrontOfAWallInThePixelWhereItMeetsIt()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];
        var origin = new Vector3(-1, -1, -5);
        var toWall = new Vector3(1.5f, 1.25f, -5);
        Vector2 meeting = camera.PixelOf(origin + toWall);

        RayTrace trace = RayOf(origin, Vector3.Normalize(toWall), camera)
            .Trace(ScreenTesting.Pyramid(camera, wall), MaxSteps);

        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the ray meets the wall, not {trace}");
        hit.Pixel.Should().Be(((int)MathF.Floor(meeting.X), (int)MathF.Floor(meeting.Y)));
        hit.EntryShare.Should().Be(0, "the ray comes from in front of the wall");
        hit.Position.X.Should().BeApproximately(meeting.X, 1e-3f);
        hit.Position.Y.Should().BeApproximately(meeting.Y, 1e-3f);
    }

    [Fact]
    public void ARayThatStartsBesideTheScreenCrossesIntoItAndHitsAWall()
    {
        // An origin two screen widths left of an 8 px screen, whose top level is 3: the trace climbs to the top level
        // in the empty cells beside the screen before it reaches the screen's own cells.
        CameraView camera = ScreenTesting.Camera(width: 8, height: 8);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];
        var origin = new Vector3(-17.3f, 0.1f, -5);
        var target = new Vector3(0.3f, 0.1f, -10);
        Vector2 meeting = camera.PixelOf(target);

        ScreenRay ray = RayOf(origin, Vector3.Normalize(target - origin), camera);
        RayTrace trace = ray.Trace(ScreenTesting.Pyramid(camera, wall), MaxSteps);

        ray.Start.X.Should().BeLessThan(-16);
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the ray meets the wall on the screen, not {trace}");
        hit.Pixel.Should().Be(((int)MathF.Floor(meeting.X), (int)MathF.Floor(meeting.Y)));
    }

    [Fact]
    public void ARayThatPassesFarBehindAWallMissesIt()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // A bar 5 m away, whose slab reaches 5.5 m, and a ray that crosses behind it 8 m away.
        SceneRectangle[] bar = [new(new(0, 0, -5), new(0.5f, 0, 0), new(0, 10, 0))];

        RayTrace trace = RayOf(new(-3, 0.3f, -8), Vector3.UnitX, camera)
            .Trace(ScreenTesting.Pyramid(camera, bar), MaxSteps);

        trace.TryGetValue(out RayLeftTheSegment left).Should().BeTrue($"the ray passes behind the bar, not {trace}");
        left.End.Should().Be(SegmentEnd.ScreenEdge);
    }

    [Fact]
    public void ARayThatEntersAWallsSlabFromBehindHasAnEntryShareOfOne()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] bar = [new(new(0, 0, -5), new(1, 0, 0), new(0, 10, 0))];
        // The ray passes behind the bar and comes toward the eye. It reaches the back of the bar's slab, 5.5 m away,
        // at the target.
        var origin = new Vector3(-2, 0.3f, -8);
        var target = new Vector3(0.13f, 0.3f, -5.5f);
        Vector2 entering = camera.PixelOf(target);

        RayTrace trace = RayOf(origin, Vector3.Normalize(target - origin), camera)
            .Trace(ScreenTesting.Pyramid(camera, bar), MaxSteps);

        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the ray enters the slab, not {trace}");
        hit.Pixel.Should().Be(((int)MathF.Floor(entering.X), (int)MathF.Floor(entering.Y)));
        hit.EntryShare.Should().Be(1);
    }

    [Fact]
    public void ARayThatEntersASlabHalfwayInHasAnEntryShareOfOneHalf()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] bar = [new(new(0, 0, -5), new(1, 0, 0), new(0, 10, 0))];
        float high = 1 / 5f;
        float low = high / (1 + ScreenRay.ThicknessShare);
        float halfway = 1 / ((high + low) / 2);

        // Parallel to the screen, the ray keeps one depth: halfway between the front and the back of the slab.
        RayTrace trace = RayOf(new(-2, 0.3f, -halfway), Vector3.UnitX, camera)
            .Trace(ScreenTesting.Pyramid(camera, bar), MaxSteps);

        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the ray lies inside the slab, not {trace}");
        hit.EntryShare.Should().BeApproximately(0.5f, 1e-3f);
    }

    [Fact]
    public void ARayThatLeavesTheScreenEndsAtTheScreenEdge()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);

        ScreenRay ray = RayOf(new(-1, 0.2f, -4), Vector3.UnitX, camera);
        RayTrace trace = ray.Trace(sky, MaxSteps);

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
            .Trace(ScreenTesting.Pyramid(camera, scene), MaxSteps);

        Vector3.Distance(point, Vector3.Zero).Should().BeInRange(1.9f, 2.1f);
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the planet covers the vanishing point, not {trace}");
        hit.Pixel.Should().Be(((int)MathF.Floor(vanishingPoint.X), (int)MathF.Floor(vanishingPoint.Y)));
        hit.EntryShare.Should().Be(0);
    }

    [Fact]
    public void EveryFloorPixelWhoseVanishingPointAPlanetCoversHitsThePlanet()
    {
        // The rounding of the ray's inverse depth near its vanishing point is larger than the slab of a surface
        // 10,000 km away, so the trace must not judge the slab by the inverse depth at a solved entry
        // (docs/plans/screen-space-reflections.md, "Precision and determinism").
        CameraView camera = ScreenTesting.Camera(width: 640, height: 360, pitchDown: 0.1f);
        SceneRectangle[] scene =
        [
            new(new(0, -1.5f, -25), new(30, 0, 0), new(0, 0, 25)),
            new(new(0, 0, -1e7f), new(1e8f, 0, 0), new(0, 1e8f, 0)),
        ];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, scene);
        var misses = new List<string>();
        int rays = 0;

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
                .Trace(pyramid, ConeTracedReflections.MaxStepsLimit);
            rays++;
            if (trace is not RayHit { EntryShare: 0 })
                misses.Add($"({x}, {y}): {trace}");
        }

        rays.Should().BeGreaterThan(1000);
        misses.Should().BeEmpty("the planet covers the vanishing point of each of {0} rays", rays);
    }

    [Fact]
    public void TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds()
    {
        CameraView camera = ScreenTesting.Camera(width: 128, height: 96, pitchDown: 0.35f);
        SceneRectangle[] scene =
        [
            new(new(0, -1.5f, -15), new(10, 0, 0), new(0, 0, 15)),
            new(new(0, 1, -30), new(10, 0, 0), new(0, 2.5f, 0)),
            new(new(-10, 0.5f, -15), new(0, 2, 0), new(0, 0, 15)),
            .. ScreenTesting.Box(new(-2, -1, -8), new(0.5f, 0.5f, 0.5f)),
            .. ScreenTesting.Box(new(3, -0.5f, -12), new(1, 1, 1)),
            .. ScreenTesting.Box(new(0.5f, -1.2f, -5), new(0.3f, 0.3f, 0.3f)),
            .. ScreenTesting.Box(new(-1, 1.5f, -14), new(1, 0.3f, 0.6f)),
        ];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, scene);
        var random = new Random(20_261_025);
        int hits = 0;
        var disagreements = new List<string>();

        for (int index = 0; index < 10_000; index++)
        {
            Vector3 viewRay = camera.ViewRayThrough(new(random.NextSingle() * 128, random.NextSingle() * 96));
            double surfaceDepth = ScreenTesting.DepthAlong(viewRay, scene) ?? 40;
            Vector3 origin = viewRay * (float)(surfaceDepth * (0.2 + 0.8 * random.NextDouble()));
            ScreenRay ray = RayOf(origin, RandomDirection(random), camera);

            RayTrace traced = ray.Trace(pyramid, maxSteps: 1_000_000);
            (int X, int Y, float EntryShare)? marched = March(ray, pyramid);

            if (traced.TryGetValue(out RayHit hit))
            {
                hits++;
                bool insideItsPixel =
                    hit.Position.X >= hit.Pixel.X
                    && hit.Position.X <= hit.Pixel.X + 1
                    && hit.Position.Y >= hit.Pixel.Y
                    && hit.Position.Y <= hit.Pixel.Y + 1;
                if (!insideItsPixel)
                    disagreements.Add($"ray {index}: the hit {hit} lies outside its pixel");
            }
            if (!Agrees(traced, marched))
                disagreements.Add($"ray {index}: trace {traced}, march {marched}");
        }

        hits.Should().BeGreaterThan(2_000, "most random rays meet a surface of the scene");
        disagreements.Should().BeEmpty();
    }

    [Fact]
    public void ASlabEntryThatRoundsOntoItsCellsFarEdgeFindsThePixelThatTheMarchFinds()
    {
        CameraView camera = ScreenTesting.Camera(width: 128, height: 96, pitchDown: 0.35f);
        SceneRectangle[] scene =
        [
            new(new(0, -1.5f, -15), new(10, 0, 0), new(0, 0, 15)),
            new(new(0, 1, -30), new(10, 0, 0), new(0, 2.5f, 0)),
            new(new(-10, 0.5f, -15), new(0, 2, 0), new(0, 0, 15)),
            .. ScreenTesting.Box(new(-2, -1, -8), new(0.5f, 0.5f, 0.5f)),
            .. ScreenTesting.Box(new(3, -0.5f, -12), new(1, 1, 1)),
            .. ScreenTesting.Box(new(0.5f, -1.2f, -5), new(0.3f, 0.3f, 0.3f)),
            .. ScreenTesting.Box(new(-1, 1.5f, -14), new(1, 0.3f, 0.6f)),
        ];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, scene);
        // Five of 2,000,000 random rays, as float bits: in each, a descent solves a slab entry inside a cell, and its
        // position rounds onto that cell's far edge. The march counts that point in the cell, and so must the trace.
        int[][] rays =
        [
            [-1107579262, -1083542741, -1081599487, 1023203020, -1089825740, -1084810634],
            [1042386941, -1082180277, -1080927508, -1089588367, -1116458374, -1084992710],
            [1058390290, -1088727974, -1088419092, -1100666412, -1088763477, -1086082921],
            [1057664682, -1088110134, -1080204555, -1095920684, -1083203284, -1111431943],
            [1074026297, -1084203234, -1069695833, -1088311125, 1034606339, -1085975904],
        ];
        var disagreements = new List<string>();

        foreach (int[] bits in rays)
        {
            float[] values = [.. bits.Select(BitConverter.Int32BitsToSingle)];
            ScreenRay ray = RayOf(new(values[0], values[1], values[2]), new(values[3], values[4], values[5]), camera);
            RayTrace traced = ray.Trace(pyramid, MaxSteps);
            (int X, int Y, float EntryShare)? marched = March(ray, pyramid);
            if (!Agrees(traced, marched))
                disagreements.Add($"trace {traced}, march {marched}");
        }

        disagreements.Should().BeEmpty();
    }

    /// <summary>The trace and the march hit one pixel with one entry share, or miss together.</summary>
    private static bool Agrees(RayTrace traced, (int X, int Y, float EntryShare)? marched) =>
        traced switch
        {
            RayHit hit => marched is { } met
                && hit.Pixel == (met.X, met.Y)
                && MathF.Abs(hit.EntryShare - met.EntryShare) < 1e-3f,
            RayLeftTheSegment => marched is null,
            RayOutOfSteps => false,
            null => false,
        };

    private static Vector3 RandomDirection(Random random)
    {
        while (true)
        {
            var candidate = new Vector3(
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1,
                random.NextSingle() * 2 - 1
            );
            float lengthSquared = candidate.LengthSquared();
            if (lengthSquared is > 0.01f and <= 1)
                return candidate / MathF.Sqrt(lengthSquared);
        }
    }

    /// <summary>
    /// The oracle of the hierarchical trace: the slab rule of the trace at every pixel of the segment in order, from
    /// the exit of the origin pixel, with no level above 0. The entry share reads the inverse depth where the image
    /// enters the pixel.
    /// </summary>
    private static (int X, int Y, float EntryShare)? March(ScreenRay ray, DepthPyramid pyramid)
    {
        var travel = new Vector2(MathF.Sign(ray.Step.X), MathF.Sign(ray.Step.Y));
        float s = PixelExit(ray, Floor(ray.Start), travel);
        for (int pixels = 0; s < ray.End; pixels++)
        {
            pixels.Should().BeLessThan(1_000_000, "the march moves on at every pixel");
            Vector2 pixel = Floor(ray.Start + ray.Step * s + travel * ScreenRay.CrossingNudge);
            float exit = PixelExit(ray, pixel, travel);
            (float low, float high) = pyramid.Bounds(0, (int)pixel.X, (int)pixel.Y).Slab(ScreenRay.ThicknessShare);
            float inW = ray.StartInverseDepth + ray.StepInverseDepth * s;
            float outW = ray.StartInverseDepth + ray.StepInverseDepth * exit;
            if (MathF.Max(MathF.Min(inW, outW), low) <= MathF.Min(MathF.Max(inW, outW), high))
            {
                float edgeW = ray.StartInverseDepth + ray.StepInverseDepth * PixelEntry(ray, pixel, travel);
                return ((int)pixel.X, (int)pixel.Y, MarchedEntryShare(edgeW, low, high));
            }
            s = exit;
        }
        return null;
    }

    private static float PixelExit(ScreenRay ray, Vector2 pixel, Vector2 travel)
    {
        Vector2 edge = pixel + Vector2.Max(travel, Vector2.Zero);
        float exitX = travel.X != 0 ? (edge.X - ray.Start.X) / ray.Step.X : float.MaxValue;
        float exitY = travel.Y != 0 ? (edge.Y - ray.Start.Y) / ray.Step.Y : float.MaxValue;
        return MathF.Min(MathF.Min(exitX, exitY), ray.End);
    }

    /// <summary>Where the image enters the pixel: the later of its crossings of the pixel's near edges.</summary>
    private static float PixelEntry(ScreenRay ray, Vector2 pixel, Vector2 travel)
    {
        Vector2 edge = pixel + Vector2.Max(-travel, Vector2.Zero);
        float entryX = travel.X != 0 ? (edge.X - ray.Start.X) / ray.Step.X : float.MinValue;
        float entryY = travel.Y != 0 ? (edge.Y - ray.Start.Y) / ray.Step.Y : float.MinValue;
        return MathF.Max(entryX, entryY);
    }

    private static float MarchedEntryShare(float inW, float low, float high)
    {
        if (inW > high)
            return 0;
        if (inW < low)
            return 1;
        return (high - inW) / (high - low);
    }

    private static Vector2 Floor(Vector2 position) => new(MathF.Floor(position.X), MathF.Floor(position.Y));

    [Fact]
    public void OverEmptySpaceATraceTakesFewerStepsThanThePixelsItCrosses()
    {
        CameraView camera = ScreenTesting.Camera(width: 1_024, height: 16);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);
        Vector3 origin = camera.ViewRayThrough(new(0.5f, 8)) * 5;

        RayTrace trace = RayOf(origin, camera.Right, camera).Trace(sky, MaxSteps);

        trace.TryGetValue(out RayLeftTheSegment left).Should().BeTrue($"the ray crosses the sky, not {trace}");
        left.Steps.Should().BeLessThan(40, "the ray crosses 1,023 px, in cells that grow at each step");
    }

    [Fact]
    public void ATraceThatUsesAllItsStepsMisses()
    {
        CameraView camera = ScreenTesting.Camera(width: 1_024, height: 16);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);
        Vector3 origin = camera.ViewRayThrough(new(0.5f, 8)) * 5;

        RayTrace trace = RayOf(origin, camera.Right, camera).Trace(sky, maxSteps: 3);

        trace.Should().Be(new RayTrace(new RayOutOfSteps(3)));
    }

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

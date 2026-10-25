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

        RayTrace trace = ScreenRay
            .Of(origin, Vector3.Normalize(toWall), camera)
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

        var ray = ScreenRay.Of(origin, Vector3.Normalize(target - origin), camera);
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

        RayTrace trace = ScreenRay
            .Of(new(-3, 0.3f, -8), Vector3.UnitX, camera)
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

        RayTrace trace = ScreenRay
            .Of(origin, Vector3.Normalize(target - origin), camera)
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
        RayTrace trace = ScreenRay
            .Of(new(-2, 0.3f, -halfway), Vector3.UnitX, camera)
            .Trace(ScreenTesting.Pyramid(camera, bar), MaxSteps);

        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the ray lies inside the slab, not {trace}");
        hit.EntryShare.Should().BeApproximately(0.5f, 1e-3f);
    }

    [Fact]
    public void ARayThatLeavesTheScreenEndsAtTheScreenEdge()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);

        var ray = ScreenRay.Of(new(-1, 0.2f, -4), Vector3.UnitX, camera);
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

        var ray = ScreenRay.Of(origin, Vector3.Normalize(target - origin), camera);

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

        var ray = ScreenRay.Of(new(-1, 0, -3), direction, camera);
        Vector2 end = ray.Start + ray.Step * ray.End;

        ray.EndsAt.Should().Be(SegmentEnd.VanishingPoint);
        end.X.Should().BeApproximately(vanishingPoint.X, 0.05f);
        end.Y.Should().BeApproximately(vanishingPoint.Y, 0.05f);
        (ray.StartInverseDepth + ray.StepInverseDepth * ray.End).Should().BeApproximately(0, 1e-6f);
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
        Vector3 point = viewRay * (float)ScreenTesting.DepthAlong(viewRay, scene)!.Value;
        var direction = Vector3.Reflect(Vector3.Normalize(point), FloorNormal);
        Vector2 vanishingPoint = camera.PixelOf(direction);

        RayTrace trace = ScreenRay
            .FromSurface(point: point, geometricNormal: FloorNormal, direction: direction, camera: camera)
            .Trace(ScreenTesting.Pyramid(camera, scene), MaxSteps);

        Vector3.Distance(point, Vector3.Zero).Should().BeInRange(1.9f, 2.1f);
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the planet covers the vanishing point, not {trace}");
        hit.Pixel.Should().Be(((int)MathF.Floor(vanishingPoint.X), (int)MathF.Floor(vanishingPoint.Y)));
        hit.EntryShare.Should().Be(0);
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
        int grazingRays = 0;
        var disagreements = new List<string>();

        for (int index = 0; index < 10_000; index++)
        {
            Vector3 viewRay = camera.ViewRayThrough(new(random.NextSingle() * 128, random.NextSingle() * 96));
            double surfaceDepth = ScreenTesting.DepthAlong(viewRay, scene) ?? 40;
            Vector3 origin = viewRay * (float)(surfaceDepth * (0.2 + 0.8 * random.NextDouble()));
            var ray = ScreenRay.Of(origin, RandomDirection(random), camera);

            RayTrace traced = ray.Trace(pyramid, maxSteps: 1_000_000);
            Marched marched = March(ray, pyramid);

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
            if (marched.Grazed.Count > 0)
                grazingRays++;
            if (!Agrees(traced, marched))
                disagreements.Add($"ray {index}: trace {traced}, march {marched.Met}, grazed {marched.Grazed.Count}");
        }

        hits.Should().BeGreaterThan(2_000, "most random rays meet a surface of the scene");
        disagreements.Should().BeEmpty();
        grazingRays
            .Should()
            .BeLessThan(100, "a ray rarely meets a slab only within the crossing nudge of a pixel's edge");
    }

    /// <summary>
    /// What the march finds: the first pixel whose slab the ray meets before the last crossing nudge of the pixel,
    /// with its entry share, and before it the pixels whose slab the ray meets only within that sliver.
    /// </summary>
    private readonly record struct Marched(List<(int X, int Y)> Grazed, (int X, int Y, float EntryShare)? Met);

    /// <summary>
    /// The trace and the march hit one pixel with one entry share, or miss together. The one exception is a pixel that
    /// the ray meets only within the crossing nudge of the pixel's far edge: a descent can solve the slab entry into
    /// that sliver, and the nudge gives it to the pixel ahead, so the trace can hit the grazed pixel, its neighbor
    /// across the sliver, or the march's next pixel.
    /// </summary>
    private static bool Agrees(RayTrace traced, Marched marched) =>
        traced switch
        {
            RayHit hit when marched.Met is { } met && hit.Pixel == (met.X, met.Y) => MathF.Abs(
                hit.EntryShare - met.EntryShare
            ) < 1e-3f,
            RayHit hit => marched.Grazed.Any(grazed => hit.Pixel == grazed || WithinNudgeOf(hit.Position, grazed)),
            RayLeftTheSegment => marched.Met is null,
            RayOutOfSteps => false,
            null => false,
        };

    private static bool WithinNudgeOf(Vector2 position, (int X, int Y) pixel)
    {
        const float nudge = ScreenRay.CrossingNudge;
        bool across = position.X >= pixel.X - nudge && position.X <= pixel.X + 1 + nudge;
        bool down = position.Y >= pixel.Y - nudge && position.Y <= pixel.Y + 1 + nudge;
        return across && down;
    }

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
    private static Marched March(ScreenRay ray, DepthPyramid pyramid)
    {
        var travel = new Vector2(MathF.Sign(ray.Step.X), MathF.Sign(ray.Step.Y));
        var grazed = new List<(int X, int Y)>();
        float s = PixelExit(ray, Floor(ray.Start), travel);
        for (int pixels = 0; s < ray.End; pixels++)
        {
            pixels.Should().BeLessThan(1_000_000, "the march moves on at every pixel");
            Vector2 pixel = Floor(ray.Start + ray.Step * s + travel * ScreenRay.CrossingNudge);
            float exit = PixelExit(ray, pixel, travel);
            (float low, float high) = pyramid.Bounds(0, (int)pixel.X, (int)pixel.Y).Slab(ScreenRay.ThicknessShare);
            float beforeSliver = MathF.Max(s, MathF.Min(SliverStart(ray, pixel, travel), exit));
            if (MeetsSlab(ray: ray, from: s, to: beforeSliver, low: low, high: high))
            {
                float edgeW = ray.StartInverseDepth + ray.StepInverseDepth * PixelEntry(ray, pixel, travel);
                return new(grazed, ((int)pixel.X, (int)pixel.Y, MarchedEntryShare(edgeW, low, high)));
            }
            if (MeetsSlab(ray: ray, from: s, to: exit, low: low, high: high))
                grazed.Add(((int)pixel.X, (int)pixel.Y));
            s = exit;
        }
        return new(grazed, null);
    }

    private static bool MeetsSlab(ScreenRay ray, float from, float to, float low, float high)
    {
        float inW = ray.StartInverseDepth + ray.StepInverseDepth * from;
        float outW = ray.StartInverseDepth + ray.StepInverseDepth * to;
        return MathF.Max(MathF.Min(inW, outW), low) <= MathF.Min(MathF.Max(inW, outW), high);
    }

    /// <summary>Where the image comes within the crossing nudge of the pixel's far edge across or down.</summary>
    private static float SliverStart(ScreenRay ray, Vector2 pixel, Vector2 travel)
    {
        Vector2 edge = pixel + Vector2.Max(travel, Vector2.Zero) - travel * ScreenRay.CrossingNudge;
        float startX = travel.X != 0 ? (edge.X - ray.Start.X) / ray.Step.X : float.MaxValue;
        float startY = travel.Y != 0 ? (edge.Y - ray.Start.Y) / ray.Step.Y : float.MaxValue;
        return MathF.Min(startX, startY);
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

        RayTrace trace = ScreenRay.Of(origin, camera.Right, camera).Trace(sky, MaxSteps);

        trace.TryGetValue(out RayLeftTheSegment left).Should().BeTrue($"the ray crosses the sky, not {trace}");
        left.Steps.Should().BeLessThan(40, "the ray crosses 1,023 px, in cells that grow at each step");
    }

    [Fact]
    public void ATraceThatUsesAllItsStepsMisses()
    {
        CameraView camera = ScreenTesting.Camera(width: 1_024, height: 16);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);
        Vector3 origin = camera.ViewRayThrough(new(0.5f, 8)) * 5;

        RayTrace trace = ScreenRay.Of(origin, camera.Right, camera).Trace(sky, maxSteps: 3);

        trace.Should().Be(new RayTrace(new RayOutOfSteps(3)));
    }

    [Fact]
    public void AFlatSurfacesOriginLiesTwoPixelFootprintsAboveIt()
    {
        CameraView camera = ScreenTesting.Camera(width: 64, height: 48, pitchDown: 0.5f);
        SceneRectangle[] floor = [new(new(0, -1.5f, 0), new(100, 0, 0), new(0, 0, 100))];
        Vector3 viewRay = camera.ViewRayThrough(new(40.5f, 40.5f));
        Vector3 point = viewRay * (float)ScreenTesting.DepthAlong(viewRay, floor)!.Value;
        float footprint = Vector3.Dot(point, camera.Forward) * camera.PixelAngle;

        var ray = ScreenRay.FromSurface(
            point: point,
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

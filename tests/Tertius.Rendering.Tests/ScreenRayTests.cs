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
    public void ARayThatComesDownOntoAFloorSeenAtAGrazingAngleHitsItsFrontWhereItCrossesIt()
    {
        // The eye stands 0.15 m above the floor, so the floor's pixels each span a long stretch of depth, and the rays
        // come down onto it from points 0.3 to 0.9 m above it, 4 to 8 m ahead, at 3° to 30° below the horizon.
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        const float floorHeight = -0.15f;
        SceneRectangle[] floor = [new(new(0, floorHeight, 0), new(1_000, 0, 0), new(0, 0, 1_000))];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, floor);
        var wrong = new List<string>();
        int hits = 0;

        foreach (float x in new[] { -1f, -0.3f, 0.4f, 1.1f })
        foreach (float height in new[] { 0.3f, 0.6f, 0.9f })
        foreach (float z in new[] { -4f, -5f, -6f, -7f, -8f })
        foreach (float down in new[] { 3f, 8f, 15f, 30f })
        foreach (float around in new[] { -60f, -25f, 0f, 25f, 60f })
        {
            var origin = new Vector3(x, floorHeight + height, z);
            float pitch = -down * MathF.PI / 180;
            float yaw = around * MathF.PI / 180;
            // Toward the eye's side of the scene, where the floor lies on the screen below the origin.
            var direction = new Vector3(
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Cos(yaw)
            );
            Vector3 crossing = origin + direction * ((floorHeight - origin.Y) / direction.Y);
            if (crossing.Z > -0.5f || ray(origin, direction).Trace(pyramid, MaxSteps) is not RayHit hit)
                continue;
            hits++;
            Vector2 expected = camera.PixelOf(crossing);
            if (hit.EntryShare > 1e-3f || Vector2.Distance(hit.Position, expected) > 1)
                wrong.Add($"from {origin} along {direction}: {hit}, the floor crossed at {expected}");
        }

        hits.Should().BeGreaterThan(400, "most rays cross the floor on the screen");
        wrong
            .Should()
            .BeEmpty(
                "a ray that comes down onto a floor meets the front of its plane, where it crosses it, with no entry share"
            );

        ScreenRay ray(Vector3 origin, Vector3 direction) => RayOf(origin, direction, camera);
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
    public void ARayThatEntersTheSlabOfATurnedWallFromBehindHitsWhereItCrossesTheSlabsBack()
    {
        // The wall turns away from the eye toward the right, so a coarse cell's farthest depth lies past the depth of the
        // pixel where the ray enters: the walk comes down to that pixel behind its slab, and the ray crosses the slab's
        // back inside the pixel, 0.6 px past the pixel's edge where the image enters it. The back is the wall's plane
        // scaled by 1 + κ about the eye.
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] wall = [new(new(0, 0, -6), new(4, 0, -2), new(0, 10, 0))];
        Vector3 back = new Vector3(0.96f, 0.3f, -6.48f) * (1 + CellBounds.ThicknessShare);
        var origin = new Vector3(3, back.Y, -12);

        RayTrace trace = RayOf(origin, Vector3.Normalize(back - origin), camera)
            .Trace(ScreenTesting.Pyramid(camera, wall), MaxSteps);

        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the ray enters the slab, not {trace}");
        hit.EntryShare.Should().Be(1, "the ray comes from behind the wall");
        Vector2 crossing = camera.PixelOf(back);
        hit.Position.X.Should().BeApproximately(crossing.X, 1e-2f, "the ray enters the slab where it crosses its back");
        hit.Position.Y.Should().BeApproximately(crossing.Y, 1e-2f);
    }

    [Fact]
    public void ARayThatEntersASlabHalfwayInHasAnEntryShareOfOneHalf()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] bar = [new(new(0, 0, -5), new(1, 0, 0), new(0, 10, 0))];
        float high = 1 / 5f;
        float low = high / (1 + CellBounds.ThicknessShare);
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
    public void ASlabEntryThatRoundsOntoItsCellsFarEdgeFindsThePixelThatTheMarchFinds()
    {
        (CameraView camera, DepthPyramid pyramid) = BoxesInARoom();
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

    [Theory]
    [InlineData(
        "down a wall past the center line of a pixel at its foot",
        68,
        34,
        1039106928,
        -1081158339,
        -1069663375,
        1033011138,
        1039679252,
        -1082297440
    )]
    [InlineData(
        "up the far wall from the floor past a box's lid",
        68,
        34,
        1067501317,
        -1078055479,
        -1078357746,
        -1097718059,
        1043023646,
        -1083075256
    )]
    [InlineData(
        "behind the side wall into a pixel at its top edge",
        108,
        25,
        1098735909,
        1073871882,
        -1042550488,
        -1089682477,
        -1095925616,
        1061384136
    )]
    public void ARayThatADescentBringsInsideAPixelWhoseSurfaceBendsFindsThePixelThatTheMarchFinds(
        string path,
        int bentX,
        int bentY,
        int originX,
        int originY,
        int originZ,
        int directionX,
        int directionY,
        int directionZ
    )
    {
        // Three of 2,000,000 random rays, as float bits. In each, a descent solves a slab entry inside the pixel
        // (bentX, bentY), whose surface stops at a center line and bends there, past the bend, where the ray meets the
        // slab. The march tests the pixel from its entry edge, where the ray misses it, so the trace must test it from
        // there too: the answer must not depend on how the walk reached the pixel.
        (CameraView camera, DepthPyramid pyramid) = BoxesInARoom();
        ScreenRay ray = RayOfBits(camera, [originX, originY, originZ, directionX, directionY, directionZ]);
        // The premise, which the rule for where a pixel's surface holds decides: if it changes, find new rays.
        PixelSurface bent = pyramid.Surface(bentX, bentY)!.Value;
        (bent.Low != new Vector2(bentX, bentY) || bent.High != new Vector2(bentX + 1, bentY + 1))
            .Should()
            .BeTrue($"the surface of ({bentX}, {bentY}) stops at a center line, not {bent}");
        ray.MeetPixel(pyramid: pyramid, pixel: new(bentX, bentY))
            .Should()
            .BeNull($"the ray {path} misses ({bentX}, {bentY}) from its entry edge");

        RayTrace traced = ray.Trace(pyramid, MaxSteps);
        (int X, int Y, float EntryShare)? marched = March(ray, pyramid);

        Agrees(traced, marched)
            .Should()
            .BeTrue($"the ray {path} has one answer, not the trace's {traced} and the march's {marched}");
    }

    [Theory]
    [InlineData(
        "toward the left edge, past the corner of a pixel of the side wall",
        -1054883520,
        1070202566,
        -1050215002,
        -1101102170,
        1064257883,
        -1097859609
    )]
    [InlineData(
        "toward the right edge, past the corner of a floor pixel",
        1071094427,
        -1078035163,
        -1075312343,
        1059093916,
        -1089261262,
        -1090087463
    )]
    [InlineData(
        "along the floor, past the corner of a pixel of a box's side",
        -1061499650,
        -1078080019,
        -1047942772,
        1051612254,
        -1095328627,
        1063171790
    )]
    public void ARayThatCrossesAPixelOnlyNearItsCornerFindsThePixelThatTheMarchFinds(
        string path,
        int originX,
        int originY,
        int originZ,
        int directionX,
        int directionY,
        int directionZ
    )
    {
        // Three of 2,000,000 random rays, as float bits. In each, the image crosses a pixel for less than the crossing
        // nudge near its corner, and meets the pixel's slab there.
        (CameraView camera, DepthPyramid pyramid) = BoxesInARoom();
        ScreenRay ray = RayOfBits(camera, [originX, originY, originZ, directionX, directionY, directionZ]);

        RayTrace traced = ray.Trace(pyramid, MaxSteps);
        (int X, int Y, float EntryShare)? marched = March(ray, pyramid);

        Agrees(traced, marched)
            .Should()
            .BeTrue($"the ray {path} has one answer, not the trace's {traced} and the march's {marched}");
    }

    [Fact]
    public void ATraceMeetsAPixelThatTheImageCrossesOnlyNearItsCorner()
    {
        // The image leaves pixel (10, 9) across x = 10 at y = 9.996, so it crosses pixel (9, 9) for 0.04 px across and
        // 0.004 px down before it enters (9, 10). Only pixel (9, 9) sees a surface, at the ray's own inverse depth.
        float[] inverseDepths = new float[16 * 16];
        inverseDepths[9 * 16 + 9] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(12.5f, 9.746f),
            Step: new(-10, 1),
            StartInverseDepth: 0.5f,
            StepInverseDepth: 0,
            End: 1.2f,
            EndsAt: SegmentEnd.ScreenEdge
        );

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the image crosses the pixel with the surface, not {trace}");
        hit.Pixel.Should().Be((9, 9));
    }

    [Fact]
    public void ACoarseCrossingWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters()
    {
        // The image leaves the level-1 cell (3, 4) across x = 8 at s = 0.23438850, and crosses y = 9 at s = 0.23438855,
        // so the march enters pixel (8, 8) for 5e-7 px. The position at the crossing, 8.483798 + 2.2023344 · s, rounds
        // to y = 9. Only pixel (8, 8) sees a surface, at the ray's own inverse depth.
        float[] inverseDepths = new float[16 * 16];
        inverseDepths[8 * 16 + 8] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(5.656115f, 8.483798f),
            Step: new(10, 2.2023344f),
            StartInverseDepth: 0.5f,
            StepInverseDepth: 0,
            End: 0.6f,
            EndsAt: SegmentEnd.ScreenEdge
        );

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        March(ray, pyramid).Should().Be((8, 8, 0f), "the march enters the pixel with the surface");
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the walk stands in the march's pixel, not {trace}");
        hit.Pixel.Should().Be((8, 8));
    }

    [Fact]
    public void ASlabEntryWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters()
    {
        // The image of ACoarseCrossingWithinARoundingOfAPixelsCornerStandsInThePixelThatTheMarchEnters, whose inverse
        // depth now falls from in front of the surface of pixel (8, 8) and reaches it at s = 0.23438853: after the
        // image enters the pixel across x = 8 at s = 0.23438850, and before it leaves it across y = 9 at
        // s = 0.23438855. The walk crosses into the level-2 cell of the pixel there and descends to that slab entry,
        // whose position rounds to y = 9. Only pixel (8, 8) sees a surface.
        float[] inverseDepths = new float[16 * 16];
        inverseDepths[8 * 16 + 8] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(5.656115f, 8.483798f),
            Step: new(10, 2.2023344f),
            StartInverseDepth: 23.938852f,
            StepInverseDepth: -100,
            End: 0.239f,
            EndsAt: SegmentEnd.VanishingPoint
        );

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        March(ray, pyramid).Should().Be((8, 8, 0f), "the ray reaches the surface inside the pixel's sliver");
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the walk descends into the march's pixel, not {trace}");
        hit.Pixel.Should().Be((8, 8));
    }

    [Fact]
    public void ARayThroughPixelCornersStepsDiagonallyAndNeverStandsInAPixelThatItTouchesOnlyAtACorner()
    {
        // At 45° from (2.25, 3.25), the image leaves each pixel (k, k + 1) exactly through its far corner, where both
        // edge parameters are the same binary fraction. The pixels (k + 1, k + 1) and (k, k + 2) beside the diagonal
        // touch the image only at those corners, and each sees a surface at the ray's own inverse depth, which a
        // stretch of no length still meets. Of the diagonal pixels, only (10, 11) sees a surface.
        float[] inverseDepths = new float[16 * 16];
        for (int k = 2; k < 14; k++)
        {
            inverseDepths[(k + 1) * 16 + k + 1] = 0.5f;
            inverseDepths[(k + 2) * 16 + k] = 0.5f;
        }
        inverseDepths[11 * 16 + 10] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(2.25f, 3.25f),
            Step: new(8, 8),
            StartInverseDepth: 0.5f,
            StepInverseDepth: 0,
            End: 1.5f,
            EndsAt: SegmentEnd.ScreenEdge
        );

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        March(ray, pyramid).Should().Be((10, 11, 0f), "the march leaves a pixel across both edges at its corner");
        trace.TryGetValue(out RayHit hit).Should().BeTrue($"the image meets the diagonal's surface, not {trace}");
        hit.Pixel.Should().Be((10, 11), "a crossing through a corner moves the walk on both axes at once");
    }

    [Fact]
    public void ASlabEntryOnItsCellsExitStaysInTheCellAndMeetsThePixelThatTheImageLeavesThere()
    {
        // The ray's inverse depth falls to the surface of pixel (7, 4) exactly where the image leaves the level-3 cell
        // of pixels 0 to 7, at x = 8 and s = 0.75, so the march meets the pixel at the end of its stretch. The descent
        // in that cell solves its slab entry on the cell's exit, where the march's pixel is (8, 4), beyond the cell.
        // Only pixel (7, 4) sees a surface.
        float[] inverseDepths = new float[16 * 16];
        inverseDepths[4 * 16 + 7] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(0.5f, 4.5f),
            Step: new(10, 0),
            StartInverseDepth: 1.25f,
            StepInverseDepth: -1,
            End: 1.5f,
            EndsAt: SegmentEnd.ScreenEdge
        );

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        March(ray, pyramid).Should().Be((7, 4, 0f), "the ray reaches the pixel's surface where the image leaves it");
        trace
            .TryGetValue(out RayHit hit)
            .Should()
            .BeTrue($"the walk stays in the cell that it descends in, not {trace}");
        hit.Pixel.Should().Be((7, 4));
    }

    [Theory]
    [InlineData("across to the right", -7.5f, 4.5f, 8, 0, 3, 4)]
    [InlineData("across to the left", 23.5f, 4.5f, -8, 0, 12, 4)]
    [InlineData("down", 4.5f, -7.5f, 0, 8, 4, 3)]
    [InlineData("up", 4.5f, 23.5f, 0, -8, 4, 12)]
    [InlineData("down and to the right", -7.5f, -7.5f, 8, 8, 3, 3)]
    [InlineData("up and to the left", 23.5f, 23.5f, -8, -8, 12, 12)]
    [InlineData("up and to the right", -7.5f, 23.5f, 8, -8, 3, 12)]
    [InlineData("down and to the left", 23.5f, -7.5f, -8, 8, 12, 3)]
    public void ASlabEntryOnAPixelsEdgeInsideACellMeetsThePixelThatTheImageLeavesThere(
        string path,
        float startX,
        float startY,
        float stepX,
        float stepY,
        int surfaceX,
        int surfaceY
    )
    {
        // The walk climbs over the empty cells beyond the screen and crosses onto it at s = 0.9375, into the level-3
        // cell that holds the surface's pixel. There it descends to the slab entry: the ray's inverse depth falls
        // to the surface's 0.5 at s = 1.4375, where the image leaves the surface's pixel across an edge, or through a
        // corner on a diagonal. That edge lies inside the level-3 cell and bounds the level-2 and level-1 cells of the
        // surface's pixel, so the march meets the pixel at the end of its stretch, and the walk meets stretches of no
        // length in those finer cells. Every number is a binary fraction, so the tie is exact.
        float[] inverseDepths = new float[16 * 16];
        inverseDepths[surfaceY * 16 + surfaceX] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(startX, startY),
            Step: new(stepX, stepY),
            StartInverseDepth: 0.859375f,
            StepInverseDepth: -0.25f,
            End: 2.9f,
            EndsAt: SegmentEnd.ScreenEdge
        );

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        March(ray, pyramid).Should().Be((surfaceX, surfaceY, 0f), "the ray reaches the surface where the image leaves");
        trace
            .TryGetValue(out RayHit hit)
            .Should()
            .BeTrue($"the walk {path} descends into the pixel that the image leaves at the entry, not {trace}");
        hit.Pixel.Should().Be((surfaceX, surfaceY));
    }

    [Theory]
    [InlineData("across and up", 0.5f, 5.9375f, 8, -1, 8, 4, 8, 5)]
    [InlineData("across and down", 0.5f, 4.0625f, 8, 1, 8, 5, 8, 4)]
    [InlineData("down and to the left", 5.9375f, 0.5f, -1, 8, 4, 8, 5, 8)]
    [InlineData("down and to the right", 4.0625f, 0.5f, 1, 8, 5, 8, 4, 8)]
    public void ACoarseCrossingThroughAPixelCornerOnTheCellsEdgeEntersThePixelThatTheMarchEnters(
        string path,
        float startX,
        float startY,
        float stepX,
        float stepY,
        int enteredX,
        int enteredY,
        int touchedX,
        int touchedY
    )
    {
        // The walk climbs over empty cells and leaves the level-2 cell of pixels 4 to 7 across the line 8 at
        // s = 0.9375. There the image passes exactly through a pixel corner inside the cell's edge, so the march leaves
        // the cell diagonally into the entered pixel, and the touched pixel beside it meets the image only at that
        // corner. Both see a surface at the ray's own inverse depth, which a stretch of no length still meets.
        float[] inverseDepths = new float[16 * 16];
        inverseDepths[enteredY * 16 + enteredX] = 0.5f;
        inverseDepths[touchedY * 16 + touchedX] = 0.5f;
        var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
        var ray = new ScreenRay(
            Start: new(startX, startY),
            Step: new(stepX, stepY),
            StartInverseDepth: 0.5f,
            StepInverseDepth: 0,
            End: 1.9f,
            EndsAt: SegmentEnd.ScreenEdge
        );

        // The premise that tells the two pixels apart: a walk that stood in the touched pixel would hit it there.
        ray.MeetPixel(pyramid: pyramid, pixel: new(touchedX, touchedY))
            .Should()
            .NotBeNull($"the image {path} meets ({touchedX}, {touchedY}) at its corner, a stretch of no length");

        RayTrace trace = ray.Trace(pyramid, MaxSteps);

        March(ray, pyramid).Should().Be((enteredX, enteredY, 0f), "the march leaves a pixel through its corner");
        trace
            .TryGetValue(out RayHit hit)
            .Should()
            .BeTrue($"the image {path} meets the entered pixel's surface, not {trace}");
        hit.Pixel.Should().Be((enteredX, enteredY), "a crossing stands in the pixel that the image enters there");
    }

    /// <summary>The scene of the random rays: a floor, a far wall, a side wall and four boxes.</summary>
    private static readonly SceneRectangle[] RoomScene =
    [
        new(new(0, -1.5f, -15), new(10, 0, 0), new(0, 0, 15)),
        new(new(0, 1, -30), new(10, 0, 0), new(0, 2.5f, 0)),
        new(new(-10, 0.5f, -15), new(0, 2, 0), new(0, 0, 15)),
        .. ScreenTesting.Box(new(-2, -1, -8), new(0.5f, 0.5f, 0.5f)),
        .. ScreenTesting.Box(new(3, -0.5f, -12), new(1, 1, 1)),
        .. ScreenTesting.Box(new(0.5f, -1.2f, -5), new(0.3f, 0.3f, 0.3f)),
        .. ScreenTesting.Box(new(-1, 1.5f, -14), new(1, 0.3f, 0.6f)),
    ];

    /// <summary>The camera of the random rays, above the floor of <see cref="RoomScene"/> and pitched down, and the
    /// pyramid of what it sees.</summary>
    private static (CameraView Camera, DepthPyramid Pyramid) BoxesInARoom()
    {
        CameraView camera = ScreenTesting.Camera(width: 128, height: 96, pitchDown: 0.35f);
        return (camera, ScreenTesting.Pyramid(camera, RoomScene));
    }

    /// <summary>The image of a ray from a bare origin, given as the float bits of its origin and its direction.</summary>
    private static ScreenRay RayOfBits(in CameraView camera, int[] bits)
    {
        float[] values = [.. bits.Select(BitConverter.Int32BitsToSingle)];
        return RayOf(new(values[0], values[1], values[2]), new(values[3], values[4], values[5]), camera);
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
    /// The oracle of the hierarchical trace: the trace's level-0 test at every pixel that the image crosses, in order,
    /// from the exit of the origin pixel, with no level above 0. Each step leaves a pixel across the edges through which
    /// the image leaves it, so no pixel that the image crosses is skipped, however near its corner.
    /// </summary>
    private static (int X, int Y, float EntryShare)? March(ScreenRay ray, DepthPyramid pyramid)
    {
        var travel = new Vector2(MathF.Sign(ray.Step.X), MathF.Sign(ray.Step.Y));
        Vector2 pixel = Floor(ray.Start);
        (float s, Vector2 crossed) = PixelExit(ray, pixel, travel);
        // Each step moves the pixel on along the image, so the march reaches the segment's end.
        while (s < ray.End)
        {
            pixel += travel * crossed;
            (float exit, Vector2 crossedAtExit) = PixelExit(ray, pixel, travel);
            if (ray.MeetPixel(pyramid: pyramid, pixel: pixel) is { } met)
                return ((int)pixel.X, (int)pixel.Y, met.EntryShare);
            s = exit;
            crossed = crossedAtExit;
        }
        return null;
    }

    /// <summary>Where the image leaves a pixel, and 1 on each axis whose far edge it leaves through there.</summary>
    private static (float Exit, Vector2 Crossed) PixelExit(ScreenRay ray, Vector2 pixel, Vector2 travel)
    {
        Vector2 edge = pixel + Vector2.Max(travel, Vector2.Zero);
        float exitX = travel.X != 0 ? (edge.X - ray.Start.X) / ray.Step.X : float.MaxValue;
        float exitY = travel.Y != 0 ? (edge.Y - ray.Start.Y) / ray.Step.Y : float.MaxValue;
        float exit = MathF.Min(exitX, exitY);
        return (MathF.Min(exit, ray.End), new(exitX <= exit ? 1 : 0, exitY <= exit ? 1 : 0));
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

    /// <summary>The trace over many random rays, against the march and against its hit pixel alone (tests/STYLE.md,
    /// "Long tests").</summary>
    public sealed class RandomRays
    {
        [Fact]
        public void TheHierarchicalTraceFindsThePixelThatAPixelByPixelMarchFinds()
        {
            (CameraView camera, DepthPyramid pyramid) = BoxesInARoom();
            var random = new Random(20_261_025);
            int hits = 0;
            var disagreements = new List<string>();

            for (int index = 0; index < 1_000_000; index++)
            {
                Vector3 viewRay = camera.ViewRayThrough(new(random.NextSingle() * 128, random.NextSingle() * 96));
                double surfaceDepth = ScreenTesting.DepthAlong(viewRay, RoomScene) ?? 40;
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

            hits.Should().BeGreaterThan(200_000, "most random rays meet a surface of the scene");
            disagreements.Should().BeEmpty();
        }

        [Fact]
        public void TheTracesHitIsWhatItsPixelAloneGivesHoweverTheWalkReachedThePixel()
        {
            // The level-0 test takes only the pixel, so the trace's hit, its position as well as its entry share, is
            // what the pixel gives any caller: the descents and crossings that brought the walk there leave no mark.
            (CameraView camera, DepthPyramid pyramid) = BoxesInARoom();
            var random = new Random(20_261_102);
            int hits = 0;
            var different = new List<string>();

            for (int index = 0; index < 100_000; index++)
            {
                Vector3 viewRay = camera.ViewRayThrough(new(random.NextSingle() * 128, random.NextSingle() * 96));
                double surfaceDepth = ScreenTesting.DepthAlong(viewRay, RoomScene) ?? 40;
                Vector3 origin = viewRay * (float)(surfaceDepth * (0.2 + 0.8 * random.NextDouble()));
                ScreenRay ray = RayOf(origin, RandomDirection(random), camera);

                if (ray.Trace(pyramid, maxSteps: 1_000_000) is not RayHit hit)
                    continue;
                hits++;
                (Vector2 Position, float EntryShare)? alone = ray.MeetPixel(
                    pyramid: pyramid,
                    pixel: new(hit.Pixel.X, hit.Pixel.Y)
                );
                if (alone != (hit.Position, hit.EntryShare))
                    different.Add($"ray {index}: the trace's {hit}, the pixel alone {alone}");
            }

            hits.Should().BeGreaterThan(20_000, "most random rays meet a surface of the scene");
            different.Should().BeEmpty();
        }

        [Fact]
        public void OnRaysOfBinaryFractionsWhoseEntriesAndCrossingsTieWithPixelEdgesTheTraceFindsThePixelThatTheMarchFinds()
        {
            // Every number is a binary fraction of few bits: starts on quarter pixels, steps of powers of two pixels,
            // and inverse depths on sixteenths, as are the surfaces facing the eye. So the image crosses pixel corners,
            // and slab entries fall exactly on pixel edges and corners, inside cells and on their edges, which random
            // rays of a camera never do (ticket ter-pywg). There a descent stands in the pixel that the image leaves
            // and a crossing in the pixel that it enters, as the march's pixels do. The segment's end lies 1/1024 off
            // the grid, because a slab entry exactly at the end is the open tie of ticket ter-16vv.
            var random = new Random(20_261_102);
            float[] steps = [-8, -4, -2, -1, 0, 1, 2, 4, 8];
            int hits = 0;
            var disagreements = new List<string>();

            for (int index = 0; index < 200_000; index++)
            {
                float[] inverseDepths = new float[16 * 16];
                for (int pixel = 0; pixel < inverseDepths.Length; pixel++)
                    inverseDepths[pixel] = random.Next(4) == 0 ? random.Next(8, 25) / 16f : 0;
                var pyramid = DepthPyramid.FromInverseDepths(inverseDepths, width: 16, height: 16);
                var step = new Vector2(steps[random.Next(steps.Length)], steps[random.Next(steps.Length)]);
                if (step == Vector2.Zero)
                    continue;
                var ray = new ScreenRay(
                    Start: new(random.Next(-16, 80) / 4f, random.Next(-16, 80) / 4f),
                    Step: step,
                    StartInverseDepth: random.Next(8, 25) / 16f,
                    StepInverseDepth: random.Next(-4, 5) / 16f,
                    End: random.Next(1, 17) / 4f + 1 / 1024f,
                    EndsAt: SegmentEnd.ScreenEdge
                );

                // A walk over the 16 x 16 grid takes far fewer steps, so a walk that stalls at a tie runs out of them.
                RayTrace traced = ray.Trace(pyramid, maxSteps: 1_000);
                (int X, int Y, float EntryShare)? marched = March(ray, pyramid);

                if (marched is not null)
                    hits++;
                if (!Agrees(traced, marched))
                    disagreements.Add($"ray {index} {ray}: trace {traced}, march {marched}");
            }

            hits.Should().BeGreaterThan(10_000, "many rays meet a surface on the grid");
            disagreements.Should().BeEmpty();
        }
    }
}

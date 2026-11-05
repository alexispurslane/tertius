using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The C# copy of the two-depth walk, <see cref="ScreenRay.Walk"/> (docs/plans/temporal-renderer.md, section 6, "The
/// traversal"): its hits against a march over the flat slabs of the pyramid's finest level, its facing test, its cap and
/// its steps. The scenes are synthetic: rectangles seen by a camera built by hand.
/// </summary>
public sealed class ScreenWalkTests
{
    private const int Width = 160;
    private const int Height = 120;
    private const int Cap = 4_096;

    [Fact]
    public void AWalkHitsTheFrontOfAWallInThePixelWhereTheMarchMeetsItsFlatSlab()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, wall);
        Vector3 origin = new(-2, -1, -4);
        var direction = Vector3.Normalize(new Vector3(3, 1.5f, -10) - origin);
        ScreenRay ray = RayOf(origin, direction, camera);

        RayTrace walked = ray.Walk(pyramid: pyramid, direction: direction, maxSteps: Cap);

        walked.TryGetValue(out RayHit hit).Should().BeTrue($"the ray runs into the wall, not {walked}");
        (hit.Pixel.X, hit.Pixel.Y).Should().Be(FlatSlabMarch(ray, pyramid, direction)!.Value.Pixel);
    }

    [Fact]
    public void ARayThatPassesBehindAWallsFlatSlabMissesIt()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // A bar 5 m away, whose slab reaches 5.5 m, and a ray that crosses behind it 8 m away.
        SceneRectangle[] bar = [new(new(0, 0, -5), new(0.5f, 0, 0), new(0, 10, 0))];

        RayTrace walked = RayOf(new(-3, 0.3f, -8), Vector3.UnitX, camera)
            .Walk(pyramid: ScreenTesting.Pyramid(camera, bar), direction: Vector3.UnitX, maxSteps: Cap);

        walked.TryGetValue(out RayLeftTheSegment left).Should().BeTrue($"the ray passes behind the bar, not {walked}");
        left.End.Should().Be(SegmentEnd.ScreenEdge);
    }

    [Fact]
    public void ARayThatRisesTowardTheEyeThroughTheSlabBehindAWallIsNoHitBecauseTheWallFacesAwayFromIt()
    {
        // The wall's normal points to the eye. A ray from 10.5 m, inside the wall's slab of 10 to 11 m, toward the eye
        // meets that slab at once, but the wall faces away from the ray, so the walk crosses its pixels.
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        SceneRectangle[] wall = [new(new(0, 0, -10), new(20, 0, 0), new(0, 20, 0))];
        var direction = Vector3.Normalize(new Vector3(0.4f, 0.1f, 1));

        RayTrace walked = RayOf(new(-1, 0.2f, -10.5f), direction, camera)
            .Walk(pyramid: ScreenTesting.Pyramid(camera, wall), direction: direction, maxSteps: Cap);

        walked
            .TryGetValue(out RayHit _)
            .Should()
            .BeFalse($"the wall faces away from the ray, but the walk gave {walked}");
    }

    [Fact]
    public void AWalkThatReachesItsCapIsAMiss()
    {
        CameraView camera = ScreenTesting.Camera(width: 1_024, height: 16);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);
        Vector3 origin = camera.ViewRayThrough(new(0.5f, 8)) * 5;

        RayTrace walked = RayOf(origin, camera.Right, camera).Walk(pyramid: sky, direction: camera.Right, maxSteps: 3);

        walked.Should().Be(new RayTrace(new RayOutOfSteps(3)));
    }

    [Fact]
    public void OverEmptySpaceAWalkTakesFewerStepsThanThePixelsItCrosses()
    {
        CameraView camera = ScreenTesting.Camera(width: 1_024, height: 16);
        DepthPyramid sky = ScreenTesting.Pyramid(camera, []);
        Vector3 origin = camera.ViewRayThrough(new(0.5f, 8)) * 5;

        RayTrace walked = RayOf(origin, camera.Right, camera)
            .Walk(pyramid: sky, direction: camera.Right, maxSteps: Cap);

        walked.TryGetValue(out RayLeftTheSegment left).Should().BeTrue($"the ray crosses the sky, not {walked}");
        left.Steps.Should().BeLessThan(40, "the ray crosses 1,023 px, in cells that grow at each step");
    }

    [Fact]
    public void BehindABlockAWalkTakesFewerStepsThanTheMarchTestsPixels()
    {
        // A block 4 m away fills the middle of the screen, and the ray crosses the screen 12 m away, far behind its
        // slab. The march tests every pixel behind the block; the walk crosses whole cells behind every slab in them.
        CameraView camera = ScreenTesting.Camera(width: 512, height: 64);
        SceneRectangle[] block = [new(new(0, 0, -4), new(1.5f, 0, 0), new(0, 2, 0))];
        DepthPyramid pyramid = ScreenTesting.Pyramid(camera, block);
        Vector3 origin = camera.ViewRayThrough(new(4.5f, 32)) * 12;
        ScreenRay ray = RayOf(origin, Vector3.UnitX, camera);

        RayTrace walked = ray.Walk(pyramid: pyramid, direction: Vector3.UnitX, maxSteps: Cap);

        walked
            .TryGetValue(out RayLeftTheSegment left)
            .Should()
            .BeTrue($"the ray passes behind the block, not {walked}");
        int marched = MarchedPixels(ray);
        marched.Should().BeGreaterThan(400, "the premise: the ray's image crosses most of the screen");
        left.Steps.Should().BeLessThan(marched / 4, "a cell behind every slab in it costs the walk one step");
    }

    /// <summary>The walk over many random rays, against the march over the flat slabs (tests/STYLE.md, "Long tests").
    /// </summary>
    public sealed class RandomRays(ITestOutputHelper output)
    {
        [Fact]
        public void AtACapOf4096TheTwoDepthWalkFindsTheHitOfAMarchOverTheFlatSlabsOnAMillionRandomRays()
        {
            CameraView camera = ScreenTesting.Camera(width: 128, height: 96, pitchDown: 0.35f);
            DepthPyramid pyramid = ScreenTesting.Pyramid(camera, RoomScene);
            var random = new Random(20_261_104);
            int hits = 0;
            long portSteps = 0,
                spikeSteps = 0;
            var disagreements = new List<string>();

            for (int index = 0; index < 1_000_000; index++)
            {
                Vector3 viewRay = camera.ViewRayThrough(new(random.NextSingle() * 128, random.NextSingle() * 96));
                double surfaceDepth = ScreenTesting.DepthAlong(viewRay, RoomScene) ?? 40;
                Vector3 origin = viewRay * (float)(surfaceDepth * (0.2 + 0.8 * random.NextDouble()));
                Vector3 direction = RandomDirection(random);
                ScreenRay ray = RayOf(origin, direction, camera);

                RayTrace walked = ray.Walk(pyramid: pyramid, direction: direction, maxSteps: Cap);
                if (walked.TryGetValue(out RayHit found))
                    portSteps += found.Steps;
                else if (walked.TryGetValue(out RayLeftTheSegment left))
                    portSteps += left.Steps;
                else
                    portSteps += Cap;
                spikeSteps += NudgedV6Steps(ray, pyramid, direction);
                (int X, int Y)? marched = FlatSlabMarch(ray, pyramid, direction)?.Pixel;

                (int X, int Y)? walkedPixel = walked.TryGetValue(out RayHit hit) ? hit.Pixel : null;
                if (walkedPixel is not null)
                    hits++;
                if (walked.TryGetValue(out RayOutOfSteps _) || walkedPixel != marched)
                    disagreements.Add($"ray {index} {ray}: walk {walked}, march {marched}");
            }

            hits.Should().BeGreaterThan(200_000, "most random rays meet a surface of the scene");
            disagreements.Should().BeEmpty();
            output.WriteLine(
                $"1,000,000 random rays, seed 20261104, cap {Cap}: port mean {portSteps / 1e6:F6} cells; nudged V6 mean {spikeSteps / 1e6:F6} cells."
            );
        }

        private static int NudgedV6Steps(ScreenRay ray, DepthPyramid pyramid, Vector3 direction)
        {
            Vector2 travel = new(MathF.Sign(ray.Step.X), MathF.Sign(ray.Step.Y));
            Vector2 farSide = Vector2.Max(travel, Vector2.Zero);
            float Exit(Vector2 cell, int level)
            {
                Vector2 edge = (cell + farSide) * (1 << level);
                float horizontal = ray.Step.X != 0 ? (edge.X - ray.Start.X) / ray.Step.X : 1e30f;
                float vertical = ray.Step.Y != 0 ? (edge.Y - ray.Start.Y) / ray.Step.Y : 1e30f;
                return MathF.Min(MathF.Min(horizontal, vertical), ray.End);
            }

            float parameter = Exit(new(MathF.Floor(ray.Start.X), MathF.Floor(ray.Start.Y)), 0);
            int level = 0;
            int top = Math.Min(pyramid.TopLevel, ScreenRay.WalkTopLevel);
            for (int steps = 0; steps < Cap; steps++)
            {
                if (parameter >= ray.End)
                    return steps;
                Vector2 position = ray.Start + ray.Step * parameter + travel * 0.005f;
                int side = 1 << level;
                Vector2 cell = new(MathF.Floor(position.X / side), MathF.Floor(position.Y / side));
                float exit = MathF.Max(Exit(cell, level), parameter);
                CellBounds bounds =
                    level == 0
                        ? CellBounds.OfSurface(pyramid.Finest((int)cell.X, (int)cell.Y))
                        : pyramid.Bounds(level, (int)cell.X, (int)cell.Y);
                float back = bounds.Far * (1 / (1 + CellBounds.ThicknessShare));
                float inW = ray.StartInverseDepth + ray.StepInverseDepth * parameter;
                float outW = ray.StartInverseDepth + ray.StepInverseDepth * exit;
                bool meets = bounds.Near > 0 && MathF.Min(inW, outW) <= bounds.Near && MathF.Max(inW, outW) >= back;
                float entry = parameter;
                if (meets && inW > bounds.Near)
                    entry = (bounds.Near - ray.StartInverseDepth) / ray.StepInverseDepth;
                else if (meets && inW < back)
                    entry = (back - ray.StartInverseDepth) / ray.StepInverseDepth;
                entry = MathF.Min(MathF.Max(entry, parameter), exit);
                if (meets && level > 0)
                {
                    parameter = entry;
                    level--;
                    continue;
                }
                Vector2 hit = Vector2.Clamp(ray.Start + ray.Step * entry, cell, cell + Vector2.One);
                if (
                    meets
                    && Vector3.Dot(
                        pyramid.GeometricNormal(
                            Math.Min((int)MathF.Floor(hit.X), pyramid.Width - 1),
                            Math.Min((int)MathF.Floor(hit.Y), pyramid.Height - 1)
                        ),
                        direction
                    ) < 0
                )
                    return steps + 1;
                parameter = exit;
                level = Math.Min(level + 1, top);
            }
            return Cap;
        }

        [Fact]
        public void OnRaysOfBinaryFractionsWhoseEntriesAndCrossingsTieWithCellEdgesTheWalkFindsTheHitOfTheMarch()
        {
            // Every number is a binary fraction of few bits: starts on quarter pixels, steps of powers of two pixels,
            // and inverse depths on sixteenths. So the image crosses pixel corners, and slab entries fall exactly on
            // pixel edges and on the center lines of cells, which random rays of a camera never do. Every surface faces
            // the ray. The segment's end lies 1/1024 off the grid, so that no slab entry falls exactly on it.
            var random = new Random(20_261_104);
            float[] steps = [-8, -4, -2, -1, 0, 1, 2, 4, 8];
            Vector3 direction = -Vector3.UnitZ;
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

                RayTrace walked = ray.Walk(pyramid: pyramid, direction: direction, maxSteps: Cap);
                (int X, int Y)? marched = FlatSlabMarch(ray, pyramid, direction)?.Pixel;

                (int X, int Y)? walkedPixel = walked.TryGetValue(out RayHit hit) ? hit.Pixel : null;
                if (marched is not null)
                    hits++;
                if (walked.TryGetValue(out RayOutOfSteps _) || walkedPixel != marched)
                    disagreements.Add($"ray {index} {ray}: walk {walked}, march {marched}");
            }

            hits.Should().BeGreaterThan(10_000, "many rays meet a surface on the grid");
            disagreements.Should().BeEmpty();
        }
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

    /// <summary>The image of a ray from a bare origin, whose own view depth is the depth that the walk expects of it.
    /// </summary>
    private static ScreenRay RayOf(Vector3 origin, Vector3 direction, in CameraView camera) =>
        ScreenRay.Of(
            origin: origin,
            direction: direction,
            viewDepth: Vector3.Dot(origin, camera.Forward),
            camera: camera
        );

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
    /// The oracle of the walk: every pixel that the image crosses, in order, from the exit of the origin's pixel. A pixel
    /// whose stretch of the image meets its flat slab, from its center's inverse depth w to w / (1 + κ), and whose
    /// surface faces the ray, is the hit. Each step leaves a pixel across the edges through which the image leaves it,
    /// so no pixel that the image crosses is skipped, however near its corner.
    /// </summary>
    /// <returns>The hit pixel, or null for a ray that reaches the segment's end.</returns>
    private static ((int X, int Y) Pixel, float EntryShare)? FlatSlabMarch(
        ScreenRay ray,
        DepthPyramid pyramid,
        Vector3 direction
    )
    {
        var travel = new Vector2(MathF.Sign(ray.Step.X), MathF.Sign(ray.Step.Y));
        Vector2 pixel = Floor(ray.Start);
        (float s, Vector2 crossed) = PixelExit(ray, pixel, travel);
        while (s < ray.End)
        {
            pixel += travel * crossed;
            (float exit, Vector2 crossedAtExit) = PixelExit(ray, pixel, travel);
            (int x, int y) = ((int)pixel.X, (int)pixel.Y);
            float w = pyramid.Finest(x, y);
            float back = w / (1 + CellBounds.ThicknessShare);
            float inW = ray.StartInverseDepth + ray.StepInverseDepth * s;
            float outW = ray.StartInverseDepth + ray.StepInverseDepth * exit;
            bool meets = w > 0 && MathF.Min(inW, outW) <= w && MathF.Max(inW, outW) >= back;
            if (meets && Vector3.Dot(pyramid.GeometricNormal(x, y), direction) < 0)
                return ((x, y), Math.Clamp((w - inW) / (w - back), 0, 1));
            s = exit;
            crossed = crossedAtExit;
        }
        return null;
    }

    /// <summary>How many pixels the march crosses from the exit of the origin's pixel to the segment's end.</summary>
    private static int MarchedPixels(ScreenRay ray)
    {
        var travel = new Vector2(MathF.Sign(ray.Step.X), MathF.Sign(ray.Step.Y));
        Vector2 pixel = Floor(ray.Start);
        (float s, Vector2 crossed) = PixelExit(ray, pixel, travel);
        int pixels = 0;
        while (s < ray.End)
        {
            pixel += travel * crossed;
            (s, crossed) = PixelExit(ray, pixel, travel);
            pixels++;
        }
        return pixels;
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
}

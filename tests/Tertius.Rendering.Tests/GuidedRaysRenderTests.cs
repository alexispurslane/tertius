using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The guided diffuse rays on the GPU (ARCHITECTURE.md, "Guided diffuse rays"): the light pyramid's levels, the mixture
/// density in the ray records, and a floor's bounce light from a small glowing patch, with the guiding on and off through
/// the debug switch TERTIUS_GUIDED_RAYS (<see cref="LightPyramidPass.SwitchedOn"/>). The camera of
/// <see cref="Gpu.Snapshot"/> stands still, and each frame steps one tick.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class GuidedRaysRenderTests
{
    private const string Switch = "TERTIUS_GUIDED_RAYS";

    /// <summary>The debug switch of the jitter (<see cref="DebugJitterSelection"/>).</summary>
    private const string JitterSwitch = "TERTIUS_DEBUG_JITTER";

    private const int Width = 160,
        Height = 90;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The glow of the patch, about 100 times the bounce light that it gives the floor below it.</summary>
    private const float PatchGlow = 40;

    /// <summary>The center of the glowing panel, 0.6 m over the floor, which stands 1 cm out of the wall.</summary>
    private static readonly Vector3 PanelCenter = new(0.8f, FloorHeight + 0.6f, -6.99f);

    [Fact]
    public void WithoutTheSwitchTheLightPyramidPassRunsAndTheDiffuseRaysAreGuided() =>
        WithSwitch(
            null,
            () =>
            {
                VulkanRenderer renderer = Gpu.Headless(width: 64, height: 36, screenTrace: ScreenTrace.Default);
                try
                {
                    renderer.ScreenTraceImages.Guided.Should().BeTrue();
                    renderer.Graph.Passes.OfType<LightPyramidPass>().Should().ContainSingle();
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void TheOffSettingBuildsNoLightPyramidAndGuidesNoRay() =>
        WithSwitch(
            "off",
            () =>
            {
                VulkanRenderer renderer = Gpu.Headless(width: 64, height: 36, screenTrace: ScreenTrace.Default);
                try
                {
                    renderer.ScreenTraceImages.Guided.Should().BeFalse();
                    renderer.Graph.Passes.OfType<LightPyramidPass>().Should().BeEmpty();
                    renderer.Render(Dark(), 1f);
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void AnUnknownSettingIsRefused() =>
        WithSwitch(
            "sometimes",
            () =>
            {
                Action select = () => LightPyramidPass.SwitchedOn();

                select.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_GUIDED_RAYS*");
            }
        );

    [Fact]
    public void TheLightPyramidHoldsEachPixelsGlowAndEachCellTheSumOfTheCellsUnderIt() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = GlowOverFloor();
                try
                {
                    renderer.Render(scene, 1f);

                    (float[] finest, int width, int height) = renderer.ReadLightPyramidLevel(0);
                    (width, height).Should().Be((Width, Height));
                    finest.Max().Should().BeApproximately(Luminance(new(PatchGlow)), 0.01f * PatchGlow);
                    finest.Count(light => light > 0).Should().BeInRange(20, 400, "the patch's pixels alone glow");
                    var copy = LightPyramid.Of(finest, Width, Height);
                    for (int level = 1; level <= copy.TopLevel; level++)
                    {
                        (float[] lights, int levelWidth, _) = renderer.ReadLightPyramidLevel(level);
                        (int Width, int Height) size = DepthPyramid.LevelSize(Width, Height, level);
                        for (int y = 0; y < size.Height; y++)
                        for (int x = 0; x < size.Width; x++)
                            lights[y * levelWidth + x]
                                .Should()
                                .BeApproximately(copy.Light(level, x, y), 1e-5f * copy.Total, $"cell ({x}, {y}) of {level}");
                    }
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void WithTheSwitchOffEachBlocksDiffuseDensityInALitSceneIsTheCosineDensity() =>
        WithSwitch(
            "off",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = GlowOverFloor();
                try
                {
                    renderer.Render(scene, 1f);

                    (int compared, int above, _) = CompareBlockDensities(renderer);
                    compared.Should().BeGreaterThan(1000);
                    above.Should().Be(0, "no ray has a guided density");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void AGuidedBlocksDiffuseDensityHoldsTheCosineShareAndTheGuidedDensityOfItsHit() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = GlowOverFloor();
                try
                {
                    renderer.Render(scene, 1f);

                    (int compared, int above, _) = CompareBlockDensities(renderer);
                    compared.Should().BeGreaterThan(1000);
                    above.Should().BeGreaterThan(compared / 8, "about half the rays aim at the patch, which they reach");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void WithNoLitPixelInViewEachBlocksDiffuseDensityIsTheCosineDensity() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = SceneOverFloor(Dark(), panel: false);
                try
                {
                    renderer.Render(scene, 1f);

                    (int compared, int above, int below) = CompareBlockDensities(renderer);
                    compared.Should().BeGreaterThan(1000);
                    (above, below).Should().Be((0, 0), "a screen without guide light guides no ray");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void ASunlitSurfacesPixelsHoldGuideLight() =>
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = SceneOverFloor(
                    Gpu.Snapshot(Vector3.UnitY),
                    panel: false
                );
                try
                {
                    renderer.Render(scene, 1f);

                    (float[] lights, _, _) = renderer.ReadLightPyramidLevel(0);
                    float[] distances = renderer.ReadDistance();
                    int surfaces = distances.Count(distance => distance > 0);
                    int lit = Enumerable.Range(0, lights.Length).Count(pixel => distances[pixel] > 0 && lights[pixel] > 0);
                    lit.Should().BeGreaterThan(surfaces / 4, "the star overhead lights the floor");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void OnAJitteredFrameEachHitsDiffuseDensityIsTheCopysMixtureOfTheCosineAndTheGuidedDensity() =>
        WithSwitch(
            null,
            () =>
            {
                string? previousJitter = Environment.GetEnvironmentVariable(JitterSwitch);
                Environment.SetEnvironmentVariable(JitterSwitch, "always");
                (VulkanRenderer renderer, RenderSnapshot scene) = GlowOverFloor();
                try
                {
                    for (int frame = 0; frame < 3; frame++)
                    {
                        scene.Time += Tick;
                        renderer.Render(scene, 1f);
                    }

                    (int compared, int guided) = CompareHitDensities(renderer);
                    compared.Should().BeGreaterThan(400);
                    guided.Should().BeGreaterThan(compared / 8, "about half the rays aim at the patch, which they reach");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                    Environment.SetEnvironmentVariable(JitterSwitch, previousJitter);
                }
            }
        );

    /// <summary>
    /// The floor's bounce light over 96 frames with the guiding on and off, which runs for seconds (tests/STYLE.md, "Long
    /// tests").
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class BounceLightOverManyFrames
    {
        [Fact]
        public void AGlowingPatchsBounceLightOnTheFloorKeepsItsMeanAndLosesMostOfItsNoise()
        {
            const int frames = 96,
                skipped = 16;
            bool[] floor = FloorBesidePatch();
            (double guidedMean, double guidedError, double guidedVariance) = WithSwitch(
                null,
                () => BounceLight(floor: floor, frames: frames, skipped: skipped, graySky: false)
            );
            (double cosineMean, double cosineError, double cosineVariance) = WithSwitch(
                "off",
                () => BounceLight(floor: floor, frames: frames, skipped: skipped, graySky: false)
            );

            double error = Math.Sqrt(guidedError * guidedError + cosineError * cosineError);
            guidedMean
                .Should()
                .BeApproximately(cosineMean, Math.Max(4 * error, 0.03 * cosineMean), $"the standard error is {error}");
            guidedVariance
                .Should()
                .BeLessThan(0.3 * cosineVariance, $"the guided rays find the patch in most frames ({guidedMean}, {cosineMean})");
        }

        [Fact]
        public void UnderAGraySkyTheFloorsLightKeepsItsMeanWithTheGuiding()
        {
            const int frames = 64,
                skipped = 16;
            bool[] floor = FloorBesidePatch();
            (double guidedMean, double guidedError, _) = WithSwitch(
                null,
                () => BounceLight(floor: floor, frames: frames, skipped: skipped, graySky: true)
            );
            (double cosineMean, double cosineError, _) = WithSwitch(
                "off",
                () => BounceLight(floor: floor, frames: frames, skipped: skipped, graySky: true)
            );

            double error = Math.Sqrt(guidedError * guidedError + cosineError * cosineError);
            guidedMean
                .Should()
                .BeApproximately(
                    cosineMean,
                    Math.Max(4 * error, 0.03 * cosineMean),
                    $"a cosine ray that misses weighs 1 / (1 - g) of its cosine weight, and a guided miss brings nothing ({error})"
                );
        }
    }

    /// <summary>
    /// A dark world with a white floor 1.5 m below the eye, a white wall 7 m ahead, and on the wall a glowing panel of
    /// 0.6 m by 0.4 m a little to the right: the only light, which the floor takes from the rays that hit it.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) GlowOverFloor() => SceneOverFloor(Dark(), panel: true);

    /// <summary>
    /// <paramref name="world"/> with a white floor 1.5 m below the eye, a white wall 7 m ahead, and with
    /// <paramref name="panel"/> the glowing panel of <see cref="GlowOverFloor"/> on the wall.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) SceneOverFloor(RenderSnapshot world, bool panel)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = world;
        MaterialDesc white = MaterialDesc.Color(Vector4.One, roughness: 1f);
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        scene.Instances.Add(Floor(renderer, white));
        scene.Instances.Add(
            Block(box: box, material: renderer.CreateMaterial(white), center: new(0, 2, -7.5f), size: new(30, 7, 1))
        );
        if (panel)
            scene.Instances.Add(
                Block(
                    box: box,
                    material: Glow(renderer, new(PatchGlow)),
                    center: PanelCenter,
                    size: new(0.6f, 0.4f, 0.02f)
                )
            );
        return (renderer, scene);
    }

    /// <summary>The floor pixels within 40 px of the panel on the screen, row by row from the top.</summary>
    private static bool[] FloorBesidePatch()
    {
        (int X, int Y) panel = PixelOf(PanelCenter, Width, Height);
        (_, int wallBottom) = PixelOf(new(PanelCenter.X, FloorHeight, -7), Width, Height);
        bool[] floor = new bool[Width * Height];
        WithSwitch(
            null,
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = GlowOverFloor();
                try
                {
                    renderer.Render(scene, 1f);
                    (float[] lights, _, _) = renderer.ReadLightPyramidLevel(0);
                    float[] distances = renderer.ReadDistance();
                    for (int y = Height / 2 + 4; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int pixel = y * Width + x;
                        bool near = Math.Abs(x - panel.X) < 40 && y > wallBottom + 2;
                        floor[pixel] = near && distances[pixel] > 0 && lights[pixel] == 0;
                    }
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );
        floor.Count(pixel => pixel).Should().BeGreaterThan(500);
        return floor;
    }

    /// <summary>
    /// The floor's diffuse samples over <paramref name="frames"/> frames from frame <paramref name="skipped"/> on: the mean
    /// luminance, its standard error from the frames' means, and the mean over the pixels of each pixel's variance. With
    /// <paramref name="graySky"/>, a gray sky of 0.5 also lights the floor, through the rays that miss.
    /// </summary>
    private static (double Mean, double Error, double Variance) BounceLight(
        bool[] floor,
        int frames,
        int skipped,
        bool graySky
    )
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = SceneOverFloor(graySky ? Gray(0.5f) : Dark(), panel: true);
        try
        {
            int pixels = floor.Count(pixel => pixel);
            double[] sums = new double[floor.Length];
            double[] squares = new double[floor.Length];
            var frameMeans = new List<double>();
            for (int frame = 0; frame < frames; frame++)
            {
                scene.Time += Tick;
                renderer.Render(scene, 1f);
                if (frame < skipped)
                    continue;
                float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0));
                double frameSum = 0;
                for (int pixel = 0; pixel < floor.Length; pixel++)
                {
                    if (!floor[pixel])
                        continue;
                    double light = Luminance(new(sample[4 * pixel], sample[4 * pixel + 1], sample[4 * pixel + 2]));
                    sums[pixel] += light;
                    squares[pixel] += light * light;
                    frameSum += light;
                }
                frameMeans.Add(frameSum / pixels);
            }
            int count = frames - skipped;
            double mean = frameMeans.Average();
            double spread = Math.Sqrt(frameMeans.Sum(m => (m - mean) * (m - mean)) / (count - 1));
            double variance = 0;
            for (int pixel = 0; pixel < floor.Length; pixel++)
                if (floor[pixel])
                    variance += squares[pixel] / count - Math.Pow(sums[pixel] / count, 2);
            return (mean, spread / Math.Sqrt(count), variance / pixels);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Compares each block's diffuse density with the cosine density of its direction at its chosen pixel, over the floor
    /// blocks of the last frame: how many it compared, and how many lie above it, which only a guided density explains.
    /// Every density must be at least the cosine share of the mixture. Also how many lie below the cosine density, which
    /// only the mixture explains.
    /// </summary>
    private static (int Compared, int Above, int Below) CompareBlockDensities(VulkanRenderer renderer)
    {
        float[] plan = renderer.ReadImage(renderer.ScreenTraceImages.Plan);
        float[] records = renderer.ReadImageLayer(renderer.ScreenTraceImages.BlockRay, 1);
        float[] normals = renderer.ReadNormals();
        float[] distances = renderer.ReadDistance();
        float share = renderer.ScreenTraceImages.Guided ? LightPyramid.GuidedShare : 0;
        int compared = 0,
            above = 0,
            below = 0;
        // Each block from its top left corner, at even coordinates: from row 50, below the horizon at row 45.
        for (int y = Height / 2 + 5; y < Height - 1; y += 2)
        for (int x = 0; x < Width - 1; x += 2)
        {
            int chosen = Enumerable
                .Range(0, 4)
                .FirstOrDefault(corner => ((int)plan[(y + corner / 2) * Width + x + corner % 2] & 2) == 0, -1);
            if (chosen < 0)
                continue;
            int pixel = (y + chosen / 2) * Width + x + chosen % 2;
            int record = 4 * (y / 2 * ((Width + 1) / 2) + x / 2);
            if (distances[pixel] <= 0 || ((int)records[record + 3] & 1) == 0)
                continue;
            Vector3 normal = OctahedralNormal.Decode(new(normals[4 * pixel], normals[4 * pixel + 1]));
            Vector3 direction = OctahedralNormal.Decode(
                new(records[record] * (2 / 65535f) - 1, records[record + 1] * (2 / 65535f) - 1)
            );
            double cosine = Math.Max(Vector3.Dot(normal, direction), 0) / Math.PI;
            double density = Math.Pow(2, (double)BitConverter.UInt16BitsToHalf((ushort)records[record + 2]));
            density.Should().BeGreaterThanOrEqualTo((1 - share) * cosine * 0.98, "the cosine draw's share stays");
            if (density > cosine * 1.05 + 1e-4)
                above++;
            if (density < cosine * 0.98 - 1e-4)
                below++;
            compared++;
        }
        return (compared, above, below);
    }

    /// <summary>
    /// Compares each floor block's diffuse density with the C# copy's mixture density, over the blocks of the last frame
    /// whose ray hit: the copy rebuilds the ray's origin from the chosen pixel's distance and the frame's jitter, and the
    /// hit pixel from the hit distance. Fewer than 1 in 20 may differ by more than 1.5 %. Returns how many it compared,
    /// and how many have a guided density above 0.
    /// </summary>
    private static (int Compared, int Guided) CompareHitDensities(VulkanRenderer renderer)
    {
        CameraView camera = renderer.LastScene.Camera;
        Vector4 parameters = renderer.LastScene.Constants.RenderParams;
        var jitter = new Vector2(parameters.Y, parameters.Z);
        jitter.Should().NotBe(Vector2.Zero, "the debug switch jitters every draw");
        (float[] lights, _, _) = renderer.ReadLightPyramidLevel(0);
        var light = LightPyramid.Of(lights, Width, Height);
        var depth = DepthPyramid.FromInverseDepths(renderer.ReadDepthFinest(), Width, Height);
        float[] plan = renderer.ReadImage(renderer.ScreenTraceImages.Plan);
        float[] records = renderer.ReadImageLayer(renderer.ScreenTraceImages.BlockRay, 1);
        float[] hits = renderer.ReadImageLayer(renderer.ScreenTraceImages.BlockLight, 1);
        float[] normals = renderer.ReadNormals();
        float[] distances = renderer.ReadDistance();
        int compared = 0,
            guided = 0;
        var mismatched = new List<string>();
        for (int y = Height / 2 + 5; y < Height - 1; y += 2)
        for (int x = 0; x < Width - 1; x += 2)
        {
            int chosen = Enumerable
                .Range(0, 4)
                .FirstOrDefault(corner => ((int)plan[(y + corner / 2) * Width + x + corner % 2] & 2) == 0, -1);
            if (chosen < 0)
                continue;
            (int X, int Y) start = (x + chosen % 2, y + chosen / 2);
            int pixel = start.Y * Width + start.X;
            int record = 4 * (y / 2 * ((Width + 1) / 2) + x / 2);
            float hitDistance = hits[record + 3];
            if (distances[pixel] <= 0 || ((int)records[record + 3] & 1) == 0 || hitDistance <= 0)
                continue;
            Vector3 direction = OctahedralNormal.Decode(
                new(records[record] * (2 / 65535f) - 1, records[record + 1] * (2 / 65535f) - 1)
            );
            Vector3 origin = OriginOf(camera: camera, jitter: jitter, pixel: start, distance: distances[pixel], normals: normals);
            // The hit distance runs from the origin to the hit pixel's center on its surface, not along the ray, so a few
            // points project into a neighbor of the hit pixel, whose 3 × 3 pixels differ.
            Vector2 place = camera.PixelOf(origin + direction * hitDistance) - jitter;
            float guidedDensity = light.GuidedDensity(
                camera: camera,
                depth: depth,
                origin: origin,
                direction: direction,
                hit: ((int)MathF.Floor(place.X), (int)MathF.Floor(place.Y)),
                jitter: jitter
            );
            Vector3 normal = OctahedralNormal.Decode(new(normals[4 * pixel], normals[4 * pixel + 1]));
            double expected = LightPyramid.MixtureDensity(
                MathF.Max(Vector3.Dot(normal, direction), 0) / MathF.PI,
                guidedDensity
            );
            double density = Math.Pow(2, (double)BitConverter.UInt16BitsToHalf((ushort)records[record + 2]));
            if (Math.Abs(density - expected) > Math.Max(0.015 * expected, 1e-6))
                mismatched.Add($"({x}, {y}): {density} against {expected}");
            compared++;
            if (guidedDensity > 0)
                guided++;
        }
        mismatched.Count.Should().BeLessThan(compared / 20, string.Join("; ", mismatched));
        return (compared, guided);
    }

    /// <summary>
    /// The origin of the rays of <paramref name="pixel"/>, camera-relative: its surface point on the jittered view ray at
    /// its distance, off the surface along its geometric normal, as tracingPixelAt in screen_trace.glsl places it.
    /// </summary>
    private static Vector3 OriginOf(in CameraView camera, Vector2 jitter, (int X, int Y) pixel, float distance, float[] normals)
    {
        int index = pixel.Y * Width + pixel.X;
        Vector3 geometricNormal = OctahedralNormal.Decode(new(normals[4 * index + 2], normals[4 * index + 3]));
        Vector3 ray = camera.ViewRayThrough(new Vector2(pixel.X + 0.5f, pixel.Y + 0.5f) + jitter);
        float viewDepth = distance / ray.Length();
        return Vector3.Normalize(ray) * distance + geometricNormal * ScreenRay.OriginOffset(viewDepth, camera.PixelAngle);
    }

    private static float Luminance(Vector3 light) => Vector3.Dot(light, new(0.2126f, 0.7152f, 0.0722f));

    /// <summary>Runs <paramref name="run"/> with the switch at <paramref name="value"/>, and restores it.</summary>
    private static T WithSwitch<T>(string? value, Func<T> run)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            return run();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }

    private static void WithSwitch(string? value, Action run) =>
        WithSwitch(
            value,
            () =>
            {
                run();
                return 0;
            }
        );
}

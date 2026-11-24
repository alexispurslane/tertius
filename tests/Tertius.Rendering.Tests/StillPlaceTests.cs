using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A still pixel's last place is the center of its own texel in the last image, so its reprojection reads that texel with
/// the weight 1 and the three other taps with 0 (ticket t0-y88b). The place is a share of the image, and its trip from a
/// pixel to a share and back to texels rounds: at a width such as 1280, which is no power of two, the place lands up to
/// about 1e-4 texel off the center. A place off the center by more than that rounding keeps its bilinear weights. Seen
/// through still_place_probe.comp, which calls the real reprojection on the real last place, and through the accumulation
/// trace of the real screen tracer and accumulator, on the plate and the glowing block of
/// <see cref="AccumulationTraceTests"/>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class StillPlaceTests
{
    private const string Agreement = "TERTIUS_REFLECTION_HISTORY_AGREEMENT";

    // The layers of the trace and the flags of layer 0's alpha (TemporalSignal.AccumulationTrace).
    private const int Lengths = 0;
    private const int Weights = 1;
    private const int Place = 2;
    private const int AntiLag = 4;
    private const int SurfacePixel = 1 << 9;
    private const int PixelPlaceRead = 1 << 10;

    /// <summary>The ray plan's FullRateFrames: a history this long shares its block's ray.</summary>
    private const float FullRateFrames = 8;

    /// <summary>A dark dielectric plate of roughness 0.2, the native captures' rough plate.</summary>
    private static readonly MaterialDesc Plate = new(
        BaseColor: new(new Vector3(0.06f), 1),
        Roughness: 0.2f,
        Reflectance: 0.5f,
        Model: new StandardShading(Metallic: 0)
    );

    private static readonly UpscalingSetting Jittered = new Fsr3Upscaling(new FixedRenderScale(1), 0);

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void AStillPixelsLastPlaceReadsItsOwnTexelWithTheWholeWeight(int width, int height)
    {
        (float[] specular, _) = Probe(width, height);

        int[] surface = ProbedSurface(specular);
        surface.Length.Should().BeGreaterThan(width * height / 4, "the plate fills the lower half of the view");
        surface
            .Where(pixel => specular[4 * pixel] == 0)
            .Should()
            .BeEmpty("a still pixel's last place is its own texel's center, whatever the image's width");
        // A pixel whose own texel fails the surface test, as at a grazing edge of the block, reads the jitter-corrected
        // sample place instead (PixelHistoryTests).
        surface.Count(pixel => specular[4 * pixel] == 0.5f).Should().BeLessThan(surface.Length / 100);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void APlaceASixtyFourthOfATexelOffACenterKeepsItsBilinearWeights(int width, int height)
    {
        (float[] specular, _) = Probe(width, height);

        int[] surface = ProbedSurface(specular);
        // The own texel takes (63 / 64)^2 and the texel to its right 63 / 64 · 1 / 64. The probe's half floats keep the
        // second exactly and the first to 2^-11, and the place's own rounding moves both by about 1e-4.
        surface.Should().OnlyContain(pixel => MathF.Abs(specular[4 * pixel + 1] - 3969f / 4096) < 1e-3f);
        surface.Should().OnlyContain(pixel => MathF.Abs(specular[4 * pixel + 2] - 63f / 4096) < 1e-3f);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void APlaceASixtyFourthOfATexelLeftOfAndAboveACenterKeepsItsBilinearWeights(int width, int height)
    {
        (float[] specular, float[] diffuse) = Probe(width, height);

        int[] surface = ProbedSurface(specular);
        // The snap compares the distance to the center on both sides: a place before the center keeps its weights as one
        // after it does. The texel to the left takes 63 / 64 · 1 / 64.
        surface.Should().OnlyContain(pixel => MathF.Abs(diffuse[4 * pixel + 1] - 63f / 4096) < 1e-3f);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void APlaceAThreeHundredthOfATexelOffACenterReadsThatCenter(int width, int height)
    {
        (float[] specular, _) = Probe(width, height);

        int[] surface = ProbedSurface(specular);
        surface
            .Where(pixel => specular[4 * pixel + 3] != 1)
            .Should()
            .BeEmpty("a place this near a center is that center up to the rounding of a still frame's place");
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void APlaceATwoHundredthOfATexelOffACenterKeepsItsBilinearWeights(int width, int height)
    {
        (float[] specular, float[] diffuse) = Probe(width, height);

        int[] surface = ProbedSurface(specular);
        // The texel to the right takes 199 / 200 · 1 / 200, about 0.004975. The place's own rounding moves the offset by
        // up to about 2.3e-4 texel at a width of 3840, a float step of a share near 1 times the width.
        surface.Should().OnlyContain(pixel => MathF.Abs(diffuse[4 * pixel] - 199f / 40000) < 5e-4f);
    }

    [Fact]
    public void EveryStillInteriorPixelReadsItsOwnTexelWithTheWholeWeight() =>
        WithTracedRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 3);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] place = TraceLayer(renderer, Place);
                float[] weights = TraceLayer(renderer, Weights);
                bool[] silhouette = Silhouettes(renderer);
                int[] interior =
                [
                    .. TracedSurface(lengths)
                        .Where(pixel => !silhouette[pixel] && HasFlag(lengths, pixel, PixelPlaceRead)),
                ];
                interior.Length.Should().BeGreaterThan(TracedWidth * TracedHeight / 4);
                interior
                    .Where(pixel => !ReadsOnlyItsOwnTexel(place, weights, pixel))
                    .Should()
                    .BeEmpty(
                        "the reprojection of a still pixel's place gives its own texel the weight 1 and the others 0"
                    );
            }
        );

    [Fact]
    public void AStillPixelAtASilhouetteReadsItsOwnTexelThroughItsOwnTexelsPassBit() =>
        WithTracedRenderer(
            new NoUpscaling(),
            (renderer, scene) =>
            {
                // At a silhouette, a neighbor texel of the last image shows another surface and fails the surface test.
                // The surface test and the reprojection must name the same four texels: a pass bit read for the wrong
                // texel would zero the own texel's weight, and the pixel would lose its pixel place. Without jitter a
                // still pixel's own texel always showed its own surface; under jitter it can show another at a
                // silhouette, and the pixel then reads its sample place by design (the pixel history rule).
                Draw(renderer, scene, 3);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] place = TraceLayer(renderer, Place);
                float[] weights = TraceLayer(renderer, Weights);
                bool[] silhouette = Silhouettes(renderer);
                int[] edge = [.. TracedSurface(lengths).Where(pixel => silhouette[pixel])];
                edge.Length.Should().BeGreaterThan(TracedWidth, "the block's outline crosses the view");
                edge.Where(pixel => !HasFlag(lengths, pixel, PixelPlaceRead))
                    .Should()
                    .BeEmpty("a still pixel's own texel shows its own surface, so its pixel place holds a history");
                edge.Where(pixel => !ReadsOnlyItsOwnTexel(place, weights, pixel)).Should().BeEmpty();
            }
        );

    [Fact]
    public void AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws() =>
        WithTracedRenderer(
            Jittered,
            (renderer, scene) =>
            {
                // The anti-lag cuts some young histories from draw 4 on. A neighbor whose history it cut must not pull a
                // still pixel's length under its schedule, or the ray plan's test of FullRateFrames fails at a history
                // as good as one of 8 draws (ticket t0-y88b).
                bool[] cut = new bool[TracedWidth * TracedHeight];
                float[] antiLag = [];
                for (int draw = 0; draw < FullRateFrames; draw++)
                {
                    Draw(renderer, scene, 1);
                    antiLag = TraceLayer(renderer, AntiLag);
                    for (int pixel = 0; pixel < cut.Length; pixel++)
                        cut[pixel] |= antiLag[4 * pixel + 1] < antiLag[4 * pixel];
                }

                float[] lengths = TraceLayer(renderer, Lengths);
                bool[] silhouette = Silhouettes(renderer);
                int[] uncut =
                [
                    .. TracedSurface(lengths)
                        .Where(pixel => !cut[pixel] && !silhouette[pixel] && HasFlag(lengths, pixel, PixelPlaceRead)),
                ];
                uncut.Length.Should().BeGreaterThan(TracedWidth * TracedHeight / 4);
                uncut
                    .Count(pixel => NextToACut(cut, pixel))
                    .Should()
                    .BeGreaterThan(0, "the test needs still pixels next to a history that a cut shortened");
                uncut.Where(pixel => antiLag[4 * pixel + 1] != FullRateFrames).Should().BeEmpty();
            }
        );

    private const int TracedWidth = 1280;
    private const int TracedHeight = 720;

    /// <summary>
    /// The probe's two images after a still draw at <paramref name="width"/> × <paramref name="height"/>: the draw keeps the
    /// render size of the two draws before it.
    /// </summary>
    private static (float[] Specular, float[] Diffuse) Probe(int width, int height)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            framesInFlight: 1,
            cascadedLights: 0,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            upscaling: Jittered
        );
        try
        {
            RenderSnapshot scene = Scene(renderer);
            Draw(renderer, scene, 2);
            ScreenTraceProbe.On(renderer, "still_place_probe.comp");
            Draw(renderer, scene, 1);
            return (
                Specular: renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0)),
                Diffuse: renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0))
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The pixels of the probe's specular image that show a surface.</summary>
    private static int[] ProbedSurface(float[] specular) =>
        [.. Enumerable.Range(0, specular.Length / 4).Where(pixel => specular[4 * pixel] >= 0)];

    /// <summary>
    /// Whether the trace's taps give the pixel's own texel the weight 1 and every other tap 0: the trace's place says where
    /// tap 0 lies relative to the pixel.
    /// </summary>
    private static bool ReadsOnlyItsOwnTexel(float[] place, float[] weights, int pixel)
    {
        int x = (int)MathF.Floor(place[4 * pixel]);
        int y = (int)MathF.Floor(place[4 * pixel + 1]);
        for (int tap = 0; tap < 4; tap++)
        {
            bool own = x + (tap & 1) == 0 && y + (tap >> 1) == 0;
            if (weights[4 * pixel + tap] != (own ? 1 : 0))
                return false;
        }
        return true;
    }

    /// <summary>Whether a pixel of the 3 × 3 pixels around <paramref name="pixel"/>, other than itself, is cut.</summary>
    private static bool NextToACut(bool[] cut, int pixel)
    {
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            int nx = pixel % TracedWidth + x;
            int ny = pixel / TracedWidth + y;
            if (
                (x != 0 || y != 0)
                && nx >= 0
                && ny >= 0
                && nx < TracedWidth
                && ny < TracedHeight
                && cut[ny * TracedWidth + nx]
            )
                return true;
        }
        return false;
    }

    /// <summary>The pixels whose trace says that they show a surface.</summary>
    private static int[] TracedSurface(float[] lengths) =>
        [.. Enumerable.Range(0, TracedWidth * TracedHeight).Where(pixel => HasFlag(lengths, pixel, SurfacePixel))];

    private static bool HasFlag(float[] layer, int pixel, int flag) => ((int)layer[4 * pixel + 3] & flag) != 0;

    /// <summary>
    /// For each pixel, whether the 3 × 3 pixels around it show another surface or the sky: a jump of the distance by a
    /// tenth marks a silhouette.
    /// </summary>
    private static bool[] Silhouettes(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        bool[] result = new bool[TracedWidth * TracedHeight];
        for (int pixel = 0; pixel < result.Length; pixel++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            int nx = pixel % TracedWidth + x;
            int ny = pixel / TracedWidth + y;
            if (nx < 0 || ny < 0 || nx >= TracedWidth || ny >= TracedHeight)
                continue;
            float neighbor = distance[ny * TracedWidth + nx];
            if (neighbor <= 0 || MathF.Abs(neighbor - distance[pixel]) > 0.1f * distance[pixel])
                result[pixel] = true;
        }
        return result;
    }

    private static float[] TraceLayer(VulkanRenderer renderer, int layer) =>
        renderer.ReadImageLayer(
            renderer.ScreenTraceSignals.Specular.AccumulationTrace(0)
                ?? throw new InvalidOperationException("The accumulation trace is off."),
            layer
        );

    /// <summary>An ambient gray, the plate as the floor, and a glowing block 5 m ahead.</summary>
    private static RenderSnapshot Scene(VulkanRenderer renderer)
    {
        RenderSnapshot scene = ReflectionTesting.Gray(0.5f);
        scene.Instances.Add(ReflectionTesting.Floor(renderer, Plate));
        RenderInstance emitter = ReflectionTesting.Block(
            box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
            material: ReflectionTesting.Glow(renderer, new(4)),
            center: new(0, 0, -5),
            size: new(4, 1.5f, 1)
        );
        emitter.EntityIndex = 2;
        scene.Instances.Add(emitter);
        return scene;
    }

    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Tick++;
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a renderer of 1280 × 720 with <paramref name="upscaling"/>, the accumulation trace and
    /// <see cref="Scene"/>.
    /// </summary>
    private static void WithTracedRenderer(UpscalingSetting upscaling, Action<VulkanRenderer, RenderSnapshot> action)
    {
        string? previousAgreement = Environment.GetEnvironmentVariable(Agreement);
        Environment.SetEnvironmentVariable(Agreement, "control-trace");
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: TracedWidth,
                height: TracedHeight,
                framesInFlight: 1,
                cascadedLights: 0,
                screenTrace: ScreenTrace.Default,
                warmUpFrames: 0,
                upscaling: upscaling
            );
            try
            {
                action(renderer, Scene(renderer));
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Agreement, previousAgreement);
        }
    }
}

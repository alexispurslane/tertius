using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The opt-in trace of the specular accumulation (ticket tiunr-4b2g, <see cref="HistoryInstrumentation.ControlTrace"/>): what
/// each pixel's reprojection, blend, box clamp and anti-lag did to its slow history in the last draw, on the real screen
/// tracer and accumulator. The trace only observes, so the renderer draws exactly as without it.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AccumulationTraceTests(ITestOutputHelper output)
{
    private const int Width = 128;
    private const int Height = 72;
    private const string Switch = "TERTIUS_REFLECTION_HISTORY_AGREEMENT";

    // The layers of the trace and the flags of their alpha channels (TemporalSignal.AccumulationTrace).
    private const int Lengths = 0;
    private const int Weights = 1;
    private const int Place = 2;
    private const int Spatial = 4;
    private const int Unclamped = 5;
    private const int Mean = 6;
    private const int Deviation = 7;
    private const int HasSample = 1 << 8;
    private const int ReflectedWon = 1 << 4;
    private const int ReflectedTried = 1 << 5;

    /// <summary>A dark dielectric plate of roughness 0.2, the native captures' rough plate.</summary>
    private static readonly MaterialDesc Plate = new(
        BaseColor: new(new Vector3(0.06f), 1),
        Roughness: 0.2f,
        Reflectance: 0.5f,
        Model: new StandardShading(Metallic: 0)
    );

    private static readonly UpscalingSetting Jittered = new Fsr3Upscaling(new FixedRenderScale(1), 0);

    [Fact]
    public void TheDiffuseSignalKeepsNoTrace() =>
        WithRenderer(
            Jittered,
            (renderer, _) =>
            {
                renderer.ScreenTraceImages.Specular.AccumulationTrace(0).Should().NotBeNull();
            }
        );

    [Fact]
    public void TheTraceRendersExactlyAsTheDefaultUnderJitter()
    {
        float[][] expected = ImagesAfterJitteredStill(null);

        float[][] actual = ImagesAfterJitteredStill("control-trace");

        for (int image = 0; image < expected.Length; image++)
        {
            int[] differ =
            [
                .. Enumerable.Range(0, expected[image].Length).Where(i => actual[image][i] != expected[image][i]),
            ];
            foreach (int index in differ.Take(12))
                output.WriteLine(
                    $"Image {image}, pixel {index / 4}, channel {index % 4}: {expected[image][index]:R} against {actual[image][index]:R}."
                );
            output.WriteLine($"Image {image}: {differ.Length} values differ.");
        }
        for (int image = 0; image < expected.Length; image++)
            actual[image].Should().Equal(expected[image], "the trace only observes");
    }

    [Fact]
    public void TheTraceGivesEachPixelTheLengthThatTheLastDrawAcceptedAtIt() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 24);
                float[] accepted = Slow(renderer);

                Draw(renderer, scene, 1);

                float[] lengths = TraceLayer(renderer, Lengths);
                int[] surface = SurfacePixels(renderer);
                surface.Length.Should().BeGreaterThan(Width * Height / 4);
                surface.Should().OnlyContain(pixel => lengths[4 * pixel] == accepted[4 * pixel + 3]);
            }
        );

    [Fact]
    public void TheTraceGivesTheLengthThatTheSpatialDispatchAccepts() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 25);

                float[] accepted = Slow(renderer);
                float[] spatial = TraceLayer(renderer, Spatial);
                SurfacePixels(renderer)
                    .Should()
                    .OnlyContain(pixel => spatial[4 * pixel + 1] == accepted[4 * pixel + 3]);
            }
        );

    [Fact]
    public void TheSpatialDispatchStartsFromTheReprojectedLengthPlusTheBlendedFrame() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 25);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] spatial = TraceLayer(renderer, Spatial);
                int[] sampled = [.. SurfacePixels(renderer).Where(pixel => HasFlag(lengths, pixel, HasSample))];
                sampled.Length.Should().BeGreaterThan(Width * Height / 4);
                sampled
                    .Should()
                    .OnlyContain(
                        pixel => spatial[4 * pixel] == lengths[4 * pixel + 1] + 1,
                        "the uncapped blend adds one frame to the reprojected length"
                    );
            }
        );

    [Fact]
    public void AStillUnjitteredPixelReprojectsOntoItsOwnTexelWithItsWholeWeight() =>
        WithRenderer(
            new NoUpscaling(),
            (renderer, scene) =>
            {
                Draw(renderer, scene, 16);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] weights = TraceLayer(renderer, Weights);
                float[] place = TraceLayer(renderer, Place);
                int[] interior = InteriorPixels(renderer);
                interior.Length.Should().BeGreaterThan(Width * Height / 4);
                interior.Should().OnlyContain(pixel => MathF.Abs(lengths[4 * pixel + 2] - 1) < 1e-5f);
                interior
                    .Should()
                    .OnlyContain(
                        pixel => OwnTexelWeight(place, weights, pixel) == 1,
                        "the place is the texel's center (lastTexelPlace in signals.glsl)"
                    );
            }
        );

    [Fact]
    public void UnderTheHeldJitterNoStillJitteredPixelReadsItsHistoryAwayFromItsOwnTexel()
    {
        string? previous = Environment.GetEnvironmentVariable("TERTIUS_DEBUG_JITTER");
        Environment.SetEnvironmentVariable("TERTIUS_DEBUG_JITTER", "held");
        try
        {
            WithRenderer(
                Jittered,
                (renderer, scene) =>
                {
                    Draw(renderer, scene, 17);

                    float[] lengths = TraceLayer(renderer, Lengths);
                    float[] place = TraceLayer(renderer, Place);
                    float[] distance = renderer.ReadDistance();
                    int[] held =
                    [
                        .. Enumerable
                            .Range(0, Width * Height)
                            .Where(pixel => distance[pixel] > 0 && lengths[4 * pixel + 2] > 0),
                    ];
                    held.Length.Should().BeGreaterThan(Width * Height / 4);
                    held.Should()
                        .OnlyContain(
                            pixel => MathF.Abs(place[4 * pixel]) < 1e-3f && MathF.Abs(place[4 * pixel + 1]) < 1e-3f,
                            "the last draw's jitter counts as this draw's, so even a silhouette's sample place is its own texel"
                        );
                }
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERTIUS_DEBUG_JITTER", previous);
        }
    }

    [Fact]
    public void TheAntiLagShortensTheLengthByTheTracedLagOverItsShare() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 40);

                float[] spatial = TraceLayer(renderer, Spatial);
                float[] mean = TraceLayer(renderer, Mean);
                int[] clamped = ClampedPixels(renderer);
                output.WriteLine(
                    $"{clamped.Length} clamped pixels, {clamped.Count(pixel => mean[4 * pixel + 3] > 0)} with lag."
                );
                clamped.Count(pixel => mean[4 * pixel + 3] > 0).Should().BeGreaterThan(10);
                clamped
                    .Should()
                    .OnlyContain(pixel =>
                        MathF.Abs(spatial[4 * pixel + 1] - AntiLagged(spatial[4 * pixel], mean[4 * pixel + 3]))
                        <= 1e-4f * spatial[4 * pixel]
                    );
            }
        );

    [Fact]
    public void TheClampedHistoryLiesInsideTheTracedBox() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 40);

                float[] slow = Slow(renderer);
                float[] mean = TraceLayer(renderer, Mean);
                float[] deviation = TraceLayer(renderer, Deviation);
                int[] clamped = ClampedPixels(renderer);
                clamped.Length.Should().BeGreaterThan(Width * Height / 4);
                clamped
                    .Should()
                    .OnlyContain(
                        pixel =>
                            MathF.Abs(Luminance(slow, pixel) - Luminance(mean, pixel))
                            <= SignalParameters.Specular(0.4f).ClampSigmas * Luminance(deviation, pixel)
                                + 1e-4f * (1 + Luminance(mean, pixel)),
                        "the box is the mean plus and minus its half width in every channel"
                    );
            }
        );

    [Fact]
    public void WhereTheBoxTakesAllNineFastHistoriesItsSpreadIsTheirSpreadAroundTheirMean() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 40);

                // The box takes its moments around the pixel's own fast history (ticket tc-8142). Its spread is still
                // the spread around the nine values' mean: a spread around the pixel's own value would be wider
                // wherever the pixel differs from its neighbors. A box leaves out a neighbor of another surface (the
                // block's edge) or of a view depth more than 10 % away (the floor near the horizon), so the test takes
                // the boxes whose nine pixels share the pixel's normal and lie within 5 % of its distance.
                float[] fast = renderer.ReadImage(
                    renderer.ScreenTraceImages.Specular.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1)
                );
                float[] mean = TraceLayer(renderer, Mean);
                float[] deviation = TraceLayer(renderer, Deviation);
                float[] normals = renderer.ReadNormals();
                float[] distance = renderer.ReadDistance();
                int[] boxes =
                [
                    .. InteriorPixels(renderer)
                        .Intersect(ClampedPixels(renderer))
                        .Where(pixel => AllNineInTheBox(normals: normals, distance: distance, pixel: pixel)),
                ];
                boxes.Length.Should().BeGreaterThan(Width * Height / 8);
                boxes
                    .Should()
                    .OnlyContain(
                        pixel =>
                            Enumerable
                                .Range(0, 3)
                                .All(channel =>
                                    Close(mean[4 * pixel + channel], NineAround(fast, pixel, channel).Average())
                                    && Close(deviation[4 * pixel + channel], SpreadOf(NineAround(fast, pixel, channel)))
                                ),
                        "the box's mean and spread are those of the nine values, and the spread is around their mean"
                    );
            }
        );

    [Fact]
    public void TheLagIsTheClampsMoveAsAShareOfTheMeansLuminance() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 40);

                float[] slow = Slow(renderer);
                float[] unclamped = TraceLayer(renderer, Unclamped);
                float[] mean = TraceLayer(renderer, Mean);
                int[] clamped = ClampedPixels(renderer);
                clamped.Count(pixel => mean[4 * pixel + 3] > 0).Should().BeGreaterThan(10);
                clamped
                    .Should()
                    .OnlyContain(pixel =>
                        MathF.Abs(
                            mean[4 * pixel + 3] * MathF.Max(Luminance(mean, pixel), 1e-4f)
                                - Luminance(Moved(unclamped, slow, pixel), 0)
                        )
                        <= 1e-4f * (1 + Luminance(mean, pixel))
                    );
            }
        );

    [Fact]
    public void APlateBelowTheHandOverTriesTheReflectedPlaceForEverySampledPixel() =>
        WithRenderer(
            Jittered,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 16);

                float[] lengths = TraceLayer(renderer, Lengths);
                // The bottom twelve rows show only the plate.
                int[] sampled =
                [
                    .. Enumerable
                        .Range((Height - 12) * Width, 12 * Width)
                        .Where(pixel => HasFlag(lengths, pixel, HasSample)),
                ];
                sampled.Length.Should().BeGreaterThan(Width * 6);
                sampled.Should().OnlyContain(pixel => HasFlag(lengths, pixel, ReflectedTried));
                SurfacePixels(renderer)
                    .Should()
                    .OnlyContain(
                        pixel => !HasFlag(lengths, pixel, ReflectedWon) || HasFlag(lengths, pixel, ReflectedTried),
                        "only a tried place can win"
                    );
            }
        );

    /// <summary>The pixels whose spatial dispatch ran the clamp: layer 5's alpha says so.</summary>
    private static int[] ClampedPixels(VulkanRenderer renderer)
    {
        float[] unclamped = TraceLayer(renderer, Unclamped);
        return [.. SurfacePixels(renderer).Where(pixel => unclamped[4 * pixel + 3] == 1)];
    }

    /// <summary>The clamp's move of each channel at <paramref name="pixel"/>, as one RGBA pixel.</summary>
    private static float[] Moved(float[] unclamped, float[] slow, int pixel) =>
        [
            .. Enumerable
                .Range(0, 4)
                .Select(channel => MathF.Abs(unclamped[4 * pixel + channel] - slow[4 * pixel + channel])),
        ];

    /// <summary>The length that the anti-lag leaves of <paramref name="before"/> for <paramref name="lag"/>.</summary>
    private static float AntiLagged(float before, float lag) =>
        MathF.Max(4, before * (1 - MathF.Min(1, lag / SignalParameters.Specular(0.4f).AntiLagShare)));

    /// <summary>
    /// The weight of the tap at the pixel's own texel: the trace's place says where tap 0 lies relative to the pixel.
    /// </summary>
    private static float OwnTexelWeight(float[] place, float[] weights, int pixel)
    {
        int x = (int)MathF.Floor(place[4 * pixel]);
        int y = (int)MathF.Floor(place[4 * pixel + 1]);
        for (int tap = 0; tap < 4; tap++)
            if (x + (tap & 1) == 0 && y + (tap >> 1) == 0)
                return weights[4 * pixel + tap];
        return 0;
    }

    /// <summary>
    /// Whether the clamp's box surely takes all nine pixels around <paramref name="pixel"/>: they share its shading
    /// normal, and each distance lies within 5 % of its own, well inside the box's 10 % of view depth (temporal.glsl,
    /// sameSurface), since the view ray's length changes by far less between neighbors.
    /// </summary>
    private static bool AllNineInTheBox(float[] normals, float[] distance, int pixel) =>
        Enumerable
            .Range(-1, 3)
            .SelectMany(y => Enumerable.Range(-1, 3).Select(x => pixel + y * Width + x))
            .All(neighbor =>
                normals[4 * neighbor] == normals[4 * pixel]
                && normals[4 * neighbor + 1] == normals[4 * pixel + 1]
                && MathF.Abs(distance[neighbor] - distance[pixel]) <= 0.05f * distance[pixel]
            );

    /// <summary>Whether a traced value matches an exact one within the GPU's single-float arithmetic.</summary>
    private static bool Close(float traced, double exact) => Math.Abs(traced - exact) <= 1e-4 * (1 + Math.Abs(exact));

    /// <summary>The population spread of <paramref name="values"/> around their mean.</summary>
    private static double SpreadOf(double[] values)
    {
        double average = values.Average();
        return Math.Sqrt(values.Average(value => (value - average) * (value - average)));
    }

    /// <summary>The <paramref name="channel"/> of the 3 × 3 pixels around <paramref name="pixel"/> in an RGBA image.</summary>
    private static double[] NineAround(float[] rgba, int pixel, int channel) =>
        [
            .. Enumerable
                .Range(-1, 3)
                .SelectMany(y =>
                    Enumerable.Range(-1, 3).Select(x => (double)rgba[4 * (pixel + y * Width + x) + channel])
                ),
        ];

    private static bool HasFlag(float[] layer, int pixel, int flag) => ((int)layer[4 * pixel + 3] & flag) != 0;

    private static float Luminance(float[] rgba, int pixel) =>
        0.2126f * rgba[4 * pixel] + 0.7152f * rgba[4 * pixel + 1] + 0.0722f * rgba[4 * pixel + 2];

    private static float[] Slow(VulkanRenderer renderer) =>
        renderer.ReadImage(renderer.ScreenTraceImages.Specular.Slow(0));

    private static float[] TraceLayer(VulkanRenderer renderer, int layer) =>
        renderer.ReadImageLayer(
            renderer.ScreenTraceImages.Specular.AccumulationTrace(0)
                ?? throw new InvalidOperationException("The accumulation trace is off."),
            layer
        );

    /// <summary>The pixels that show a surface.</summary>
    private static int[] SurfacePixels(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        return [.. Enumerable.Range(0, Width * Height).Where(pixel => distance[pixel] > 0)];
    }

    /// <summary>The floor's pixels in the lower half of the screen whose eight neighbors show a surface too.</summary>
    private static int[] InteriorPixels(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        return
        [
            .. Enumerable
                .Range(Height / 2 * Width, Width * (Height / 2 - 1))
                .Where(pixel =>
                    pixel % Width is > 0 and < Width - 1
                    && Enumerable
                        .Range(-1, 3)
                        .All(y => Enumerable.Range(-1, 3).All(x => distance[pixel + y * Width + x] > 0))
                ),
        ];
    }

    /// <summary>The specular and diffuse slow histories, the specular sample and the HDR image after a jittered still.</summary>
    private static float[][] ImagesAfterJitteredStill(string? value)
    {
        float[][] result = [];
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            WithRenderer(
                Jittered,
                (renderer, scene) =>
                {
                    Draw(renderer, scene, 40);
                    result =
                    [
                        Slow(renderer),
                        renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0)),
                        renderer.ReadHdr(),
                    ];
                },
                setSwitch: false
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
        return result;
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
    /// Runs <paramref name="action"/> on a renderer with the accumulation trace, unless <paramref name="setSwitch"/> is
    /// false, and a scene of an ambient gray, the plate as the floor and a glowing block 5 m ahead.
    /// </summary>
    private static void WithRenderer(
        UpscalingSetting upscaling,
        Action<VulkanRenderer, RenderSnapshot> action,
        bool setSwitch = true
    )
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        if (setSwitch)
            Environment.SetEnvironmentVariable(Switch, "control-trace");
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                framesInFlight: 1,
                cascadedLights: 0,
                screenTrace: ScreenTrace.Default,
                warmUpFrames: 0,
                upscaling: upscaling
            );
            try
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
                action(renderer, scene);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }
}

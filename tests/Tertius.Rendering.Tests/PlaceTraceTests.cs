using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The accumulation trace's record of a pixel's two history places (ticket tiunr-wk29): the sample place, which moves with
/// the jitter, and the pixel place, the pixel moved by the scene's motion alone (the pixel history rule, ticket tiunr-4b2g).
/// Layer 8 holds the weight of the taps that pass the surface test at each place, and layer 0's flags say which place the
/// pixel read.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PlaceTraceTests(ITestOutputHelper output)
{
    private const int Width = 128;
    private const int Height = 72;
    private const string Agreement = "TERTIUS_REFLECTION_HISTORY_AGREEMENT";

    // The layers of the trace and the flags of layer 0's alpha (TemporalSignal.AccumulationTrace).
    private const int Lengths = 0;
    private const int Places = 8;
    private const int ReflectedWon = 1 << 4;
    private const int ReflectedTried = 1 << 5;
    private const int PixelPlaceRead = 1 << 10;
    private const int ReflectedPixelPlaceRead = 1 << 11;

    /// <summary>signals.glsl's LeastHistoryWeight.</summary>
    private const float LeastHistoryWeight = 0.05f;

    /// <summary>A dark dielectric plate of roughness 0.2, the native captures' rough plate.</summary>
    private static readonly MaterialDesc Plate = new(
        BaseColor: new(new Vector3(0.06f), 1),
        Roughness: 0.2f,
        Reflectance: 0.5f,
        Model: new StandardShading(Metallic: 0)
    );

    private static readonly UpscalingSetting Jittered = new Fsr3Upscaling(new FixedRenderScale(1), 0);

    [Fact]
    public void TheTraceGivesTheSpecularWeightOfThePlaceThatThePixelRead() =>
        WithRenderer(
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 16);

                DrawMoving(renderer, scene, 2);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] places = TraceLayer(renderer, Places);
                int[] surface = [.. SurfacePixels(renderer).Where(pixel => !HasFlag(lengths, pixel, ReflectedWon))];
                surface.Length.Should().BeGreaterThan(Width * Height / 4);
                surface
                    .Should()
                    .OnlyContain(
                        pixel =>
                            MathF.Abs(
                                lengths[4 * pixel + 2]
                                    - (
                                        HasFlag(lengths, pixel, PixelPlaceRead)
                                            ? places[4 * pixel + 2]
                                            : places[4 * pixel + 3]
                                    )
                            ) < 1e-5f,
                        "layer 8 weighs each place's taps that pass the specular test, as the reprojection does"
                    );
            }
        );

    [Fact]
    public void TheTraceMarksEveryPixelThatReadsAtItsPixelPlace() =>
        WithRenderer(
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 16);

                DrawMoving(renderer, scene, 2);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] motion = renderer.ReadMotion();
                int[] surface = SurfacePixels(renderer);
                int marked = surface.Count(pixel => HasFlag(lengths, pixel, PixelPlaceRead));
                output.WriteLine($"{marked} of {surface.Length} surface pixels read at their pixel place.");
                marked.Should().BeGreaterThan(Width * Height / 4);
                surface
                    .Should()
                    .OnlyContain(
                        pixel => HasFlag(lengths, pixel, PixelPlaceRead) == (motion[4 * pixel + 3] != 0),
                        "the surface test marks the choice in the motion target's alpha"
                    );
            }
        );

    [Fact]
    public void APixelFallsBackExactlyWhereItsPixelPlaceHoldsLessThanTheLeastWeight() =>
        WithRenderer(
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 16);

                DrawMoving(renderer, scene, 2);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] places = TraceLayer(renderer, Places);
                int[] surface = SurfacePixels(renderer);
                int fallen = surface.Count(pixel => !HasFlag(lengths, pixel, PixelPlaceRead));
                output.WriteLine($"{fallen} of {surface.Length} surface pixels fall back to the sample place.");
                fallen.Should().BeGreaterThan(0, "the plate's border and the block's outline fall back");
                surface
                    .Should()
                    .OnlyContain(pixel =>
                        HasFlag(lengths, pixel, PixelPlaceRead) == (places[4 * pixel] >= LeastHistoryWeight)
                    );
            }
        );

    [Fact]
    public void AStillJitteredInteriorPixelsOwnTexelHoldsTheWholeWeightOfItsPixelPlace() =>
        WithRenderer(
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                float[] places = TraceLayer(renderer, Places);
                int[] interior = InteriorPixels(renderer);
                interior.Length.Should().BeGreaterThan(Width * Height / 6);
                interior
                    .Should()
                    .OnlyContain(
                        pixel => places[4 * pixel] > 0.9999f && places[4 * pixel + 2] > 0.9999f,
                        "a still pixel's pixel place is its own texel's center, on the same flat plate"
                    );
            }
        );

    [Fact]
    public void AStillTriedReflectedPlaceIsItsPixelPlace() =>
        WithRenderer(
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                float[] lengths = TraceLayer(renderer, Lengths);
                int[] tried = [.. InteriorPixels(renderer).Where(pixel => HasFlag(lengths, pixel, ReflectedTried))];
                tried
                    .Length.Should()
                    .BeGreaterThan(Width * Height / 8, "the test needs pixels that try the reflected place");
                tried.Should().OnlyContain(pixel => HasFlag(lengths, pixel, ReflectedPixelPlaceRead));
                SurfacePixels(renderer)
                    .Should()
                    .OnlyContain(
                        pixel =>
                            !HasFlag(lengths, pixel, ReflectedPixelPlaceRead)
                            || HasFlag(lengths, pixel, ReflectedTried),
                        "only a tried reflected place has a place to choose"
                    );
            }
        );

    [Fact]
    public void TheTracedRendererRendersExactlyAsTheUntracedOneUnderJitterAndMotion()
    {
        float[][] expected = ImagesAfterJitteredMotion(trace: false);

        float[][] actual = ImagesAfterJitteredMotion(trace: true);

        for (int image = 0; image < expected.Length; image++)
        {
            int[] differ =
            [
                .. Enumerable.Range(0, expected[image].Length).Where(i => actual[image][i] != expected[image][i]),
            ];
            output.WriteLine($"Image {image}: {differ.Length} values differ.");
        }
        for (int image = 0; image < expected.Length; image++)
            actual[image].Should().Equal(expected[image], "the trace only observes");
    }

    /// <summary>
    /// The specular and diffuse slow histories, the specular sample, the HDR image and FSR's output after 30 still and 10
    /// moving jittered draws.
    /// </summary>
    private static float[][] ImagesAfterJitteredMotion(bool trace)
    {
        float[][] result = [];
        WithRenderer(
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 30);
                DrawMoving(renderer, scene, 10);
                result =
                [
                    renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0)),
                    renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0)),
                    renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0)),
                    renderer.ReadHdr(),
                    renderer.ReadPostHdr(),
                ];
            },
            trace: trace
        );
        return result;
    }

    private static bool HasFlag(float[] layer, int pixel, int flag) => ((int)layer[4 * pixel + 3] & flag) != 0;

    private static float[] TraceLayer(VulkanRenderer renderer, int layer)
    {
        GpuImage trace =
            renderer.ScreenTraceSignals.Specular.AccumulationTrace(0)
            ?? throw new InvalidOperationException("The accumulation trace is off.");
        if (layer >= trace.ArrayLayers)
            throw new InvalidOperationException($"The accumulation trace has no layer {layer}.");
        return renderer.ReadImageLayer(trace, layer);
    }

    /// <summary>The pixels that show a surface.</summary>
    private static int[] SurfacePixels(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        return [.. Enumerable.Range(0, Width * Height).Where(pixel => distance[pixel] > 0)];
    }

    /// <summary>
    /// The pixels in the lower half of the screen whose 5 × 5 pixels show a surface within a tenth of their distance: no
    /// silhouette.
    /// </summary>
    private static int[] InteriorPixels(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        return
        [
            .. Enumerable
                .Range(Height / 2 * Width, Width * (Height / 2 - 2))
                .Where(pixel =>
                    pixel % Width is > 1 and < Width - 2
                    && distance[pixel] > 0
                    && Enumerable
                        .Range(-2, 5)
                        .All(y =>
                            Enumerable
                                .Range(-2, 5)
                                .All(x =>
                                    distance[pixel + y * Width + x] > 0
                                    && MathF.Abs(distance[pixel + y * Width + x] - distance[pixel])
                                        <= 0.1f * distance[pixel]
                                )
                        )
                ),
        ];
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

    /// <summary>Draws <paramref name="frames"/> frames in which the camera steps 2 cm to the right.</summary>
    private static void DrawMoving(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Camera.PreviousPosition = scene.Camera.Position;
            scene.Camera.PreviousRotation = scene.Camera.Rotation;
            scene.Camera.Position += new Vector3(0.02f, 0, 0);
            scene.Tick++;
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1);
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a jittered FSR renderer, with the accumulation trace unless
    /// <paramref name="trace"/> is false, and a scene of an ambient gray, the plate as the floor and a glowing block 5 m
    /// ahead.
    /// </summary>
    private static void WithRenderer(Action<VulkanRenderer, RenderSnapshot> action, bool trace = true)
    {
        string? previousAgreement = Environment.GetEnvironmentVariable(Agreement);
        Environment.SetEnvironmentVariable(Agreement, trace ? "control-trace" : null);
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: Width,
                height: Height,
                framesInFlight: 1,
                cascadedLights: 0,
                screenTrace: ScreenTrace.Default,
                warmUpFrames: 0,
                upscaling: Jittered
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
            Environment.SetEnvironmentVariable(Agreement, previousAgreement);
        }
    }
}

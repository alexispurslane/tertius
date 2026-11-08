using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The pixel history rule (ticket tiunr-4b2g, "jitter is not motion"): a history texel stands for its whole pixel, so a
/// history moves only with the scene, never with FSR's jitter. A pixel whose own place holds less than LeastHistoryWeight
/// of passing taps, as at a silhouette, falls back to the jitter-corrected place. Seen through the accumulation trace of the
/// real screen tracer and accumulator, on the plate and the glowing block of <see cref="AccumulationTraceTests"/>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PixelHistoryTests(ITestOutputHelper output)
{
    private const int Width = 128;
    private const int Height = 72;
    private const string Agreement = "TERTIUS_REFLECTION_HISTORY_AGREEMENT";

    // The layers of the trace and the flags of their alpha channels (TemporalSignal.AccumulationTrace).
    private const int Lengths = 0;
    private const int Weights = 1;
    private const int Place = 2;
    private const int Places = 8;
    private const int ReflectedWon = 1 << 4;
    private const int ReflectedTried = 1 << 5;
    private const int SurfaceHolds = 1 << 6;
    private const int ReflectedHolds = 1 << 7;

    /// <summary>signals.glsl's LeastHistoryWeight.</summary>
    private const float LeastHistoryWeight = 0.05f;

    /// <summary>A dark dielectric plate of roughness 0.2, the native captures' rough plate.</summary>
    private static readonly MaterialDesc Plate = new(
        BaseColor: new(new Vector3(0.06f), 1),
        Roughness: 0.2f,
        Reflectance: 0.5f,
        Model: new StandardShading(Metallic: 0)
    );

    /// <summary>A smooth dielectric plate of roughness 0.05: its roughness differs from the plate's by more than RoughnessStep.</summary>
    private static readonly MaterialDesc SmoothPlate = Plate with { Roughness = 0.05f };

    /// <summary>The x in m of the seam between the plate and the smooth plate in <see cref="SeamedFloor"/>.</summary>
    private const float Seam = 0.5f;

    private static readonly UpscalingSetting Jittered = new Fsr3Upscaling(new FixedRenderScale(1), 0);

    [Fact]
    public void AStillPixelUnderFsrJitterReadsItsOwnHistoryTexel()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                float[] place = TraceLayer(renderer, Place);
                float[] weights = TraceLayer(renderer, Weights);
                int[] interior = InteriorPixels(renderer);
                interior.Length.Should().BeGreaterThan(Width * Height / 6);
                interior
                    .Should()
                    .OnlyContain(
                        pixel => MathF.Abs(place[4 * pixel]) < 1e-4f && MathF.Abs(place[4 * pixel + 1]) < 1e-4f,
                        "the jitter does not move a history"
                    );
                interior.Should().OnlyContain(pixel => OwnTexelWeight(place, weights, pixel) > 0.9999f);
            }
        );
    }

    [Fact]
    public void AStillPixelUnderAJitterStepWiderThanThePlaneBoundKeepsItsWholeHistory()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                // At 90 degrees, one pixel of the 72 rows spans 2 / 72 of the view depth, so a step of the jitter by half
                // a row moves a point at the same view depth off the floor by 1.4 % of it, past the plane test's 1 %.
                scene.Camera.VerticalFov = MathF.PI / 2;
                Draw(renderer, scene, 17);

                MathF.Abs(JitterChange(renderer).Y).Should().BeGreaterThan(0.5f, "the test needs a wide step of the jitter");
                float[] place = TraceLayer(renderer, Place);
                float[] weights = TraceLayer(renderer, Weights);
                int[] interior = InteriorPixels(renderer);
                interior.Length.Should().BeGreaterThan(Width * Height / 6);
                interior
                    .Should()
                    .OnlyContain(
                        pixel => OwnTexelWeight(place, weights, pixel) > 0.9999f,
                        "the surface test rebuilds the last point through the place where the last draw's sample saw it"
                    );
            }
        );
    }

    [Fact]
    public void UnderAJitterStepWiderThanThePlaneBoundAStillReflectedPlaceHoldsWhereTheSurfacePlaceHolds()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                // As in AStillPixelUnderAJitterStepWiderThanThePlaneBoundKeepsItsWholeHistory.
                scene.Camera.VerticalFov = MathF.PI / 2;
                Draw(renderer, scene, 17);

                MathF.Abs(JitterChange(renderer).Y).Should().BeGreaterThan(0.5f, "the test needs a wide step of the jitter");
                float[] lengths = TraceLayer(renderer, Lengths);
                int[] tried =
                [
                    .. InteriorPixels(renderer)
                        .Where(pixel => HasFlag(lengths, pixel, ReflectedTried) && HasFlag(lengths, pixel, SurfaceHolds)),
                ];
                tried.Length.Should().BeGreaterThan(Width * Height / 8);
                double held = tried.Count(pixel => HasFlag(lengths, pixel, ReflectedHolds)) / (double)tried.Length;
                output.WriteLine($"The reflected place holds at {held:P1} of {tried.Length} pixels whose surface place holds.");
                held.Should()
                    .BeGreaterThan(
                        0.75,
                        "a still mirror shows its hit along the view ray, so its reflected place is its surface place, "
                            + "tested against the same last point; the hit distance test cuts about 9 %"
                    );
            }
        );
    }

    [Fact]
    public void AStillPixelAtASeamBetweenTwoRoughnessesOfOnePlaneReadsItsOwnHistoryTexel()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                (int x, int y) = ReflectionTesting.PixelOf(new(Seam, ReflectionTesting.FloorHeight, -4), Width, Height);
                x.Should().BeInRange(1, Width - 2, "the test needs the seam on the screen");
                y.Should().BeInRange(Height / 2 + 1, Height - 1, "the test needs the seam in the lower half");
                float[] place = TraceLayer(renderer, Place);
                int[] interior = InteriorPixels(renderer);
                interior.Length.Should().BeGreaterThan(Width * Height / 6);
                interior
                    .Should()
                    .OnlyContain(
                        pixel => MathF.Abs(place[4 * pixel]) < 1e-4f && MathF.Abs(place[4 * pixel + 1]) < 1e-4f,
                        "the place follows the surface test of the diffuse signal, which asks for no equal roughness, so "
                            + "a texel whose last sample saw the other half still stands for its pixel"
                    );
            },
            floor: SeamedFloor
        );
    }

    [Fact]
    public void AStillPixelUnderFsrJitterWhoseReflectedPlaceWinsReadsItsOwnTexel()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] place = TraceLayer(renderer, Place);
                int[] won = [.. InteriorPixels(renderer).Where(pixel => HasFlag(lengths, pixel, ReflectedWon))];
                output.WriteLine($"{won.Length} interior pixels take the reflected place.");
                won.Length.Should().BeGreaterThan(10, "the test needs pixels whose reflected place wins");
                won.Should()
                    .OnlyContain(
                        pixel => MathF.Abs(place[4 * pixel]) < 1e-4f && MathF.Abs(place[4 * pixel + 1]) < 1e-4f,
                        "the reflected place follows the same rule"
                    );
            }
        );
    }

    [Fact]
    public void UnderCameraMotionAPixelUnderFsrJitterReadsAtThePlaceThatItsMotionAloneGives()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 16);

                DrawMoving(renderer, scene, 2);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] place = TraceLayer(renderer, Place);
                float[] motion = renderer.ReadMotion();
                // The bottom twelve rows show only the plate.
                int[] plate =
                [
                    .. Enumerable
                        .Range((Height - 12) * Width, 12 * Width)
                        .Where(pixel => pixel % Width is > 0 and < Width - 1 && !HasFlag(lengths, pixel, ReflectedWon)),
                ];
                plate.Length.Should().BeGreaterThan(Width * 6);
                MathF.Abs(motion[4 * plate[0]] * Width).Should().BeGreaterThan(0.2f, "the test needs a moving camera");
                plate
                    .Should()
                    .OnlyContain(
                        pixel =>
                            MathF.Abs(place[4 * pixel] - motion[4 * pixel] * Width) < 1e-3f
                            && MathF.Abs(place[4 * pixel + 1] - motion[4 * pixel + 1] * Height) < 1e-3f,
                        "the place is the pixel's center moved by the scene's motion, as without jitter"
                    );
            }
        );
    }

    [Fact]
    public void ASilhouettePixelWhoseOwnTexelFailsReadsAtTheJitterCorrectedPlace()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                Vector2 change = JitterChange(renderer);
                change.Length().Should().BeGreaterThan(0.05f, "the test needs a draw whose jitter moved");
                float[] place = TraceLayer(renderer, Place);
                float[] weights = TraceLayer(renderer, Weights);
                bool[] silhouette = Silhouettes(renderer);
                int[] corrected =
                [
                    .. SurfacePixels(renderer)
                        .Where(pixel =>
                            MathF.Abs(place[4 * pixel] - change.X) < 1e-3f
                            && MathF.Abs(place[4 * pixel + 1] - change.Y) < 1e-3f
                            && weights.AsSpan(4 * pixel, 4).ToArray().Sum() >= LeastHistoryWeight
                        ),
                ];
                output.WriteLine($"{corrected.Length} pixels read at the jitter-corrected place.");
                corrected.Length.Should().BeGreaterThan(0, "the plate's border and the block's outline fall back");
                corrected.Should().OnlyContain(pixel => silhouette[pixel], "an inside pixel's own texel passes");
            }
        );
    }

    [Fact]
    public void EveryPixelWhoseJitterCorrectedPlaceHoldsAHistoryHoldsOne()
    {
        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 17);

                float[] lengths = TraceLayer(renderer, Lengths);
                float[] places = TraceLayer(renderer, Places);
                int[] held = [.. SurfacePixels(renderer).Where(pixel => places[4 * pixel + 3] >= LeastHistoryWeight)];
                held.Length.Should().BeGreaterThan(Width * Height / 4);
                held.Should()
                    .OnlyContain(
                        pixel => lengths[4 * pixel + 2] >= LeastHistoryWeight,
                        "where the pixel's own texel fails, it falls back to the jitter-corrected place"
                    );
            }
        );
    }

    [Fact]
    public void AStillSlowHistoryUnderFsrJitterGrowsAsLongAsWithoutJitter()
    {
        double expected = 0;
        WithRenderer(
            upscaling: new NoUpscaling(),
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 96);
                expected = MedianReceiverLength(renderer);
            }
        );

        WithRenderer(
            upscaling: Jittered,
            action: (renderer, scene) =>
            {
                Draw(renderer, scene, 96);

                double actual = MedianReceiverLength(renderer);
                output.WriteLine($"Median receiver length: {actual} under jitter, {expected} without.");
                actual.Should().BeGreaterThanOrEqualTo(0.9 * expected, "the jitter does not blur the history");
            }
        );
    }

    /// <summary>
    /// The median accepted slow specular length of the reflection's receivers: the plate's pixels in the lower half of the
    /// screen whose sample's rays hit the block in the last draw.
    /// </summary>
    private static double MedianReceiverLength(VulkanRenderer renderer)
    {
        float[] slow = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0));
        float[] place = TraceLayer(renderer, Place);
        float[] lengths =
        [
            .. InteriorPixels(renderer).Where(pixel => place[4 * pixel + 2] > 0).Select(pixel => slow[4 * pixel + 3]).Order(),
        ];
        lengths.Length.Should().BeGreaterThan(100, "the test needs the reflection's receivers");
        return lengths[lengths.Length / 2];
    }

    /// <summary>The jitter's change from the last draw to this draw, in pixels.</summary>
    private static Vector2 JitterChange(VulkanRenderer renderer)
    {
        int phases = Fsr3Upscaler.JitterPhaseCount(Width, Width);
        long draw = renderer.LastDraw.DrawIndex;
        return Fsr3Upscaler.JitterOffset((int)(draw % phases), phases)
            - Fsr3Upscaler.JitterOffset((int)((draw - 1) % phases), phases);
    }

    /// <summary>
    /// For each pixel, whether the 5 × 5 pixels around it show another surface or the sky: the block is entity 2 and the
    /// plate lies below it, so a jump of the distance by a tenth marks a silhouette.
    /// </summary>
    private static bool[] Silhouettes(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        var result = new bool[Width * Height];
        for (int pixel = 0; pixel < Width * Height; pixel++)
            for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    int nx = pixel % Width + x;
                    int ny = pixel / Width + y;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        continue;
                    float neighbor = distance[ny * Width + nx];
                    if (neighbor <= 0 || MathF.Abs(neighbor - distance[pixel]) > 0.1f * distance[pixel])
                        result[pixel] = true;
                }
        return result;
    }

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

    private static bool HasFlag(float[] layer, int pixel, int flag) => ((int)layer[4 * pixel + 3] & flag) != 0;

    private static float[] TraceLayer(VulkanRenderer renderer, int layer) =>
        renderer.ReadImageLayer(
            renderer.ScreenTraceSignals.Specular.AccumulationTrace(0)
                ?? throw new InvalidOperationException("The accumulation trace is off."),
            layer
        );

    /// <summary>The pixels that show a surface.</summary>
    private static int[] SurfacePixels(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        return [.. Enumerable.Range(0, Width * Height).Where(pixel => distance[pixel] > 0)];
    }

    /// <summary>The pixels in the lower half of the screen that show a surface and lie at no silhouette.</summary>
    private static int[] InteriorPixels(VulkanRenderer renderer)
    {
        float[] distance = renderer.ReadDistance();
        bool[] silhouette = Silhouettes(renderer);
        return
        [
            .. Enumerable
                .Range(Height / 2 * Width, Width * (Height / 2 - 1))
                .Where(pixel => distance[pixel] > 0 && !silhouette[pixel]),
        ];
    }

    /// <summary>
    /// The floor of <see cref="ReflectionTesting.Floor"/> in two halves on one plane: the plate left of x =
    /// <see cref="Seam"/>, the smooth plate right of it. The seam runs away from the camera, so on the screen it crosses
    /// the pixels at every fraction of a pixel.
    /// </summary>
    private static RenderInstance[] SeamedFloor(VulkanRenderer renderer)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
        RenderInstance Half(MaterialDesc material, float centerX) =>
            Gpu.Instance(
                mesh: plane,
                material: renderer.CreateMaterial(material with { NormalMap = ReflectionTesting.FlatNormalMap(renderer) }),
                position: new(centerX, ReflectionTesting.FloorHeight, -20),
                scale: new(15, 1, 30),
                radius: 34,
                chainDepth: TestChains.CamerasFrame
            );
        return [Half(Plate, Seam - 15), Half(SmoothPlate, Seam + 15)];
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
    /// Runs <paramref name="action"/> on a renderer with the accumulation trace and a scene of an ambient gray, the plate
    /// as the floor, or the instances of <paramref name="floor"/>, and a glowing block 5 m ahead.
    /// </summary>
    private static void WithRenderer(
        UpscalingSetting upscaling,
        Action<VulkanRenderer, RenderSnapshot> action,
        Func<VulkanRenderer, RenderInstance[]>? floor = null
    )
    {
        string? previousAgreement = Environment.GetEnvironmentVariable(Agreement);
        Environment.SetEnvironmentVariable(Agreement, "control-trace");
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
                scene.Instances.AddRange(floor?.Invoke(renderer) ?? [ReflectionTesting.Floor(renderer, Plate)]);
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

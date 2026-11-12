using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// FSR 3.1's history over still surfaces whose direct light changes (ticket t1-4hij): the light-age record of each
/// pixel tells FSR where its history holds old light, so a film under a moving lamp shows the lamp's light where it is.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class MovingLightUpscaleTests(ITestOutputHelper output)
{
    private const int Width = 640;
    private const int Height = 360;

    /// <summary>How far the lamp moves in a draw, in m: 1.5 cm, about a render pixel of the floor below it, as in the lamp halls.</summary>
    private const float StepPerDraw = 0.015f;

    /// <summary>The lamp's power in W: 18 W per steradian, so that its light on the floor 2.4 m below shows.</summary>
    private const float LampPower = 4 * MathF.PI * 18;

    /// <summary>The still lamp's place, 2.4 m over the floor 5 m ahead, as high as the lamp halls' lamps.</summary>
    private static readonly Vector3 LampAbove = new(0, ReflectionTesting.FloorHeight + 2.4f, -5);

    /// <summary>A matte gray floor: its direct specular light, which moves with the lamp but no record holds, is a faint, broad lobe.</summary>
    private static readonly MaterialDesc Matte = new(
        BaseColor: new(new Vector3(0.5f), 1),
        Roughness: 1,
        Model: new StandardShading(Metallic: 0)
    );

    [Fact]
    public void AFilmOfAStillFloorUnderAMovingLampShowsTheLampsLightWhereTheLampIs() =>
        WithLampOverFloor(
            power: LampPower,
            (renderer, scene) =>
            {
                Vector3 start = LampAbove - new Vector3(0.6f, 0, 0);
                scene.Lights[0] = Lamp(position: start, previous: start);
                Draw(renderer, scene, 16);
                const int draws = 48;
                for (int draw = 1; draw < draws; draw++)
                {
                    scene.Lights[0] = Lamp(position: PlaceAt(start, draw), previous: PlaceAt(start, draw - 1));
                    Draw(renderer, scene, 1);
                }
                scene.Lights[0] = Lamp(position: PlaceAt(start, draws), previous: PlaceAt(start, draws - 1));
                Capture film = Gpu.RenderAndCapture(renderer: renderer, snapshot: Ticked(scene), name: "moving lamp");

                // The lamp stops where the film's last draw showed it, and FSR settles on that light.
                scene.Lights[0] = Lamp(position: PlaceAt(start, draws), previous: PlaceAt(start, draws));
                Draw(renderer, scene, 64);
                Capture still = Gpu.RenderAndCapture(renderer: renderer, snapshot: Ticked(scene), name: "still lamp");

                // Measured on 2026-11-11: 0.073 without the light-change mask, 0.049 with it, and 0.044 with its marks
                // before the opening. The mask marks a pixel only where its light leaves the range of its neighbors' last
                // light, so a change slower than that keeps some of FSR's history (ARCHITECTURE.md, "Upscaling: FSR 3.1 in
                // the frame").
                float error = TrailError(film, still);
                output.WriteLine($"Moving lamp: 90th percentile of the relative error of the lit floor {error:F4}.");
                error
                    .Should()
                    .BeLessThan(
                        0.055f,
                        "FSR's history must not hold the light of the lamp's earlier places on the still floor"
                    );
            }
        );

    [Fact]
    public void AStillLitSceneUnderJitterMarksAlmostNoPixelForFsr() =>
        WithLampOverFloor(
            power: LampPower,
            (renderer, scene) =>
            {
                // A block on the floor beside the lamp: its edges under jitter alternate its lit faces and the floor.
                RenderInstance block = ReflectionTesting.Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(Matte),
                    center: new(-0.8f, ReflectionTesting.FloorHeight + 0.25f, -4.5f),
                    size: new(0.5f)
                );
                block.EntityIndex = 3;
                scene.Instances.Add(block);
                Draw(renderer, scene, 16);
                long marked = 0;
                long seen = 0;
                for (int frame = 0; frame < 32; frame++)
                {
                    Draw(renderer, scene, 1);
                    float[] mask = renderer.ReadLightChangeMask();
                    marked += mask.Count(value => value > 0);
                    seen += mask.Length;
                }

                output.WriteLine($"Still lit scene: {marked} marked pixels over 32 frames of {seen / 32} pixels.");
                // Measured on 2026-11-11: none of 3.3 million, and 30 before the opening: at a still edge the jitter holds
                // a few pixels' light outside their range for a draw, in a line that the opening removes.
                marked
                    .Should()
                    .BeLessThan(
                        seen / 10_000,
                        "the jitter at a still edge rarely moves a pixel's light out of its range"
                    );
            }
        );

    [Fact]
    public void AStillGlowingBarThinnerThanAPixelMarksAlmostNoPixelForFsrUnderJitter() =>
        WithLampOverFloor(
            power: LampPower,
            (renderer, scene) =>
            {
                // A bar 5 mm tall and 2 m wide, 5 m ahead: about a fifth of a render pixel tall, so the jitter moves each
                // pixel on and off it, as the edge-on floor of rect-lamps' niches (ticket t1-4hij).
                RenderInstance bar = ReflectionTesting.Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: ReflectionTesting.Glow(renderer, new Vector3(40)),
                    center: new(0, ReflectionTesting.FloorHeight + 1.2f, -5),
                    size: new(2, 0.005f, 0.005f)
                );
                bar.EntityIndex = 3;
                scene.Instances.Add(bar);
                Draw(renderer, scene, 16);
                long marked = 0;
                long seen = 0;
                for (int frame = 0; frame < 32; frame++)
                {
                    Draw(renderer, scene, 1);
                    float[] mask = renderer.ReadLightChangeMask();
                    marked += mask.Count(value => value > 0);
                    seen += mask.Length;
                }

                output.WriteLine($"Thin glowing bar: {marked} marked pixels over 32 frames of {seen / 32} pixels.");
                // Measured on 2026-11-11: 1,565 of 3.3 million before the opening, none with it.
                marked
                    .Should()
                    .BeLessThan(
                        seen / 10_000,
                        "the jitter's sampling of a still feature thinner than a pixel is no change of its light"
                    );
            }
        );

    [Fact]
    public void ALightChangeAlongTheImagesEdgeColumnsMarksThemForFsrAsIfTheEdgeRepeatedBeyondTheImage() =>
        WithLampOverFloor(
            power: LampPower,
            (renderer, scene) =>
            {
                // After the tracer's own resolve, the probes rewrite this frame's records and the mask from them.
                ScreenTraceProbe.On(renderer, "light_change_mask_edge_setup.comp");
                ScreenTraceProbe.On(renderer, "screen_trace_resolve.comp");

                Draw(renderer, scene, 1);

                float[] mask = renderer.ReadLightChangeMask();
                // Under a fixed render scale, the viewport fills the image.
                int width = (int)renderer.ScreenTraceImages.LightChangeMask!.Extent.Width;
                int height = mask.Length / width;
                int[] edges = [0, width - 1];
                int[] markedEdgePixels =
                [
                    .. edges.Select(x => Enumerable.Range(0, height).Count(y => mask[y * width + x] > 0)),
                ];
                int markedInside = Enumerable
                    .Range(0, height)
                    .Sum(y => Enumerable.Range(1, width - 2).Count(x => mask[y * width + x] > 0));

                // The opening reads a 5 × 5 window, which reaches two pixels beyond the image at its edges. The clamp
                // repeats the edge there, so a 3 × 3 block that reaches beyond the image changes whole. Without it, the
                // spec leaves those reads undefined.
                markedEdgePixels
                    .Should()
                    .Equal([height, height], "the clamp repeats each changing edge column beyond the image");
                markedInside.Should().Be(0, "a still pixel is never marked");
            }
        );

    [Fact]
    public void ALampThatSwitchesOffMarksItsFloorForFsrFromItsFirstDrawForAFewFramesAfterTheChangeCounts() =>
        WithLampOverFloor(
            power: LampPower,
            (renderer, scene) =>
            {
                Draw(renderer, scene, 16);
                float[] before = renderer.ReadLightAge();
                int[] lit = [.. Enumerable.Range(0, before.Length / 2).Where(pixel => before[2 * pixel] > 0.05f)];
                scene.Lights[0] = Lamp(position: LampAbove, previous: LampAbove, power: 0);
                var shares = new List<float>();
                for (int draw = 1; draw <= 40; draw++)
                {
                    Draw(renderer, scene, 1);
                    float[] mask = renderer.ReadLightChangeMask();
                    shares.Add(lit.Count(pixel => mask[pixel] > 0) / (float)lit.Length);
                }

                output.WriteLine(
                    $"{lit.Length} lit pixels, marked share per draw: {string.Join(", ", shares.Select(s => s.ToString("F3")))}"
                );
                lit.Length.Should().BeGreaterThan(500, "the lamp must light much of the floor");
                // The change counts on its fourth draw. The mask holds while the change is pending and for some frames after
                // it counts, so FSR follows the whole change, and then ends, so a still floor keeps FSR's history again.
                shares
                    .Take(4)
                    .Should()
                    .OnlyContain(share => share > 0.95f, "a pending change marks the pixel from its first draw");
                shares[4].Should().BeGreaterThan(0.95f, "the mask holds in the frame in which the change counts");
                shares[^1].Should().BeLessThan(0.01f, "the mask ends once the light has stayed still for a while");
            }
        );

    /// <summary>The lamp's place at <paramref name="draw"/> draws after <paramref name="start"/>, moving along +X.</summary>
    private static Vector3 PlaceAt(Vector3 start, int draw) => start + new Vector3(StepPerDraw * draw, 0, 0);

    private static RenderSnapshot Ticked(RenderSnapshot scene)
    {
        scene.Tick++;
        scene.Time += 1.0 / 60;
        return scene;
    }

    private static PointRenderLight Lamp(Vector3 position, Vector3 previous, float power = LampPower) =>
        new(
            EntityIndex: Gpu.LampEntity,
            EntityGeneration: 0,
            Position: position,
            PreviousPosition: previous,
            Color: Vector3.One,
            Power: power,
            Radius: 0.05f,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// The 90th percentile of the film's relative difference from the still over the floor that the lamp lights, after a
    /// 5 × 5 box blur: the trail is light of low frequency, and the blur leaves out the jitter of single pixels.
    /// </summary>
    private static float TrailError(Capture film, Capture still)
    {
        float[] a = Blurred(film);
        float[] b = Blurred(still);
        float brightest = b.Max();
        float[] errors =
        [
            .. Enumerable
                .Range(0, Width * Height)
                .Where(pixel => b[pixel] > 0.1f * brightest)
                .Select(pixel => MathF.Abs(a[pixel] - b[pixel]) / b[pixel])
                .Order(),
        ];
        return errors[errors.Length * 9 / 10];
    }

    private static float[] Blurred(Capture capture)
    {
        float[] luminance = new float[Width * Height];
        for (int pixel = 0; pixel < luminance.Length; pixel++)
        {
            Vector3 linear = Measure.ToLinear(
                new Vector3(capture.Rgba[4 * pixel], capture.Rgba[4 * pixel + 1], capture.Rgba[4 * pixel + 2]) / 255
            );
            luminance[pixel] = 0.2126f * linear.X + 0.7152f * linear.Y + 0.0722f * linear.Z;
        }
        float[] blurred = new float[Width * Height];
        for (int y = 2; y < Height - 2; y++)
        for (int x = 2; x < Width - 2; x++)
        {
            float sum = 0;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                sum += luminance[(y + dy) * Width + x + dx];
            blurred[y * Width + x] = sum / 25;
        }
        return blurred;
    }

    /// <summary>
    /// Runs <paramref name="action"/> on a renderer with the screen tracer and FSR 3.1 at the budget's render scale of
    /// 2/3, and a scene of a dim gray ambient light, the matte floor and a lamp of <paramref name="power"/> W at
    /// <see cref="LampAbove"/>, the scene's only light.
    /// </summary>
    private static void WithLampOverFloor(float power, Action<VulkanRenderer, RenderSnapshot> action)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            cascadedLights: 0,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0)
        );
        try
        {
            RenderSnapshot scene = ReflectionTesting.Gray(0.02f);
            scene.Lights.Clear();
            scene.Instances.Add(ReflectionTesting.Floor(renderer, Matte));
            scene.Lights.Add(Lamp(position: LampAbove, previous: LampAbove, power: power));
            action(renderer, scene);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
            renderer.Render(Ticked(scene), 1);
    }
}

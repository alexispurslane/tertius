using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The surface share (ARCHITECTURE.md, "Surface share", ticket t1-wl7c): a pixel sends its fresh light, its glow and its
/// direct light times its material factors, in full only where the last draw showed the same surface there. Elsewhere it
/// sends one part in the spread, min(P, draws since the reset + 1), where P is the jitter's phase count. The hit light
/// and the guide's finest level read the same share. Every draw jitters by FSR's sequence (TERTIUS_DEBUG_JITTER=always)
/// at 160 × 90 without an upscale, so P is 8.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SurfaceShareTests
{
    private const string JitterSwitch = "TERTIUS_DEBUG_JITTER";

    private const int Width = 160,
        Height = 90;

    /// <summary>The jitter's phase count at a render width equal to the output width: Fsr3Upscaler.JitterPhaseCount.</summary>
    private const int Phases = 8;

    /// <summary>The step of each draw after the first: one tick, which continues the last draw.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The glow of the panel in each channel. The test exposure is 0 stops, so the hit light holds it as it is.</summary>
    private const float PanelGlow = 4;

    /// <summary>The panel's entity: no other instance has it, so its first draw has no last place.</summary>
    private const uint PanelEntity = 7;

    private static readonly Vector3 PanelCenter = new(0, 0, -6.9f);

    private static readonly Vector3 PanelSize = new(1.2f, 0.8f, 0.02f);

    /// <summary>
    /// After the reset in draw 0, the panel first shows in draw <paramref name="draw"/>. Its pixels fail the surface test, so
    /// they send 1 / min(P, <paramref name="draw"/> + 1) of the glow toward a ray of either kind. The black panel sends no
    /// other light.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 6)]
    [InlineData(12, Phases)]
    public void ASurfaceInTheDrawThatFirstShowsItSendsOnePartInTheSpreadOfItsGlow(int draw, int spread) =>
        WithJitter(
            "always",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = WallBehindAPanel();
                try
                {
                    Draw(renderer, scene, draws: draw);
                    scene.Instances.Add(Panel(renderer, PanelCenter));
                    Draw(renderer, scene, draws: 1);

                    foreach (float sent in HitLightOfThePanel(renderer))
                        sent.Should()
                            .BeApproximately(PanelGlow / spread, 1e-3f * PanelGlow, "the share is 1 / {0}", spread);
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    /// <summary>
    /// A white rough panel in the sun, under no ambient light, first shows in draw 2: its diffuse hit light is its direct
    /// diffuse light times its share, 1/3, against the same draw without jitter, whose share is 1. Its first draw holds no
    /// history, and the closed form of a world without ambient light is 0.
    /// </summary>
    [Fact]
    public void ASurfaceInTheDrawThatFirstShowsItSendsItsShareOfItsDirectDiffuseLight()
    {
        float[] jittered = LightOfASunlitPanel(jitter: "always", metallic: 0, drawsShowingIt: 1, read: DiffuseHitLight);
        float[] still = LightOfASunlitPanel(jitter: "none", metallic: 0, drawsShowingIt: 1, read: DiffuseHitLight);

        AssertShareOf(jittered: jittered, still: still, share: 1f / 3, channels: 4);
    }

    /// <summary>
    /// The guide aims by the same share of the direct diffuse light as the hit light: in the sunlit panel's first draw, its
    /// guide light is a third of the guide light of the same draw without jitter.
    /// </summary>
    [Fact]
    public void TheGuideSharesTheDirectDiffuseLightOfASurfaceInTheDrawThatFirstShowsIt()
    {
        float[] jittered = LightOfASunlitPanel(jitter: "always", metallic: 0, drawsShowingIt: 1, read: GuideLight);
        float[] still = LightOfASunlitPanel(jitter: "none", metallic: 0, drawsShowingIt: 1, read: GuideLight);

        AssertShareOf(jittered: jittered, still: still, share: 1f / 3, channels: 1);
    }

    /// <summary>
    /// In its second draw the sunlit panel passes the surface test, so it sends all of its direct diffuse light, as the same
    /// draw without jitter does. The black wall sends no bounce to the panel, so its history holds no light in either run.
    /// </summary>
    [Fact]
    public void ASurfaceSendsAllOfItsDirectDiffuseLightFromItsSecondDraw()
    {
        float[] jittered = LightOfASunlitPanel(jitter: "always", metallic: 0, drawsShowingIt: 2, read: DiffuseHitLight);
        float[] still = LightOfASunlitPanel(jitter: "none", metallic: 0, drawsShowingIt: 2, read: DiffuseHitLight);

        AssertShareOf(jittered: jittered, still: still, share: 1, channels: 4);
    }

    /// <summary>
    /// A white metal panel of roughness 0.5 in the sun sends only specular light. In its first draw its reflection hit
    /// light is its direct specular light times its share, 1/3, against the same draw without jitter.
    /// </summary>
    [Fact]
    public void ASurfaceInTheDrawThatFirstShowsItSendsItsShareOfItsDirectSpecularLightTowardAReflectionRay()
    {
        float[] jittered = LightOfASunlitPanel(
            jitter: "always",
            metallic: 1,
            drawsShowingIt: 1,
            read: ReflectionHitLight
        );
        float[] still = LightOfASunlitPanel(jitter: "none", metallic: 1, drawsShowingIt: 1, read: ReflectionHitLight);

        AssertShareOf(jittered: jittered, still: still, share: 1f / 3, channels: 4);
    }

    /// <summary>
    /// Each pixel inside the panel holds <paramref name="share"/> of its light without jitter, which is above 0.01, in an
    /// image of <paramref name="channels"/> floats per pixel, whose first channel the test compares.
    /// </summary>
    private static void AssertShareOf(float[] jittered, float[] still, float share, int channels)
    {
        int compared = 0;
        foreach ((int x, int y) in InsideThePanel(PanelCenter))
        {
            int at = channels * (y * Width + x);
            still[at].Should().BeGreaterThan(0.01f, "the sun lights the panel at ({0}, {1})", x, y);
            jittered[at].Should().BeApproximately(share * still[at], 0.01f * still[at], "pixel ({0}, {1})", x, y);
            compared++;
        }
        compared.Should().BeGreaterThan(30, "the panel covers about 13 by 9 pixels");
    }

    /// <summary>The hit light's diffuse layer: four floats per pixel.</summary>
    private static float[] DiffuseHitLight(VulkanRenderer renderer) =>
        renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 1);

    /// <summary>The hit light's reflection layer: four floats per pixel.</summary>
    private static float[] ReflectionHitLight(VulkanRenderer renderer) =>
        renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 0);

    /// <summary>The guide's finest level: one float per pixel, the luminance of the light that the guide aims by.</summary>
    private static float[] GuideLight(VulkanRenderer renderer) => renderer.ReadLightPyramidLevel(0).Lights;

    /// <summary>
    /// What <paramref name="read"/> gives after a white panel of roughness 0.5 and <paramref name="metallic"/> joins a black
    /// wall in the sun in draw 2 and shows in <paramref name="drawsShowingIt"/> draws, under TERTIUS_DEBUG_JITTER at
    /// <paramref name="jitter"/>, with no ambient light.
    /// </summary>
    private static float[] LightOfASunlitPanel(
        string jitter,
        float metallic,
        int drawsShowingIt,
        Func<VulkanRenderer, float[]> read
    )
    {
        float[] light = [];
        WithJitter(
            jitter,
            () =>
            {
                VulkanRenderer renderer = Gpu.Headless(
                    width: Width,
                    height: Height,
                    screenTrace: ScreenTrace.Default,
                    warmUpFrames: 0
                );
                try
                {
                    RenderSnapshot scene = Gpu.Snapshot(Vector3.Normalize(new(0, 0.3f, 1)));
                    scene.Ambient.Intensity = 0;
                    MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
                    scene.Instances.Add(
                        Block(
                            box: box,
                            material: renderer.CreateMaterial(
                                MaterialDesc.Color(new(x: 0, y: 0, z: 0, w: 1), roughness: 1f)
                            ),
                            center: new(0, 0, -8.5f),
                            size: new(30, 20, 1)
                        )
                    );
                    Draw(renderer, scene, draws: 2);
                    scene.Instances.Add(
                        Gpu.Instance(
                            mesh: box,
                            material: renderer.CreateMaterial(
                                new(
                                    BaseColor: Vector4.One,
                                    Roughness: 0.5f,
                                    Model: new StandardShading(Metallic: metallic)
                                )
                            ),
                            position: PanelCenter,
                            scale: PanelSize,
                            radius: PanelSize.Length() / 2,
                            entity: PanelEntity,
                            chainDepth: TestChains.CamerasFrame
                        )
                    );
                    Draw(renderer, scene, draws: drawsShowingIt);
                    light = read(renderer);
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );
        return light;
    }

    [Fact]
    public void TheGuideAimsByTheSameShareOfTheFreshLightAsTheHitLight() =>
        WithJitter(
            "always",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = WallBehindAPanel();
                try
                {
                    Draw(renderer, scene, draws: 2);
                    scene.Instances.Add(Panel(renderer, PanelCenter));
                    Draw(renderer, scene, draws: 1);

                    (float[] guide, _, _) = renderer.ReadLightPyramidLevel(0);
                    foreach ((int x, int y) in InsideThePanel(PanelCenter))
                        guide[y * Width + x]
                            .Should()
                            .BeApproximately(
                                PanelGlow / 3,
                                1e-3f * PanelGlow,
                                "the gray glow's luminance, in the third draw"
                            );
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void ASurfaceSendsAllOfItsGlowFromItsSecondDraw() =>
        WithJitter(
            "always",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = WallBehindAPanel();
                try
                {
                    Draw(renderer, scene, draws: 3);
                    scene.Instances.Add(Panel(renderer, PanelCenter));
                    Draw(renderer, scene, draws: 2);

                    foreach (float sent in HitLightOfThePanel(renderer))
                        sent.Should().BeApproximately(PanelGlow, 1e-3f * PanelGlow, "the last draw showed the panel");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void ACameraCutsDrawSendsAllOfEveryPixelsFreshLight() =>
        WithJitter(
            "always",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = WallBehindAPanel();
                try
                {
                    Draw(renderer, scene, draws: 10);
                    scene.Instances.Add(Panel(renderer, PanelCenter));
                    scene.CameraCuts++;
                    Draw(renderer, scene, draws: 1);

                    foreach (float sent in HitLightOfThePanel(renderer))
                        sent.Should().BeApproximately(PanelGlow, 1e-3f * PanelGlow, "a reset draw's spread is 1");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    [Fact]
    public void WithoutJitterASurfaceSendsAllOfItsGlowInTheDrawThatFirstShowsIt() =>
        WithJitter(
            "none",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = WallBehindAPanel();
                try
                {
                    Draw(renderer, scene, draws: 5);
                    scene.Instances.Add(Panel(renderer, PanelCenter));
                    Draw(renderer, scene, draws: 1);

                    foreach (float sent in HitLightOfThePanel(renderer))
                        sent.Should().BeApproximately(PanelGlow, 1e-3f * PanelGlow, "a draw without jitter has P = 1");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    /// <summary>
    /// The surface test runs on every pixel that is not sky, an impostor's too, so an impostor sends its glow, which holds
    /// all of its light, by the same share. A small sunlit sphere 6 m ahead is an impostor at 640 × 360. It first shows in
    /// draw 3 of the reset, whose spread is 4, and the last draw showed it in draw 4.
    /// </summary>
    [Fact]
    public void AnImpostorInTheDrawThatFirstShowsItSendsOnePartInTheSpreadOfItsGlow() =>
        WithJitter(
            "always",
            () =>
            {
                const int width = 640,
                    height = 360;
                VulkanRenderer renderer = Gpu.Headless(
                    width: width,
                    height: height,
                    impostors: true,
                    screenTrace: ScreenTrace.Default,
                    warmUpFrames: 0
                );
                try
                {
                    RenderSnapshot scene = Gpu.Snapshot(Vector3.Normalize(new(0, 0.3f, 1)));
                    Draw(renderer, scene, draws: 3);
                    scene.Instances.Add(
                        Gpu.Instance(
                            mesh: renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                            material: renderer.CreateMaterial(
                                MaterialDesc.Color(new(x: 0.8f, y: 0.2f, z: 0.2f, w: 1f), roughness: 0.5f)
                            ),
                            position: new(0, 0, -6),
                            scale: new(0.3f),
                            entity: 9,
                            chainDepth: TestChains.CamerasFrame
                        )
                    );
                    Draw(renderer, scene, draws: 1);
                    float[] first = renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 1);
                    Draw(renderer, scene, draws: 1);
                    float[] second = renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 1);

                    renderer.LastFrameStats.Impostors.Should().BePositive("the small sphere is an impostor");
                    (int X, int Y) center = PixelOf(new(0, 0, -6), width, height);
                    int compared = 0;
                    for (int y = center.Y - 2; y <= center.Y + 2; y++)
                    for (int x = center.X - 2; x <= center.X + 2; x++)
                    {
                        int at = 4 * (y * width + x);
                        first[at]
                            .Should()
                            .BeApproximately(second[at] / 4, 0.02f * second[at], "pixel ({0}, {1})", x, y);
                        compared++;
                    }
                    compared.Should().Be(25);
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    /// <summary>
    /// The panel slides sideways by about one pixel per draw, from its last position to its new one. Its motion carries
    /// each inner pixel's surface to the pixel's last place, so the surface test holds there and the moving panel keeps
    /// all of its glow.
    /// </summary>
    [Fact]
    public void AMovingSurfaceSendsAllOfItsGlowInsideItsEdges() =>
        WithJitter(
            "always",
            () =>
            {
                (VulkanRenderer renderer, RenderSnapshot scene) = WallBehindAPanel();
                try
                {
                    Vector3 center = PanelCenter;
                    scene.Instances.Add(Panel(renderer, center));
                    Draw(renderer, scene, draws: 4);
                    for (int draw = 0; draw < 6; draw++)
                    {
                        RenderInstance panel = scene.Instances[^1];
                        panel.PreviousPosition = center;
                        center.X += 0.09f;
                        panel.Position = center;
                        scene.Instances[^1] = panel;
                        Draw(renderer, scene, draws: 1);
                    }

                    foreach (float sent in HitLightOfThePanel(renderer, center))
                        sent.Should()
                            .BeApproximately(PanelGlow, 1e-3f * PanelGlow, "the moving panel holds its history");
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    /// <summary>
    /// A white strip 0.06 pixels tall on screen hangs 5 m ahead under a sun from behind the eye, with the sky behind it. Its
    /// row shows the strip in one jitter phase of 8 and the sky in the others, so the strip's pixels never hold a history.
    /// A ceiling above, which the sun does not light, takes the strip's bounce. Over the 8 phases the strip deserves one
    /// draw in 8 of its light. A ceiling sample that reads its full light in the one draw that shows it is a speck that the
    /// accumulator cannot average away. Main's largest sample was 1.2 times the strip's full light.
    /// </summary>
    [Fact]
    public void NoCeilingSampleReadsMoreThanTwiceTheShareOfTheLightOfASunlitStripThinnerThanAPixel() =>
        WithJitter(
            "always",
            () =>
            {
                float fullLight = HitLightOfAThickStrip();
                (VulkanRenderer renderer, RenderSnapshot scene) = StripUnderACeiling(StripPixels);
                try
                {
                    Draw(renderer, scene, draws: Phases);
                    float largest = 0;
                    int stripDraws = 0;
                    for (int draw = 0; draw < 6 * Phases; draw++)
                    {
                        Draw(renderer, scene, draws: 1);
                        float[] samples = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0));
                        float[] distances = renderer.ReadDistance();
                        if (distances[StripRow * Width + Width / 2] > 0)
                            stripDraws++;
                        for (int y = 0; y < StripRow - 4; y++)
                        for (int x = 0; x < Width; x++)
                            if (distances[y * Width + x] > 0)
                                largest = Math.Max(largest, Luminance(samples, y * Width + x));
                    }

                    stripDraws.Should().Be(6, "the row shows the strip in one phase of 8");
                    largest.Should().BeLessThan(2 * fullLight / Phases, "the strip's full light is {0}", fullLight);
                }
                finally
                {
                    Gpu.DisposeAndAssertClean(renderer);
                }
            }
        );

    /// <summary>
    /// The diffuse hit light of the strip's middle pixel when the strip is 3 pixels tall, so that every draw shows it: its
    /// full fresh light.
    /// </summary>
    private static float HitLightOfAThickStrip()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = StripUnderACeiling(3);
        try
        {
            Draw(renderer, scene, draws: 4);
            float[] hit = renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 1);
            return Luminance(hit, StripRow * Width + Width / 2);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The pixel row that holds the strip, one below the middle of the view.</summary>
    private const int StripRow = Height / 2 - 1;

    /// <summary>The strip's distance ahead, in m.</summary>
    private const float StripDistance = 5;

    /// <summary>The strip's height on screen, in pixels: less than the 1/9 pixel between two phases' jitter.</summary>
    private const float StripPixels = 0.06f;

    /// <summary>
    /// The strip's center: on the place in <see cref="StripRow"/> where the first jitter phase's sample lies, so that the
    /// row shows it in that phase alone.
    /// </summary>
    private static Vector3 StripCenter =>
        new(0, HeightOfRow(StripRow + 0.5f + Fsr3Upscaler.JitterOffset(0, Phases).Y), -StripDistance);

    /// <summary>
    /// The height at <see cref="StripDistance"/> of the place <paramref name="row"/> pixels from the view's top.
    /// </summary>
    private static float HeightOfRow(float row) => (1 - 2 * row / Height) * MathF.Tan(Gpu.Fov / 2) * StripDistance;

    /// <summary>The height and depth in m of a strip <paramref name="pixels"/> tall on screen.</summary>
    private static float StripHeight(float pixels) => HeightOfRow(0) - HeightOfRow(pixels);

    /// <summary>
    /// The strip under the ceiling: a white strip 6 m wide and <paramref name="pixels"/> tall and deep on screen at
    /// <see cref="StripCenter"/>, under a sun of 30 behind the eye and 17° up, which lights its front and its top; and a
    /// white ceiling 1.2 m above the eye from
    /// 2 m to 9 m ahead, whose underside the sun does not light. No floor: the sky lies behind and below the strip.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) StripUnderACeiling(float pixels)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Gpu.Snapshot(Vector3.Normalize(new(0, 0.3f, 1)), starIrradiance: 30);
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 1f));
        scene.Instances.Add(
            Block(
                box: box,
                material: white,
                center: StripCenter,
                size: new(6, StripHeight(pixels), StripHeight(pixels))
            )
        );
        scene.Instances.Add(Block(box: box, material: white, center: new(0, 1.25f, -5.5f), size: new(20, 0.1f, 7)));
        return (renderer, scene);
    }

    /// <summary>
    /// A white wall 8 m ahead in the dark, a scene that the panel of <see cref="Panel"/> joins: only the panel's glow lights
    /// anything.
    /// </summary>
    private static (VulkanRenderer Renderer, RenderSnapshot Scene) WallBehindAPanel()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Dark();
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        scene.Instances.Add(
            Block(
                box: box,
                material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 1f)),
                center: new(0, 0, -8.5f),
                size: new(30, 20, 1)
            )
        );
        return (renderer, scene);
    }

    /// <summary>
    /// A black panel of reflectance 0 that glows <see cref="PanelGlow"/>, 1.2 m by 0.8 m, facing the eye at
    /// <paramref name="center"/>, as entity <see cref="PanelEntity"/>.
    /// </summary>
    private static RenderInstance Panel(VulkanRenderer renderer, Vector3 center) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
            material: Glow(renderer, new(PanelGlow)),
            position: center,
            scale: PanelSize,
            radius: PanelSize.Length() / 2,
            entity: PanelEntity,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>The pixels at least two pixels inside the edges of the panel at <paramref name="center"/>.</summary>
    private static IEnumerable<(int X, int Y)> InsideThePanel(Vector3 center)
    {
        Vector2 low = Gpu.Project(point: center - (PanelSize / 2) with { Z = 0 }, width: Width, height: Height);
        Vector2 high = Gpu.Project(point: center + (PanelSize / 2) with { Z = 0 }, width: Width, height: Height);
        Vector2 least = Vector2.Min(low, high),
            most = Vector2.Max(low, high);
        for (int y = (int)least.Y + 2; y < (int)most.Y - 2; y++)
        for (int x = (int)least.X + 2; x < (int)most.X - 2; x++)
            yield return (x, y);
    }

    /// <summary>
    /// The diffuse hit light of each pixel inside the panel at <paramref name="center"/>, by default
    /// <see cref="PanelCenter"/>, after it checked that both kinds hold the same light, as a black panel sends.
    /// </summary>
    private static List<float> HitLightOfThePanel(VulkanRenderer renderer, Vector3? center = null)
    {
        float[] reflection = renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 0);
        float[] diffuse = renderer.ReadImageLayer(renderer.ScreenTraceImages.HitLight, 1);
        var sent = new List<float>();
        foreach ((int x, int y) in InsideThePanel(center ?? PanelCenter))
        {
            int at = 4 * (y * Width + x);
            reflection[at]
                .Should()
                .BeApproximately(diffuse[at], 1e-3f * PanelGlow, "the black panel sends its glow alone");
            sent.Add(diffuse[at]);
        }
        sent.Count.Should().BeGreaterThan(30, "the panel covers about 13 by 9 pixels");
        return sent;
    }

    private static float Luminance(float[] rgba, int pixel) =>
        0.2126f * rgba[4 * pixel] + 0.7152f * rgba[4 * pixel + 1] + 0.0722f * rgba[4 * pixel + 2];

    /// <summary>Draws <paramref name="draws"/> draws of <paramref name="scene"/>, each one tick after the last.</summary>
    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene, int draws)
    {
        for (int draw = 0; draw < draws; draw++)
        {
            renderer.Render(scene, 1f);
            scene.Time += Tick;
        }
    }

    /// <summary>
    /// Runs <paramref name="test"/> with TERTIUS_DEBUG_JITTER at <paramref name="setting"/>, read when the renderer is made.
    /// </summary>
    private static void WithJitter(string setting, Action test)
    {
        string? previous = Environment.GetEnvironmentVariable(JitterSwitch);
        Environment.SetEnvironmentVariable(JitterSwitch, setting);
        try
        {
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(JitterSwitch, previous);
        }
    }
}

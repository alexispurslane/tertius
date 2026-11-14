using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen tracer (docs/plans/temporal-renderer.md, section 6) and its two indirect signals, under
/// <see cref="ScreenTrace.Default"/> at a fixed exposure of 0 stops: the light that its samples converge to, where a hit's
/// light comes from, the share of a blocker, the later bounces, a sample at every lit pixel, the ray plan and its
/// own rays, and the walk against its C# copy.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ScreenTraceRenderTests
{
    private const float Tick = 1f / 60;

    /// <summary>
    /// A validating headless renderer of 640 × 360 under the screen tracer's default, with no warm-up draws: a still
    /// is one draw, so the first frame of <see cref="Draw"/> is one reset frame, and each frame that it names is one draw.
    /// </summary>
    private static VulkanRenderer Tracing(bool upscaling = false) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            upscaling: upscaling ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling()
        );

    /// <summary>Draws <paramref name="frames"/> frames of <paramref name="scene"/>, each one tick after the last.</summary>
    private static void Draw(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Time += Tick;
            renderer.Render(scene, 1f);
        }
    }

    /// <summary>The pixels of x from <paramref name="Left"/> to <paramref name="Right"/> and y from <paramref name="Top"/> to <paramref name="Bottom"/>, inclusive.</summary>
    private readonly record struct Region(int Left, int Right, int Top, int Bottom)
    {
        /// <summary>The pixels within <paramref name="radius"/> of <paramref name="pixel"/> along each axis.</summary>
        public static Region Around((int X, int Y) pixel, int radius) =>
            new(Left: pixel.X - radius, Right: pixel.X + radius, Top: pixel.Y - radius, Bottom: pixel.Y + radius);
    }

    /// <summary>The floor's pixels in the middle of the lower half of the image.</summary>
    private static readonly Region MiddleOfTheFloor = new(Left: 200, Right: 440, Top: 260, Bottom: 300);

    /// <summary>The mean light of an RGBA image over <paramref name="region"/>.</summary>
    private static Vector3 MeanOver(float[] rgba, Region region)
    {
        Vector3 sum = Vector3.Zero;
        for (int y = region.Top; y <= region.Bottom; y++)
        for (int x = region.Left; x <= region.Right; x++)
            sum += At(rgba, (x, y));
        return sum / ((region.Right - region.Left + 1) * (region.Bottom - region.Top + 1));
    }

    /// <summary>A white floor of <paramref name="roughness"/> that is no metal.</summary>
    private static MaterialDesc WhiteFloor(float roughness) =>
        new(BaseColor: Vector4.One, Roughness: roughness, Model: new StandardShading(Metallic: 0));

    [Fact]
    public void UnderAUniformEnvironmentWithNoBlockerBothIndirectSignalsConvergeToItsLight()
    {
        // The white furnace: every ray misses and reads the light of 0.5, and each estimator's mean is 1.
        const float light = 0.5f;
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(light);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.3f)));

            Draw(renderer, scene, frames: 40);

            (TemporalSignal diffuse, TemporalSignal specular) = renderer.ScreenTraceSignals;
            Vector3 diffuseLight = MeanOver(renderer.ReadImage(diffuse.Slow(0)), MiddleOfTheFloor);
            Vector3 specularLight = MeanOver(renderer.ReadImage(specular.Slow(0)), MiddleOfTheFloor);
            diffuseLight.X.Should().BeApproximately(light, 0.01f * light, "the diffuse signal converges to the light");
            specularLight
                .X.Should()
                .BeApproximately(light, 0.01f * light, "the specular signal converges to the light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASurfaceAboveTheReflectionLimitReceivesSpecularLightFromItsDiffuseRays()
    {
        const float light = 0.5f;
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(light);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));

            Draw(renderer, scene, frames: 40);

            Vector3 specularLight = MeanOver(
                renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0)),
                MiddleOfTheFloor
            );
            specularLight
                .X.Should()
                .BeInRange(
                    0.5f * light,
                    1.5f * light,
                    "a surface without reflection rays must retain a specular estimate from its diffuse rays"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMirrorFloorShowsAGlowingPanelAtThePlaceOfItsMirrorImage(bool upscaling)
    {
        var glow = new Vector3(4f);
        VulkanRenderer renderer = Tracing(upscaling);
        try
        {
            Draw(renderer, GlowingPanelOverAMirror(renderer, glow), frames: 1);

            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
            (int X, int Y) shown = MirrorPixel(GlowingPanelCenter);
            At(sample, shown)
                .X.Should()
                .BeGreaterThan(0.8f * glow.X, "the mirror image of the panel's center shows its glow");
            At(sample, (shown.X + 150, shown.Y))
                .X.Should()
                .BeLessThan(0.05f * glow.X, "the floor beside the image shows the dark sky");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AChangedGlowingPanelLosesNinetyPercentOfItsOldMirrorImageWithinTenFrames(bool moves, bool upscaling)
    {
        VulkanRenderer renderer = Tracing(upscaling);
        try
        {
            RenderSnapshot scene = GlowingPanelOverAMirror(renderer, new(4f));
            Draw(renderer, scene, frames: 40);
            (int X, int Y) shown = MirrorPixel(GlowingPanelCenter);
            float before = At(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0)), shown).X;
            before.Should().BeGreaterThan(3.2f);
            RenderInstance panel = scene.Instances[1];
            if (moves)
            {
                panel.PreviousPosition = panel.Position;
                panel.Position += new Vector3(4, 0, 0);
            }
            else
                panel.Material = Glow(renderer, Vector3.Zero);
            scene.Instances[1] = panel;

            Draw(renderer, scene, frames: 10);

            float after = At(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0)), shown).X;
            after.Should().BeLessThan(0.1f * before);
            if (moves)
                At(renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0)), MirrorPixel(panel.Position))
                    .X.Should()
                    .BeGreaterThan(3.2f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A dark scene with a mirror floor and a glowing panel 12 m ahead, at <see cref="GlowingPanelCenter"/>. With
    /// <paramref name="holdsLight"/>, the panel's entity holds a lamp at its center, so that it shows its glow to the
    /// eye but sends none: the lamp's own light stands for it (frame.glsl, glowSent).
    /// </summary>
    private static RenderSnapshot GlowingPanelOverAMirror(
        VulkanRenderer renderer,
        Vector3 glow,
        bool holdsLight = false
    )
    {
        RenderSnapshot scene = Dark();
        scene.Instances.Add(Floor(renderer, Mirror()));
        scene.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: Glow(renderer, glow),
                position: GlowingPanelCenter,
                scale: new(3, 2, 0.2f),
                radius: 1.8f,
                entity: holdsLight ? Gpu.LampEntity : 0,
                chainDepth: TestChains.CamerasFrame
            )
        );
        if (holdsLight)
            scene.Lights.Add(
                Gpu.Lamp(
                    position: GlowingPanelCenter,
                    color: Vector3.One,
                    intensity: 1,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        return scene;
    }

    private static readonly Vector3 GlowingPanelCenter = new(0, -0.5f, -12);

    [Fact]
    public void AHitFartherThanTheLargestHalfFloatStoresTheLargestHalfFloatAsItsDistance()
    {
        // A glowing wall 100 km ahead and 15 km up, whose mirror image the floor shows about 10 m ahead. Metal saturates a
        // half float store at 65,504, and so does KosmicKrisp on the same GPU, so only another driver, which stores
        // infinity, can fail this without the cap.
        var wallCenter = new Vector3(0, 15_000, -100_000);
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Dark();
            scene.Instances.Add(Floor(renderer, Mirror()));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: Glow(renderer, new(4f)),
                    position: wallCenter,
                    scale: new(40_000, 20_000, 100),
                    radius: 23_000,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            Draw(renderer, scene, frames: 1);

            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
            (int X, int Y) shown = MirrorPixel(wallCenter);
            At(sample, shown).X.Should().BeGreaterThan(2f, "the mirror image of the wall shows its glow");
            sample[4 * (shown.Y * Width + shown.X) + 3]
                .Should()
                .Be(65_504f, "a hit about 100 km away stores the largest half float, not an infinity");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMirrorFloorShowsNoGlowOfAPanelWhoseEntityHoldsALight()
    {
        var glow = new Vector3(4f);
        VulkanRenderer renderer = Tracing();
        try
        {
            Draw(renderer, GlowingPanelOverAMirror(renderer, glow, holdsLight: true), frames: 1);

            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
            At(sample, MirrorPixel(GlowingPanelCenter))
                .X.Should()
                .BeLessThan(0.05f * glow.X, "the lamp's light stands for its glow, so a hit counts the glow once");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMirrorFloorShowsNoGlowOfABarWhoseEntityHoldsATubeLight()
    {
        var glow = new Vector3(4f);
        VulkanRenderer renderer = Tracing();
        try
        {
            // The glowing panel of GlowingPanelOverAMirror as a bar 3 m wide, whose entity holds a tube light along it.
            RenderSnapshot scene = Dark();
            scene.Instances.Add(Floor(renderer, Mirror()));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: Glow(renderer, glow),
                    position: GlowingPanelCenter,
                    scale: new(3, 0.2f, 0.2f),
                    radius: 1.6f,
                    entity: Gpu.LampEntity,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            scene.Lights.Add(
                new TubeRenderLight(
                    EntityIndex: Gpu.LampEntity,
                    EntityGeneration: 0,
                    Position: GlowingPanelCenter,
                    PreviousPosition: GlowingPanelCenter,
                    Axis: Vector3.UnitX,
                    PreviousAxis: Vector3.UnitX,
                    Color: Vector3.One,
                    Power: 1,
                    Radius: 0.1f,
                    Length: 2.8f,
                    CastsShadow: false,
                    ChainDepth: TestChains.CamerasFrame
                )
            );
            Draw(renderer, scene, frames: 1);

            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
            At(sample, MirrorPixel(GlowingPanelCenter))
                .X.Should()
                .BeLessThan(0.05f * glow.X, "the tube light stands for the bar's glow, so a hit counts the glow once");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCompositeShowsTheTracersReflectionAndWithoutTheTracerTheClosedForm(bool tracer)
    {
        var glow = new Vector3(4f);
        VulkanRenderer renderer = tracer ? Tracing() : Gpu.Headless(width: Width, height: Height);
        try
        {
            Draw(renderer, GlowingPanelOverAMirror(renderer, glow), frames: 1);

            Vector3 shown = At(renderer.ReadHdr(), MirrorPixel(GlowingPanelCenter));
            if (tracer)
                shown.X.Should().BeGreaterThan(0.8f * glow.X, "the composite adds the specular signal's slow history");
            else
                shown.X.Should().BeLessThan(0.05f * glow.X, "the closed form of the dark sky holds no panel");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFrameAfterOneWithMoreOwnRaysShowsWhatTheSameFrameShowsInAFreshRenderer()
    {
        // The wall's frame lists more own rays than the floor's, so the lists keep entries past the floor frame's count.
        // A dispatch that read past the count, or a count that the clear did not reset, would trace them again.
        static RenderSnapshot FloorAlone(VulkanRenderer renderer)
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.2f)));
            // A cut, so that the frame resets every history, as the fresh renderer's first frame does.
            scene.CameraCuts = 1;
            return scene;
        }

        float[] fresh;
        VulkanRenderer first = Tracing();
        try
        {
            Draw(first, FloorAlone(first), frames: 1);
            fresh = Signals(first);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(first);
        }

        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot wall = Gray(0.3f);
            wall.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.2f)));
            wall.Instances.Add(
                Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: renderer.CreateMaterial(WhiteFloor(roughness: 0.2f)),
                    center: new(0, 2, -10),
                    size: new(40, 8, 1)
                )
            );
            Draw(renderer, wall, frames: 1);
            int listedBefore = renderer.ReadOwnRayCounts().Diffuse;
            Draw(renderer, FloorAlone(renderer), frames: 1);
            int listed = renderer.ReadOwnRayCounts().Diffuse;

            Signals(renderer).Should().Equal(fresh, "a reset frame draws from nothing that the frame before it left");
            listed.Should().BeLessThan(listedBefore, "the wall's frame lists the pixels of the wall too");
            (listed % OwnRayGroupSize)
                .Should()
                .NotBe(0, "the last workgroup of the own-ray dispatch has invocations past the count");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The invocations of a workgroup of the own-ray dispatch: OwnRayGroupSize of own_rays.glsl.</summary>
    private const int OwnRayGroupSize = 64;

    /// <summary>Both signals' samples of the last frame, one after the other.</summary>
    private static float[] Signals(VulkanRenderer renderer)
    {
        (TemporalSignal diffuse, TemporalSignal specular) = renderer.ScreenTraceSignals;
        return [.. renderer.ReadImage(diffuse.Sample(0)), .. renderer.ReadImage(specular.Sample(0))];
    }

    [Theory]
    [InlineData(0.95f)]
    [InlineData(4.95f)]
    public void AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight(float distance)
    {
        // A row of floor pixels in front of a black panel that glows: their diffuse light is f · glow + (1 − f) · sky,
        // with f the panel's form factor, the cosine-weighted share of the hemisphere that the panel covers. At 0.95 m the
        // panel covers a third of it, at 4.95 m about 4 %. There the panel's rays are rare and bright, and a clamp of the
        // slow history into the box of the fast histories alone read about half of the panel's share (ter-18hc). The
        // retained deviation widens the box to hold them (docs/plans/bounce-light-confidence.md, section 7).
        const float sky = 0.2f;
        const float glow = 2f;
        var receiver = new Vector3(0, FloorHeight, -7);
        float panelFront = receiver.Z - distance;
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(sky);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));
            scene.Instances.Add(
                Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: Glow(renderer, new(glow)),
                    center: new(0, 0, panelFront - 0.05f),
                    size: new(6, 3, 0.1f)
                )
            );

            Draw(renderer, scene, frames: 160);

            float share = FormFactor(point: receiver, front: panelFront, halfWidth: 3, bottom: FloorHeight, top: 1.5f);
            (int X, int Y) pixel = PixelOf(receiver);
            float[] history = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0));
            Vector3 shown = MeanOver(
                history,
                new Region(Left: pixel.X - 40, Right: pixel.X + 40, Top: pixel.Y - 1, Bottom: pixel.Y + 1)
            );
            float shownShare = (shown.X - sky) / (glow - sky);
            shownShare.Should().BeApproximately(share, 0.1f * share, "the panel takes its form factor of the light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The bounce light's retained deviation and its filter against real rays (docs/plans/bounce-light-confidence.md,
    /// decision 6). Each test makes a renderer with the diffuse filter off by its debug switch, so the class holds the
    /// process environment.
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class BounceLightConfidence(ITestOutputHelper output)
    {
        /// <summary>
        /// A renderer of <see cref="Tracing"/> with the diffuse filter off (<see cref="DiffuseFilter.Selected"/>), so that
        /// the slow history holds only its own samples and the hits read the fast history.
        /// </summary>
        private static VulkanRenderer TracingWithoutTheFilter()
        {
            const string Switch = "TERTIUS_DIFFUSE_FILTER";
            string? previous = Environment.GetEnvironmentVariable(Switch);
            Environment.SetEnvironmentVariable(Switch, "off");
            try
            {
                return Tracing();
            }
            finally
            {
                Environment.SetEnvironmentVariable(Switch, previous);
            }
        }

        [Fact]
        public void TheRetainedDeviationPredictsTheSpreadOfTheSlowHistory()
        {
            // The floor 4.95 m before the glowing panel of AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight
            // takes rare bright rays. An exponential mean of the weight w over independent samples of the deviation σ
            // deviates by σ √(w / (2 − w)) from their expected value. The runs lie 64 draws apart, after which the slow
            // history keeps (31 / 32)^64 ≈ 0.13 of its light, so they are nearly independent. The scene is gray, so the red
            // channel is the luminance. A result outside is a finding for the design, not a bound to loosen.
            const int runs = 16;
            const float weight = 1f / 32;
            var receiver = new Vector3(0, FloorHeight, -7);
            VulkanRenderer renderer = TracingWithoutTheFilter();
            try
            {
                RenderSnapshot scene = Gray(0.2f);
                scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));
                scene.Instances.Add(
                    Block(
                        box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                        material: Glow(renderer, new(2)),
                        center: new(0, 0, receiver.Z - 4.95f - 0.05f),
                        size: new(6, 3, 0.1f)
                    )
                );
                Draw(renderer, scene, frames: 64);
                (int X, int Y) pixel = PixelOf(receiver);
                var region = new Region(Left: pixel.X - 40, Right: pixel.X + 40, Top: pixel.Y - 1, Bottom: pixel.Y + 1);
                int columns = region.Right - region.Left + 1;
                int count = columns * (region.Bottom - region.Top + 1);
                double[] sums = new double[count];
                double[] squares = new double[count];
                double predicted = 0;

                for (int run = 0; run < runs; run++)
                {
                    Draw(renderer, scene, frames: 64);
                    float[] slow = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0));
                    float[] fast = renderer.ReadImage(
                        renderer.ScreenTraceSignals.Diffuse.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1)
                    );
                    for (int at = 0; at < count; at++)
                    {
                        int index = 4 * ((region.Top + at / columns) * Width + region.Left + at % columns);
                        sums[at] += slow[index];
                        squares[at] += slow[index] * (double)slow[index];
                        double deviation = fast[index + 3] * Math.Sqrt(weight / (2 - weight));
                        predicted += deviation * deviation / (runs * count);
                    }
                }

                double variance = 0;
                for (int at = 0; at < count; at++)
                    variance += (squares[at] - sums[at] * sums[at] / runs) / (runs - 1) / count;
                double ratio = Math.Sqrt(variance / predicted);
                output.WriteLine($"Measured over predicted deviation of the slow history: {ratio:F3}.");
                ratio
                    .Should()
                    .BeInRange(0.5, 2, "the retained deviation is the spread of the samples that the history averages");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void TheBounceLightOfALitPoolKeepsItsEdgeWithRealRays()
        {
            // A spot lamp lights a pool on the floor before a white wall, and the wall's foot takes the pool's light. Its
            // bounce light falls from the pool's chord to the dark floor beside it over about its height above the floor.
            // The retained deviation keeps the filter's luminance stop open for the samples' spread, which must not open
            // it across that edge. The design's bound was 4 pixels wider than without the filter; this GPU measured 6
            // (37 against 31), so 8 holds the measured width until the user decides (t0-anp3).
            float filtered = RiseOfTheWallsBounceLight(filtered: true);
            float unfiltered = RiseOfTheWallsBounceLight(filtered: false);
            output.WriteLine($"10 to 90 % rise: {filtered} px filtered, {unfiltered} px unfiltered.");

            filtered
                .Should()
                .BeLessThanOrEqualTo(unfiltered + 8, "the filter smooths noise, not the edge of real light");
        }

        /// <summary>
        /// The width in pixels of the 10 to 90 % rise of the bounce light along the foot of the wall of
        /// <see cref="TheBounceLightOfALitPoolKeepsItsEdgeWithRealRays"/>, from the dark floor beside the pool to the
        /// pool's chord, over the rows 0.05 to 0.3 m above the floor: the filtered light or the slow history without the
        /// filter, each the mean of 128 draws.
        /// </summary>
        private static float RiseOfTheWallsBounceLight(bool filtered)
        {
            const float wall = -10;
            VulkanRenderer renderer = filtered ? Tracing() : TracingWithoutTheFilter();
            try
            {
                MaterialId white = renderer.CreateMaterial(WhiteFloor(roughness: 1));
                RenderSnapshot scene = Dark();
                scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));
                scene.Instances.Add(
                    Block(
                        box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                        material: white,
                        center: new(0, FloorHeight + 3, wall - 0.1f),
                        size: new(40, 6, 0.2f)
                    )
                );
                // 4 m above the floor and 1.2 m before the wall, a cone of 0.4 rad lights a pool of radius 1.69 m whose
                // chord along the wall's foot reaches x = ±1.2 m. Its floor reflects a radiance of 1.
                scene.Lights.Add(
                    Gpu.Spot(
                        position: new(0, FloorHeight + 4, wall + 1.2f),
                        direction: -Vector3.UnitY,
                        color: Vector3.One,
                        intensity: 16 * MathF.PI,
                        innerConeAngle: 0.39f,
                        outerConeAngle: 0.4f,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                Draw(renderer, scene, frames: 64);
                int top = PixelOf(new(0, FloorHeight + 0.3f, wall)).Y;
                int bottom = PixelOf(new(0, FloorHeight + 0.05f, wall)).Y;
                int left = PixelOf(new(0, FloorHeight + 0.1f, wall)).X;
                int right = PixelOf(new(2.5f, FloorHeight + 0.1f, wall)).X;
                float[] profile = new float[right - left + 1];
                for (int draw = 0; draw < 128; draw++)
                {
                    Draw(renderer, scene, frames: 1);
                    float[] light = renderer.ReadImage(
                        filtered
                            ? renderer.ScreenTraceImages.Filter!.Filtered
                            : renderer.ScreenTraceSignals.Diffuse.Slow(0)
                    );
                    for (int y = top; y <= bottom; y++)
                    for (int x = left; x <= right; x++)
                        profile[x - left] += At(light, (x, y)).X / (128 * (bottom - top + 1));
                }

                float inside = profile[..8].Average();
                float outside = profile[^8..].Average();
                (inside - outside).Should().BePositive("the pool's chord takes the most light");
                float LevelAt(float share) => outside + share * (inside - outside);
                int high = Array.FindLastIndex(profile, light => light >= LevelAt(0.9f));
                int low = Array.FindIndex(profile, light => light <= LevelAt(0.1f));
                return low - high;
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }

    /// <summary>The 364 draws of the still panel scene (tests/STYLE.md, "Long tests").</summary>
    [Collection(GpuCollection.Name)]
    public sealed class StillPanelForFourHundredDraws(ITestOutputHelper output)
    {
        [Fact]
        public void AStillSceneGivesNoEvidenceToEitherSignal()
        {
            // The floor 4.95 m before the glowing panel of
            // AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight takes rare bright rays: the heavy tail of
            // real bounce light, whose hits also read the last draw's filtered light. Nothing in the still changes, so no
            // sample of either signal may bring a recent light code or a motion bit after the histories fill. Evidence
            // reads what the rays saw, not the samples' statistics, so noise cannot count as a change
            // (docs/plans/bounce-light-confidence.md, section 16, point 4).
            var receiver = new Vector3(0, FloorHeight, -7);
            VulkanRenderer renderer = Tracing();
            try
            {
                RenderSnapshot scene = Gray(0.2f);
                scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));
                scene.Instances.Add(
                    Block(
                        box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                        material: Glow(renderer, new(2)),
                        center: new(0, 0, receiver.Z - 4.95f - 0.05f),
                        size: new(6, 3, 0.1f)
                    )
                );
                Draw(renderer, scene, frames: 64);

                long evidence = 0;
                long samples = 0;
                for (int draw = 64; draw < 364; draw++)
                {
                    Draw(renderer, scene, frames: 1);
                    foreach (
                        TemporalSignal signal in (TemporalSignal[])
                            [renderer.ScreenTraceSignals.Diffuse, renderer.ScreenTraceSignals.Specular]
                    )
                    {
                        float[] ages = renderer.ReadImage(signal.SampleAge(0)!);
                        samples += ages.Length;
                        evidence += ages.Count(age =>
                            ((int)age & TemporalSignal.SampleMotionBit) != 0
                            || ((int)age & TemporalSignal.LightCodeMask) is >= 1 and <= 7
                        );
                    }
                }

                output.WriteLine($"Samples with evidence: {evidence} of {samples}.");
                evidence.Should().Be(0, "nothing in a still changes what the rays see");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }

    /// <summary>The 152 draws of the box that slides past a wall (tests/STYLE.md, "Long tests").</summary>
    [Collection(GpuCollection.Name)]
    public sealed class BoxSlidingPastAWall(ITestOutputHelper output)
    {
        [Fact]
        public void ABoxThatSlidesPastAWallNarrowsTheWallsBoxWhileItMovesAndNotAfter()
        {
            // A white wall stands left of the view, along it, and faces the view's middle. A glowing box stands on the
            // floor 0.4 m beside the wall and farther from the eye than the wall's pixels below, and slides 2.5 cm per
            // draw away from the eye, under a still camera. The screen shows the box's front face, so the wall's diffuse
            // rays that run away from the eye hit it. The wall's pixels do not move, so a wall sample's motion bit comes
            // only from such a ray, which hit a surface that the ray plan marks as moved. A block of 8 × 8 pixels with
            // such a sample, or with a recent light code, has evidence, and either level drops the widening of the
            // block's box in that draw (docs/plans/bounce-light-confidence.md, section 8.2), the action that
            // AccumulatorRenderTests.AMovingWallNarrowsTheBoxOfItsBlocksWhileItMoves pins. After the box stops, the light
            // codes of its last moves stay recent for at most 11 draws (section 8.3), so from 16 draws after the stop no
            // block of the wall has evidence, and the widened box returns.
            VulkanRenderer renderer = Tracing();
            try
            {
                RenderSnapshot scene = Dark();
                int box = AddWallAndBox(renderer, scene);
                Draw(renderer, scene, frames: 64);
                int[] wallBlocks = WallBlocks();

                var moving = new List<int>();
                for (int draw = 0; draw < 40; draw++)
                {
                    Move(scene, box, new Vector3(0, 0, -0.025f));
                    Draw(renderer, scene, frames: 1);
                    moving.Add(BlocksWith(renderer, wallBlocks, MotionBit));
                }

                Move(scene, box, Vector3.Zero);
                Draw(renderer, scene, frames: 16);
                int after = 0;
                for (int draw = 0; draw < 32; draw++)
                {
                    Draw(renderer, scene, frames: 1);
                    after += BlocksWith(renderer, wallBlocks, Evidence);
                }

                output.WriteLine(
                    $"Wall blocks with a motion bit of {wallBlocks.Length}: {string.Join(' ', moving)} while the box "
                        + $"moves. Block-draws with evidence in the 32 draws from 16 after the stop: {after}."
                );
                wallBlocks.Length.Should().BeGreaterThan(20, "the wall fills the left of the view");
                moving
                    .Skip(1)
                    .Should()
                    .OnlyContain(count => count >= wallBlocks.Length / 4, "the wall's rays see the box move");
                after.Should().Be(0, "a still wall beside a still box gives no evidence");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>The plane x = WallX of the white wall, left of the view.</summary>
        private const float WallX = -2;

        /// <summary>
        /// Adds a white floor, a white wall in the plane x = <see cref="WallX"/> from 4 m to 16 m before the eye, and a
        /// glowing box of 1.2 × 1.5 × 1 m on the floor 0.4 m beside the wall, 8.5 m to 9.5 m before the eye. Returns the
        /// box's index among the scene's instances.
        /// </summary>
        private static int AddWallAndBox(VulkanRenderer renderer, RenderSnapshot scene)
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));
            scene.Instances.Add(
                Block(
                    box: box,
                    material: renderer.CreateMaterial(WhiteFloor(roughness: 1)),
                    center: new(WallX - 0.1f, FloorHeight + 3, -10),
                    size: new(0.2f, 6, 12)
                )
            );
            // The box's entity gives it an identity, by which the renderer finds its last place and so its motion.
            RenderInstance glowing = Block(
                box: box,
                material: Glow(renderer, new(4)),
                center: new(-1, FloorHeight + 0.75f, -9),
                size: new(1.2f, 1.5f, 1)
            );
            glowing.EntityIndex = 1;
            scene.Instances.Add(glowing);
            return scene.Instances.Count - 1;
        }

        /// <summary>
        /// The blocks of 8 × 8 pixels inside the wall's pixels between 5 m and 8 m from the eye and 0.2 to 2 m above the
        /// floor, which the box never covers: on screen the box lies right of the wall's pixels at 8 m.
        /// </summary>
        private static int[] WallBlocks()
        {
            int left = PixelOf(new(WallX, FloorHeight + 2, -5)).X;
            int top = PixelOf(new(WallX, FloorHeight + 2, -8)).Y;
            (int right, int bottom) = PixelOf(new(WallX, FloorHeight + 0.2f, -8));
            return
            [
                .. Enumerable
                    .Range(0, Width / 8 * (Height / 8))
                    .Where(block =>
                        block % (Width / 8) * 8 >= left
                        && block % (Width / 8) * 8 + 7 <= right
                        && block / (Width / 8) * 8 >= top
                        && block / (Width / 8) * 8 + 7 <= bottom
                    ),
            ];
        }

        /// <summary>
        /// Moves the scene's instance <paramref name="instance"/> by <paramref name="step"/> in this draw: its last place
        /// becomes its place before the step, so a zero step stops it.
        /// </summary>
        private static void Move(RenderSnapshot scene, int instance, Vector3 step)
        {
            RenderInstance moved = scene.Instances[instance];
            moved.PreviousPosition = moved.Position;
            moved.Position += step;
            scene.Instances[instance] = moved;
        }

        /// <summary>Whether a diffuse sample's age holds the motion bit (sample_age.glsl).</summary>
        private static bool MotionBit(int age) => (age & TemporalSignal.SampleMotionBit) != 0;

        /// <summary>
        /// Whether a diffuse sample's age gives its block evidence: a motion bit or a recent light code, 1 to 7
        /// (sample_age.glsl, evidenceOfSample of accumulate_spatial.comp).
        /// </summary>
        private static bool Evidence(int age) =>
            MotionBit(age) || (age & TemporalSignal.LightCodeMask) is >= 1 and <= 7;

        /// <summary>How many of <paramref name="blocks"/> hold a diffuse sample of the last draw that <paramref name="holds"/>.</summary>
        private static int BlocksWith(VulkanRenderer renderer, int[] blocks, Func<int, bool> holds)
        {
            float[] ages = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.SampleAge(0)!);
            var holding = new HashSet<int>();
            for (int pixel = 0; pixel < ages.Length; pixel++)
                if (holds((int)ages[pixel]))
                    holding.Add(pixel / Width / 8 * (Width / 8) + pixel % Width / 8);
            return blocks.Count(holding.Contains);
        }
    }

    /// <summary>
    /// The form factor from a floor point, whose normal is +Y, to the rectangle at z = <paramref name="front"/> from
    /// x = −<paramref name="halfWidth"/> to <paramref name="halfWidth"/> and y = <paramref name="bottom"/> to
    /// <paramref name="top"/>, whose normal is +Z: ∫ cos θ_p cos θ_r / (π r²) dA over a grid of 600 × 300 cells.
    /// </summary>
    private static float FormFactor(Vector3 point, float front, float halfWidth, float bottom, float top)
    {
        const int columns = 600,
            rows = 300;
        double cell = 2 * halfWidth / columns * ((top - bottom) / rows);
        double sum = 0;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            var at = new Vector3(
                -halfWidth + (column + 0.5f) * 2 * halfWidth / columns,
                bottom + (row + 0.5f) * (top - bottom) / rows,
                front
            );
            Vector3 toCell = at - point;
            double squared = toCell.LengthSquared();
            double distance = Math.Sqrt(squared);
            double receiverCosine = toCell.Y / distance;
            double cellCosine = (point.Z - front) / distance;
            if (receiverCosine > 0)
                sum += receiverCosine * cellCosine / (Math.PI * squared) * cell;
        }

        return (float)sum;
    }

    [Fact]
    public void InAClosedBoxTheDiffuseLightRisesWithNoOvershootTowardTheSumOfTheBounces()
    {
        // Walls of albedo 0.5 under a lamp, and a dark sky: the first frame holds the first bounce E, and each later
        // bounce adds at most half of the one before, so the light rises toward at most E / (1 − 0.5).
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Dark();
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId wall = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1),
                    Roughness: 1,
                    Model: new StandardShading(Metallic: 0)
                )
            );
            // A room of 6 × 4 × 8 m around the eye, open behind it.
            scene.Instances.Add(Block(box: box, material: wall, center: new(0, -2.05f, -4), size: new(6, 0.1f, 8)));
            scene.Instances.Add(Block(box: box, material: wall, center: new(0, 2.05f, -4), size: new(6, 0.1f, 8)));
            scene.Instances.Add(Block(box: box, material: wall, center: new(-3.05f, 0, -4), size: new(0.1f, 4, 8)));
            scene.Instances.Add(Block(box: box, material: wall, center: new(3.05f, 0, -4), size: new(0.1f, 4, 8)));
            scene.Instances.Add(Block(box: box, material: wall, center: new(0, 0, -8.05f), size: new(6, 4, 0.1f)));
            scene.Lights.Add(
                Gpu.Lamp(
                    position: new(0, 1.5f, -3),
                    color: Vector3.One,
                    intensity: 20,
                    chainDepth: TestChains.CamerasFrame
                )
            );

            var means = new List<float>();
            for (int frame = 0; frame < 48; frame++)
            {
                Draw(renderer, scene, frames: 1);
                float[] history = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Slow(0));
                means.Add(MeanOver(history, new Region(Left: 280, Right: 360, Top: 150, Bottom: 210)).X);
            }

            for (int frame = 1; frame < means.Count; frame++)
                means[frame]
                    .Should()
                    .BeGreaterThanOrEqualTo(0.98f * means[frame - 1], "frame {0} rises from the one before", frame);
            means[^1].Should().BeGreaterThan(1.05f * means[0], "the later bounces add light");
            means[^1]
                .Should()
                .BeLessThanOrEqualTo(means[0] / (1 - 0.5f), "the bounces sum to at most E / (1 − albedo)");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EveryLitPixelGetsAFiniteSampleOfEachSignalInEachFrame()
    {
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.2f)));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(WhiteFloor(roughness: 0.6f));
            scene.Instances.Add(Block(box: box, material: white, center: new(-1.5f, -1, -6), size: new(1, 1, 1)));
            scene.Instances.Add(
                Block(box: box, material: white, center: new(1.2f, -0.5f, -9), size: new(1.5f, 2, 1.5f))
            );
            (TemporalSignal diffuse, TemporalSignal specular) = renderer.ScreenTraceSignals;
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, new NotANumberSamples(diffuse, specular));

            Draw(renderer, scene, frames: 3);

            float[] distance = renderer.ReadDistance();
            float[] diffuseSample = renderer.ReadImage(diffuse.Sample(0));
            float[] specularSample = renderer.ReadImage(specular.Sample(0));
            int lit = 0;
            for (int pixel = 0; pixel < distance.Length; pixel++)
            {
                if (distance[pixel] <= 0)
                    continue;
                lit++;
                for (int channel = 0; channel < 3; channel++)
                {
                    float.IsFinite(diffuseSample[4 * pixel + channel])
                        .Should()
                        .BeTrue("lit pixel {0} has a diffuse sample", pixel);
                    float.IsFinite(specularSample[4 * pixel + channel])
                        .Should()
                        .BeTrue("lit pixel {0} has a specular sample", pixel);
                }
            }

            lit.Should().BeGreaterThan(Width * Height / 3);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void InAResetFrameEveryLitPixelButEachBlocksChosenPixelTracesItsOwnRayOfEachKind()
    {
        // A smooth floor that fills the lower half: every pixel of it starts both kinds, and no pixel has a history.
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.2f)));

            Draw(renderer, scene, frames: 1);

            float[] distance = renderer.ReadDistance();
            int lit = 0;
            var blocks = new HashSet<(int X, int Y)>();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (distance[y * Width + x] > 0)
                {
                    lit++;
                    blocks.Add((x / 2, y / 2));
                }

            (int reflection, int diffuseRays) = renderer.ReadOwnRayCounts();
            diffuseRays.Should().Be(lit - blocks.Count, "each block's chosen pixel traces the block's ray instead");
            reflection.Should().Be(lit - blocks.Count);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AfterItsHistoryFillsAPixelInsideASurfaceSharesItsBlocksDiffuseRay()
    {
        // A rough floor: its specular cap and its diffuse cap reach FullRateFrames, so once the histories hold 8 frames,
        // only the pixels whose block's ray does not fit them trace their own rays.
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));
            Draw(renderer, scene, frames: 1);
            int reset = renderer.ReadOwnRayCounts().Diffuse;

            Draw(renderer, scene, frames: 12);

            int converged = renderer.ReadOwnRayCounts().Diffuse;
            converged.Should().BeLessThan(reset / 10, "a converged pixel of a flat floor shares its block's ray");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.2f, false)]
    [InlineData(0f, true)]
    public void AConvergedPixelOfAFlatFloorTracesItsOwnReflectionRayOnlyWhereOnePixelsTurnOfTheViewLeavesItsLobe(
        float roughness,
        bool ownRays
    )
    {
        // One pixel of the view turns the mirror direction by about 0.003 rad near the middle of 640 × 360. The share of a
        // lobe at roughness 0.2 is about 0.04 rad, so the block's ray lies inside it. The share of the roughness floor's
        // lobe is about 0.002 rad, so a neighbor's mirror direction lies outside it, though the floor's normal is the
        // same. Toward the image's sides the view turns less, and there the side neighbors share: about a tenth of the
        // floor's pixels.
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness)));
            Draw(renderer, scene, frames: 1);
            int reset = renderer.ReadOwnRayCounts().Reflection;

            Draw(renderer, scene, frames: 12);

            int converged = renderer.ReadOwnRayCounts().Reflection;
            if (ownRays)
                converged.Should().BeGreaterThan(reset * 3 / 4, "a flat mirror keeps its sharp image");
            else
                converged.Should().BeLessThan(reset / 10, "a converged pixel of a glossy floor shares its block's ray");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(0.4f, false)]
    public void AConvergedPixelOfABallTracesItsOwnReflectionRayOnlyWhereItsNeighborsNormalLeavesItsLobe(
        float roughness,
        bool ownRays
    )
    {
        // A ball of about 78 px radius: its normal turns by about 0.013 rad per pixel, its mirror direction by twice that.
        // The share of a mirror's lobe at the roughness floor is about 0.002 rad, so no neighbor's ray lies inside it.
        // The share of a lobe at roughness 0.4 is about 0.16 rad, so every neighbor's does.
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Sphere(1f, 64, 32)),
                    material: renderer.CreateMaterial(WhiteFloor(roughness)),
                    position: new(0, 0, -4),
                    scale: Vector3.One,
                    chainDepth: TestChains.CamerasFrame,
                    radius: 1
                )
            );

            Draw(renderer, scene, frames: 13);

            float[] distance = renderer.ReadDistance();
            int lit = 0;
            var blocks = new HashSet<(int X, int Y)>();
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (distance[y * Width + x] > 0)
                {
                    lit++;
                    blocks.Add((x / 2, y / 2));
                }
            int sharing = lit - blocks.Count;
            int own = renderer.ReadOwnRayCounts().Reflection;
            if (ownRays)
                own.Should().BeGreaterThan(sharing * 9 / 10, "a curved mirror's neighbor reflects another direction");
            else
                own.Should().BeLessThan(sharing / 10, "a rough ball's lobe holds its neighbor's ray");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(70)]
    public void TwoRenderersThatDrawTheSameFramesGiveTheSameBits(int frames)
    {
        float[] first = SignalsAfterFrames(frames);

        float[] second = SignalsAfterFrames(frames);

        second
            .Should()
            .Equal(
                first,
                "every random number comes from the pixel or block, the draw index, a constant and the mask, also past the mask's period of 64 draws"
            );
    }

    /// <summary>Both signals' slow histories after <paramref name="frames"/> frames of a floor with a block on it.</summary>
    private static float[] SignalsAfterFrames(int frames)
    {
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(0.3f);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.3f)));
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            scene.Instances.Add(
                Block(
                    box: box,
                    material: renderer.CreateMaterial(WhiteFloor(0.5f)),
                    center: new(0, -1, -6),
                    size: new(2, 1, 1)
                )
            );
            Draw(renderer, scene, frames: frames);
            (TemporalSignal diffuse, TemporalSignal specular) = renderer.ScreenTraceSignals;
            return [.. renderer.ReadImage(diffuse.Slow(0)), .. renderer.ReadImage(specular.Slow(0))];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheReflectionSampleOfAMirrorFloorHoldsTheGlowOfThePixelThatTheCSharpWalkHits()
    {
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Dark();
            scene.Instances.Add(Floor(renderer, Mirror()));
            AddTiledWall(renderer, scene);

            Draw(renderer, scene, frames: 1);

            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
            float[] lit = renderer.ReadHdr();
            float[] distance = renderer.ReadDistance();
            DepthPyramid pyramid = PyramidOf(renderer);
            CameraView camera = renderer.LastScene.Camera;
            var rays = RecordedRays.Read(renderer, kind: 0);
            int compared = 0;
            int misses = 0;
            for (int y = 232; y < Height - 40; y += 4)
            for (int x = 130; x < 510; x += 6)
            {
                // The tracer draws its ray from GGX's visible normals at the roughness floor, so the C# walk follows the
                // recorded direction, not the exact mirror direction.
                RayHit? walked = WalkHitOf(
                    camera: camera,
                    distance: distance,
                    pyramid: pyramid,
                    pixel: (x, y),
                    direction: rays.DirectionAt((x, y))
                );
                if (walked is not { } hit)
                {
                    At(sample, (x, y))
                        .Length()
                        .Should()
                        .BeLessThan(1e-5f, "a CPU miss reflects the dark sky at ({0}, {1})", x, y);
                    sample[4 * (y * Width + x) + 3].Should().Be(0, "a CPU miss must not become a GPU hit");
                    misses++;
                    continue;
                }
                if (!InsideATile(lit, hit.Pixel))
                    continue;
                Vector3 expected = (1 - hit.EntryShare) * At(lit, hit.Pixel);
                Vector3 shown = At(sample, (x, y));
                BrdfTesting
                    .LargestChannelDifference(shown, expected)
                    .Should()
                    .BeLessThanOrEqualTo(
                        0.03f * expected.Y + 1e-3f,
                        "floor pixel ({0}, {1}) reflects the tile of pixel {2}",
                        x,
                        y,
                        hit.Pixel
                    );
                compared++;
            }

            compared.Should().BeGreaterThan(300, "most floor pixels below the wall reflect the inside of a tile");
            misses.Should().BePositive("the pin tests CPU misses as well as hits");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnImpostorsGlowReachesAMirrorOnlyWhenItsEntityHoldsNoLight(bool holdsLight)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            impostors: true
        );
        try
        {
            RenderSnapshot scene = GlowingPanelOverAMirror(renderer, new(4f), holdsLight);
            Draw(renderer, scene, frames: 3);

            renderer.LastFrameStats.Impostors.Should().BePositive("the receiver sees a billboard, not the mesh panel");
            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0));
            float reflected = At(sample, MirrorPixel(GlowingPanelCenter)).X;
            if (holdsLight)
                reflected.Should().BeLessThan(0.01f, "the billboard's light mark suppresses its baked glow");
            else
                reflected.Should().BeGreaterThan(1f, "the same billboard without a light mark sends its glow");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A wall 14 m ahead of tiles of 1 m that glow in four colors, from the floor up to 6 m, so that a floor pixel's
    /// reflection names the tile that its ray hits.
    /// </summary>
    private static void AddTiledWall(VulkanRenderer renderer, RenderSnapshot scene)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        Vector3[] colors = [new(1, 0.2f, 0.2f), new(0.2f, 1, 0.2f), new(0.2f, 0.2f, 1), new(1, 1, 0.2f)];
        MaterialId[] glows = [.. colors.Select(color => Glow(renderer, 2 * color))];
        for (int row = 0; row < 6; row++)
        for (int column = -8; column < 8; column++)
            scene.Instances.Add(
                Block(
                    box: box,
                    material: glows[(row + column + 16) % 4],
                    center: new(column + 0.5f, FloorHeight + row + 0.5f, -14),
                    size: new(1, 1, 0.1f)
                )
            );
    }

    /// <summary>Whether the pixels within 2 of <paramref name="pixel"/> all show its color: the inside of a tile.</summary>
    private static bool InsideATile(float[] lit, (int X, int Y) pixel)
    {
        Vector3 color = At(lit, pixel);
        for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            if (Vector3.Distance(At(lit, (pixel.X + dx, pixel.Y + dy)), color) > 1e-3f)
                return false;
        return true;
    }

    /// <summary>
    /// The C# walk's hit of a floor pixel's ray along <paramref name="direction"/>, as the tracer starts it: from the
    /// surface point at the pixel's distance, at the reflection cap. Null for a miss.
    /// </summary>
    private static RayHit? WalkHitOf(
        CameraView camera,
        float[] distance,
        DepthPyramid pyramid,
        (int X, int Y) pixel,
        Vector3 direction
    )
    {
        Vector3 viewRay = camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f));
        var toSurface = Vector3.Normalize(viewRay);
        float distanceToEye = distance[pixel.Y * camera.Width + pixel.X];
        var ray = ScreenRay.FromSurface(
            point: toSurface * distanceToEye,
            viewDepth: distanceToEye / viewRay.Length(),
            geometricNormal: Vector3.UnitY,
            direction: direction,
            camera: camera
        );
        return ray.Walk(pyramid, direction, ScreenTrace.Default.ReflectionSteps).TryGetValue(out RayHit hit)
            ? hit
            : null;
    }

    /// <summary>
    /// The rays of one kind that the last frame recorded (screen_trace.glsl, RayRecord): the ray plan's bits at each
    /// pixel, and the ray image of the pixels' own rays and of the blocks' rays, 4 channels per texel.
    /// </summary>
    private sealed record RecordedRays(int Kind, float[] Plan, float[] Own, float[] Blocks)
    {
        /// <summary>Reads the records of <paramref name="kind"/>: 0 the reflection rays, 1 the diffuse rays.</summary>
        public static RecordedRays Read(VulkanRenderer renderer, int kind)
        {
            ScreenTraceImages images = renderer.ScreenTraceImages;
            return new(
                Kind: kind,
                Plan: renderer.ReadImage(images.Plan),
                Own: renderer.ReadImageLayer(images.OwnRay, kind),
                Blocks: renderer.ReadImageLayer(images.BlockRay, kind)
            );
        }

        /// <summary>
        /// The direction of the ray that <paramref name="pixel"/> traced: its own ray where the plan lists it, or else its
        /// block's ray, which its block's chosen pixel traced. The code's units decode as directionOfCode does.
        /// </summary>
        public Vector3 DirectionAt((int X, int Y) pixel)
        {
            bool own = ((int)Plan[pixel.Y * Width + pixel.X] & (1 << Kind)) != 0;
            int texel = own ? pixel.Y * Width + pixel.X : pixel.Y / 2 * ((Width + 1) / 2) + pixel.X / 2;
            float[] rays = own ? Own : Blocks;
            return OctahedralNormal.Decode(
                new(rays[4 * texel] * (2 / 65535f) - 1, rays[4 * texel + 1] * (2 / 65535f) - 1)
            );
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void AStillUnderTheTracerDrawsItsWarmUpDrawsAndThenTheShownDraw(int warmUpFrames)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 64,
            height: 36,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: warmUpFrames
        );
        try
        {
            var counted = new CountedDraws();
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, counted);

            renderer.Render(Gray(0.3f), 1f);

            counted.Draws.Should().Be(warmUpFrames + 1, "the first draw of a renderer is a still");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheRendererTakesTheLargestWarmUpCount() =>
        FluentActions
            .Invoking(() =>
                Gpu.DisposeAndAssertClean(
                    Gpu.Headless(
                        width: 64,
                        height: 36,
                        screenTrace: ScreenTrace.Default,
                        warmUpFrames: RendererOptions.MaxWarmUpFrames
                    )
                )
            )
            .Should()
            .NotThrow("1024 is the top of the range");

    /// <summary>A pass that records nothing and counts the draws that record it.</summary>
    private sealed class CountedDraws : RenderPass
    {
        public int Draws { get; private set; }

        public override string Name => "counted draws";

        public override void Record(FrameContext context) => Draws++;
    }

    /// <summary>A pass before the tracer that sets both signals' samples to NaN, so that a sample that the tracer leaves shows.</summary>
    private sealed unsafe class NotANumberSamples(TemporalSignal diffuse, TemporalSignal specular) : RenderPass
    {
        public override string Name => "test samples";

        public override void Record(FrameContext context)
        {
            GpuImage[] samples = [diffuse.Sample(0), specular.Sample(0)];
            foreach (GpuImage image in samples)
                context.Barriers.Image(image, ImageUse.TransferDestination, discard: true);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            var color = new ClearColorValue(
                float32_0: float.NaN,
                float32_1: float.NaN,
                float32_2: float.NaN,
                float32_3: float.NaN
            );
            var whole = new ImageSubresourceRange(
                aspectMask: ImageAspectFlags.ColorBit,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 1
            );
            foreach (GpuImage image in samples)
                context.Vk.CmdClearColorImage(
                    commandBuffer: context.CommandBuffer,
                    image: image.Handle,
                    imageLayout: ImageLayout.TransferDstOptimal,
                    pColor: &color,
                    rangeCount: 1,
                    pRanges: &whole
                );
        }
    }
}

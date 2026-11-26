using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen tracer (https://github.com/alexispurslane/tertius/wiki/temporal-renderer, section 6) and its two indirect signals, under
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
    public void UnderAUniformEnvironmentWithNoBlockerGlossyReflectionsConvergeToItsLight()
    {
        // The white furnace: every ray misses and reads the light of 0.5, and each estimator's mean is 1.
        const float light = 0.5f;
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(light);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 0.3f)));

            Draw(renderer, scene, frames: 40);

            TemporalSignal specular = renderer.ScreenTraceImages.Specular;
            Vector3 specularLight = MeanOver(renderer.ReadImage(specular.Slow(0)), MiddleOfTheFloor);
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
    public void ASurfaceAboveTheReflectionLimitReceivesEnvironmentLightWithoutTracing()
    {
        const float light = 0.5f;
        VulkanRenderer renderer = Tracing();
        try
        {
            RenderSnapshot scene = Gray(light);
            scene.Instances.Add(Floor(renderer, WhiteFloor(roughness: 1)));

            Draw(renderer, scene, frames: 40);

            renderer.ReadReflectionRayCount().Should().Be(0);
            renderer.ReadImage(renderer.ScreenTraceImages.BlockRay).Should().OnlyContain(word => word == 0);
            MeanOver(renderer.ReadHdr(), MiddleOfTheFloor).X.Should().BePositive();
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

            float[] sample = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
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
            float before = At(renderer.ReadImage(renderer.ScreenTraceImages.Specular.Slow(0)), shown).X;
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

            float after = At(renderer.ReadImage(renderer.ScreenTraceImages.Specular.Slow(0)), shown).X;
            after.Should().BeLessThan(0.1f * before);
            if (moves)
                At(renderer.ReadImage(renderer.ScreenTraceImages.Specular.Slow(0)), MirrorPixel(panel.Position))
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

            float[] sample = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
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

            float[] sample = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
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

            float[] sample = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
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
            int listedBefore = renderer.ReadReflectionRayCount();
            Draw(renderer, FloorAlone(renderer), frames: 1);
            int listed = renderer.ReadReflectionRayCount();

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
        TemporalSignal specular = renderer.ScreenTraceImages.Specular;
        return renderer.ReadImage(specular.Sample(0));
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
            TemporalSignal specular = renderer.ScreenTraceImages.Specular;
            renderer.Graph.Add(FrameGraph.Stage.GBuffer, new NotANumberSamples(specular));

            Draw(renderer, scene, frames: 3);

            float[] distance = renderer.ReadDistance();
            float[] specularSample = renderer.ReadImage(specular.Sample(0));
            int lit = 0;
            for (int pixel = 0; pixel < distance.Length; pixel++)
            {
                if (distance[pixel] <= 0)
                    continue;
                lit++;
                for (int channel = 0; channel < 3; channel++)
                {
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
    public void InAResetFrameEveryLitPixelButEachBlocksChosenPixelTracesItsOwnReflectionRay()
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

            int reflection = renderer.ReadReflectionRayCount();
            reflection.Should().Be(lit - blocks.Count);
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
            int reset = renderer.ReadReflectionRayCount();

            Draw(renderer, scene, frames: 12);

            int converged = renderer.ReadReflectionRayCount();
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
            int own = renderer.ReadReflectionRayCount();
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
            TemporalSignal specular = renderer.ScreenTraceImages.Specular;
            return renderer.ReadImage(specular.Slow(0));
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

            float[] sample = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
            float[] lit = renderer.ReadHdr();
            float[] distance = renderer.ReadDistance();
            DepthPyramid pyramid = PyramidOf(renderer);
            CameraView camera = renderer.LastScene.Camera;
            var rays = RecordedRays.Read(renderer);
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
            float[] sample = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
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
    private sealed record RecordedRays(float[] Plan, float[] Own, float[] Blocks)
    {
        public static RecordedRays Read(VulkanRenderer renderer)
        {
            ScreenTraceImages images = renderer.ScreenTraceImages;
            return new(
                Plan: renderer.ReadImage(images.Plan),
                Own: renderer.ReadImage(images.OwnRay),
                Blocks: renderer.ReadImage(images.BlockRay)
            );
        }

        /// <summary>
        /// The direction of the ray that <paramref name="pixel"/> traced: its own ray where the plan lists it, or else its
        /// block's ray, which its block's chosen pixel traced. The code's units decode as directionOfCode does.
        /// </summary>
        public Vector3 DirectionAt((int X, int Y) pixel)
        {
            bool own = ((int)Plan[pixel.Y * Width + pixel.X] & 1) != 0;
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
    private sealed unsafe class NotANumberSamples(TemporalSignal specular) : RenderPass
    {
        public override string Name => "test samples";

        public override void Record(FrameContext context)
        {
            GpuImage[] samples = [specular.Sample(0)];
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

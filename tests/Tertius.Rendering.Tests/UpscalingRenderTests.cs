using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>The real frame's temporal upscale, active rectangle, reset and native zero case, under Vulkan validation.</summary>
[Collection(GpuCollection.Name)]
public sealed class UpscalingRenderTests
{
    [Theory]
    [InlineData(64, 64, 40, 40)]
    [InlineData(7, 5, 8, 8)]
    [InlineData(1, 1, 8, 8)]
    public void SmallHeadlessOutputsKeepTheDefaultActiveScale(int width, int height, int renderWidth, int renderHeight)
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Width = width,
                Height = height,
                Validation = true,
                WarmUpFrames = 0,
            }
        );
        try
        {
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: BoxScene(renderer),
                name: "small default FSR output"
            );
            image.Width.Should().Be(width);
            image.Height.Should().Be(height);
            renderer.LastScene.Camera.Width.Should().Be(renderWidth);
            renderer.LastScene.Camera.Height.Should().Be(renderHeight);
            float.IsFinite(renderer.LastScene.Constants.RenderParams.X).Should().BeTrue();
            float.IsFinite(renderer.LastScene.Constants.RenderParams.Y).Should().BeTrue();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(40, 32)]
    [InlineData(7, 5)]
    [InlineData(1, 1)]
    public void AWindowCanShrinkBelowFsrsAllocationMinimumAndRestore(int width, int height)
    {
        VulkanRenderer renderer = WindowRenderer(target: new(1000f / 60, 1, 1));
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "initial FSR window");
            int allocations = renderer.Allocator.LiveAllocations;
            renderer.Resize(width, height);
            scene.Time += 1.0 / 60;
            Capture small = Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "small FSR window");
            small.Width.Should().Be(width);
            small.Height.Should().Be(height);
            renderer.LastScene.Camera.Width.Should().Be(Math.Max(8, width / 8 * 8));
            renderer.LastScene.Camera.Height.Should().Be(Math.Max(8, height / 8 * 8));
            renderer.ReadSurfaceTest().Should().OnlyContain(bits => bits == 0);
            renderer.Resize(160, 96);
            scene.Time += 1.0 / 60;
            Capture restored = Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "restored FSR window");
            restored.Width.Should().Be(160);
            restored.Height.Should().Be(96);
            renderer.ReadSurfaceTest().Should().OnlyContain(bits => bits == 0);
            renderer.Allocator.LiveAllocations.Should().Be(allocations);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void FullyOpaqueCutoutPixelsUseTheSameMainViewMipBiasAsSolidPixels()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            byte[] pixels = new byte[256 * 256 * 4];
            for (int row = 0; row < 256; row++)
            for (int column = 0; column < 256; column++)
            {
                int offset = 4 * (row * 256 + column);
                byte color = ((row / 4 + column / 4) & 1) == 0 ? (byte)0 : (byte)255;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = color;
                pixels[offset + 3] = 255;
            }
            MaterialDesc material = MaterialDesc.Color(Vector4.One, roughness: 1) with
            {
                BaseColorMap = renderer.CreateTexture(new(Width: 256, Height: 256, Rgba: pixels)),
            };
            RenderInstance box = scene.Instances[0];
            box.Material = renderer.CreateMaterial(material);
            scene.Instances[0] = box;
            renderer.Render(scene, 1);
            float[] solid = renderer.ReadHdr();

            box.Material = renderer.CreateMaterial(material with { Alpha = new CutoutAlpha(0.5f) });
            scene.Instances[0] = box;
            renderer.Render(scene, 1);

            renderer.ReadHdr().Should().Equal(solid);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void NativeRenderingKeepsOddOutputDimensionsAndNoJitter()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 159, height: 91, warmUpFrames: 0);
        try
        {
            renderer.Render(BoxScene(renderer), 1);
            renderer.LastScene.Camera.Width.Should().Be(159);
            renderer.LastScene.Camera.Height.Should().Be(91);
            renderer.LastScene.Constants.RenderParams.Should().Be(Vector4.Zero);
            renderer.LastScene.Constants.PreviousJitter.Should().Be(Vector4.Zero);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void FsrReadsThisFramesExposureRatioBeforeAdaptationWritesTheNextFramesExposure()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            exposure: AutoExposure.Default with
            {
                IntoDarkSeconds = 0,
                IntoLightSeconds = 0,
            },
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            renderer.Render(scene, 1);
            (float stops, float previousStops) = renderer.ReadExposureStops();
            Gpu.Star(snapshot: scene, irradiance: Gpu.StarIrradiance(scene) * 0.25f);
            scene.Time += 1.0 / 60;

            renderer.Render(scene, 1);

            renderer.ReadFsrExposureRatio().Should().BeApproximately(MathF.Pow(2, stops - previousStops), 0.0001f);
            renderer
                .ReadExposureStops()
                .Stops.Should()
                .NotBeApproximately(
                    stops,
                    0.01f,
                    "adaptation must also run, but only after the upscale reads the old stops"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheReactiveMaskMarksGlassThatChangedTheOpaqueColorButNotTheSky()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            renderer.Render(scene, 1);
            renderer.ReadReactiveMask().Should().OnlyContain(value => value == 0);
            RenderInstance box = scene.Instances[0];
            box.Material = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.2f, Emissive: new Vector3(4), Alpha: new GlassAlpha())
            );
            scene.Instances[0] = box;
            scene.Time += 1.0 / 60;

            renderer.Render(scene, 1);

            byte[] mask = renderer.ReadReactiveMask();
            CameraView camera = renderer.LastScene.Camera;
            mask[camera.Height / 2 * camera.Width + camera.Width / 2].Should().BeGreaterThan(200);
            mask[0].Should().Be(0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WindowResolutionChangesKeepTheImagesAndHistoryWhileClearingInactiveDepth()
    {
        VulkanRenderer renderer = WindowRenderer();
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            renderer.Render(scene, 1);
            int allocations = renderer.Allocator.LiveAllocations;
            DriveScale(renderer: renderer, scene: scene, frames: 12, renderMilliseconds: 100, fixedMilliseconds: 1);
            CameraView camera = renderer.LastScene.Camera;
            camera.Width.Should().Be(80);
            camera.Height.Should().Be(48);
            float[] depth = renderer.ReadDepthFinest();
            for (int row = 0; row < 96; row++)
            for (int column = 0; column < 160; column++)
                if (column >= camera.Width || row >= camera.Height)
                    depth[row * 160 + column].Should().Be(0, "inactive pixels must not enter the depth pyramid");
            renderer
                .ReadSurfaceTest()
                .Should()
                .Contain(bits => bits != 0, "a scale change continues the previous surface history");
            DriveScale(renderer: renderer, scene: scene, frames: 64, renderMilliseconds: 0.01, fixedMilliseconds: 0.01);
            renderer.LastScene.Camera.Width.Should().BeGreaterThan(144);
            renderer.Allocator.LiveAllocations.Should().Be(allocations, "scale changes do not allocate new images");
            renderer.ScaleController!.AssertInvariants();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReflectedMotionFindsThePanelWhileTheActiveSizeChanges(bool grows)
    {
        VulkanRenderer renderer = WindowRenderer(width: 640, height: 360);
        try
        {
            RenderSnapshot scene = ReflectionTesting.Dark();
            scene.Instances.Add(ReflectionTesting.Floor(renderer, ReflectionTesting.Mirror()));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: ReflectionTesting.Glow(renderer, new(4f)),
                    position: new(0, -0.5f, -12),
                    scale: new(3, 2, 0.2f),
                    radius: 1.8f,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            renderer.Render(scene, 1);
            DriveScale(
                renderer: renderer,
                scene: scene,
                frames: 40,
                renderMilliseconds: grows ? 100 : 0.01,
                fixedMilliseconds: 0.01
            );
            int beforeWidth = renderer.LastScene.Camera.Width;
            scene.Camera.PreviousPosition = scene.Camera.Position;
            scene.Camera.Position += new Vector3(0.3f, 0, 0);
            RenderInstance panel = scene.Instances[1];
            panel.PreviousPosition = panel.Position;
            panel.Position += new Vector3(4, 0, 0);
            scene.Instances[1] = panel;

            DriveScale(
                renderer: renderer,
                scene: scene,
                frames: 12,
                renderMilliseconds: grows ? 0.01 : 100,
                fixedMilliseconds: 0.01
            );

            CameraView camera = renderer.LastScene.Camera;
            if (grows)
                camera.Width.Should().BeGreaterThan(beforeWidth);
            else
                camera.Width.Should().BeLessThan(beforeWidth);
            renderer.LastDraw.Reset.Should().BeFalse();
            float[] history = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0));
            (int X, int Y) shown = ReflectionTesting.MirrorPixel(
                panel.Position - scene.Camera.Position,
                camera.Width,
                camera.Height
            );
            (int X, int Y) old = ReflectionTesting.MirrorPixel(
                new Vector3(0, -0.5f, -12) - scene.Camera.Position,
                camera.Width,
                camera.Height
            );
            ReflectionTesting.At(history, shown).X.Should().BeGreaterThan(3.2f);
            ReflectionTesting.At(history, old).X.Should().BeLessThan(0.4f);
            history[4 * (shown.Y * 640 + shown.X) + 3]
                .Should()
                .BeGreaterThan(1, "the reflected location continues history across an active-size change");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static VulkanRenderer WindowRenderer(int width = 160, int height = 96, FrameTimeTarget? target = null) =>
        VulkanRenderer.CreateOffscreen(
            options: new()
            {
                Width = width,
                Height = height,
                FramesInFlight = 1,
                Validation = true,
                Impostors = false,
                ScreenTrace = ScreenTrace.Default,
                EnvironmentMap = new NoEnvironmentMap(),
                LocalShadows = new NoLocalShadows(),
                Subsurface = new NoSubsurface(),
                CascadedLights = 0,
                Exposure = new FixedExposure(0),
                Bloom = new NoBloom(),
                Upscaling = new Fsr3Upscaling(target ?? FrameTimeTarget.Default, 0),
                WarmUpFrames = 0,
            },
            createOutput: (device, allocator, extent) =>
                new HeadlessTarget(device: device, allocator: allocator, extent: extent, bgra: false),
            headless: false
        );

    private static void DriveScale(
        VulkanRenderer renderer,
        RenderSnapshot scene,
        int frames,
        double renderMilliseconds,
        double fixedMilliseconds
    )
    {
        for (int frame = 0; frame < frames; frame++)
        {
            long number = renderer.DrawnFrames - 1;
            renderer.ScaleController!.Measure(
                new(
                    number,
                    new PassTiming[]
                    {
                        new("G-buffer", renderMilliseconds) { Stage = FrameGraph.Stage.GBuffer },
                        new("exposure", 0) { Stage = FrameGraph.Stage.Exposure },
                        new("FSR 3.1", fixedMilliseconds) { Stage = FrameGraph.Stage.Upscale },
                        new("bloom", fixedMilliseconds) { Stage = FrameGraph.Stage.Bloom },
                        new("tone map", 100) { Stage = FrameGraph.Stage.ToneMap },
                    },
                    100 + renderMilliseconds + 2 * fixedMilliseconds
                )
            );
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1);
        }
    }

    [Fact]
    public void RepeatingAStillRestartsTheJitterWithoutStartingAController()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0)
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            Capture first = Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "FSR first still");
            renderer.DrawnFrames.Should().Be(RendererOptions.DefaultWarmUpFrames + 1);
            Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "FSR repeated still");
            renderer.DrawnFrames.Should().Be(2 * (RendererOptions.DefaultWarmUpFrames + 1));
            renderer.LastDraw.Should().Be((RendererOptions.DefaultWarmUpFrames, false));
            renderer.ScaleController.Should().BeNull();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void IndependentHeadlessRunsWithTheSameDrawHistoryProduceTheSameOutput()
    {
        Capture? first = null;
        for (int run = 0; run < 2; run++)
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: 160,
                height: 96,
                upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0)
            );
            try
            {
                Capture shown = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: BoxScene(renderer),
                    name: "FSR deterministic still"
                );
                if (first is { } reference)
                    shown.Rgba.Should().Equal(reference.Rgba);
                first = shown;
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }

    [Fact]
    public void TheUpscaledHdrImageHoldsTheOutputSizeUnderFsr()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            renderer.Render(BoxScene(renderer), 1);

            float[] upscaled = renderer.ReadPostHdr();

            upscaled.Length.Should().Be(4 * 160 * 96, "FSR writes the output size, before bloom and tone mapping");
            upscaled.Should().Contain(value => value > 0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void JitterDoesNotAppearInTheMotionOfAStillBox()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            renderer.Render(scene, 1);
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1);
            int width = renderer.LastScene.Camera.Width;
            int height = renderer.LastScene.Camera.Height;
            float[] motion = renderer.ReadMotion();
            int center = 4 * (height / 2 * width + width / 2);
            motion[center].Should().Be(0);
            motion[center + 1].Should().Be(0);
            motion[center + 2].Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMovingBoxKeepsTheAnalyticMotionSignAndUnitsUnderFsr()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            renderer.Render(scene, 1);
            RenderInstance box = scene.Instances[0];
            box.PreviousPosition = box.Position;
            box.Position += new Vector3(0.2f, 0, 0);
            scene.Instances[0] = box;
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1);
            CameraView camera = renderer.LastScene.Camera;
            int center = camera.Height / 2 * camera.Width + camera.Width / 2;
            Vector4 constants = renderer.LastScene.Constants.RenderParams;
            Vector3 ray = camera.ViewRayThrough(
                new(camera.Width / 2 + 0.5f + constants.Y, camera.Height / 2 + 0.5f + constants.Z)
            );
            float depth = renderer.ReadDistance()[center] / ray.Length();
            float expected = -0.2f / (2 * depth * camera.TanHalfX);
            renderer.ReadMotion()[4 * center].Should().BeApproximately(expected, 0.001f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnOutputResizeRecreatesTheRenderImagesAndResetsTheHistory()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = BoxScene(renderer);
            renderer.Render(scene, 1);
            renderer.Resize(192, 112);
            scene.Time += 1.0 / 60;
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "resized FSR output");
            image.Width.Should().Be(192);
            image.Height.Should().Be(112);
            renderer.LastScene.Camera.Width.Should().Be(128);
            renderer.LastScene.Camera.Height.Should().Be(72);
            renderer.ReadSurfaceTest().Should().OnlyContain(bits => bits == 0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFirstFixedExposureInitializesBothStopsBeforeFsrReadsThem()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 96,
            exposure: new FixedExposure(3),
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            renderer.Render(BoxScene(renderer), 1);
            renderer.ReadExposureStops().Should().Be((3f, 3f));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static RenderSnapshot BoxScene(VulkanRenderer renderer)
    {
        RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitZ);
        scene.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Box(new(1f))),
                material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f)),
                position: new(0, 0, -4),
                scale: Vector3.One,
                entity: 5,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return scene;
    }

    [Fact]
    public void TheHeadlessDefaultUpscalesTheActiveRectangleBeforeTheOutputPasses()
    {
        using var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Width = 160,
                Height = 90,
                Validation = true,
                Impostors = false,
                ScreenTrace = new NoScreenTrace(),
                EnvironmentMap = new NoEnvironmentMap(),
                LocalShadows = new NoLocalShadows(),
                Subsurface = new NoSubsurface(),
                CascadedLights = 0,
                Exposure = new FixedExposure(0),
                WarmUpFrames = 2,
            }
        );
        renderer.Render(Gpu.Snapshot(Vector3.UnitZ), 0);
        renderer.LastScene.Camera.Width.Should().Be(104);
        renderer.LastScene.Camera.Height.Should().Be(56);
        renderer.Graph.Passes.Select(pass => pass.Name).Should().Contain("FSR 3.1");
        renderer
            .Graph.Passes.Select(pass => pass.Name)
            .Should()
            .NotContain(name => name.Contains("SMAA", StringComparison.OrdinalIgnoreCase));
        renderer.ValidationMessages.Should().BeEmpty();
    }
}

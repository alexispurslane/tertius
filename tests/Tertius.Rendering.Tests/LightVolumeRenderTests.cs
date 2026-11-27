using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class LightVolumeRenderTests
{
    private readonly record struct Panel(Vector3 Position, Vector3 Scale, uint Entity);

    private static VulkanRenderer CreateVolumeRenderer(bool screenTrace = false, float exposureStops = 0) =>
        VulkanRenderer.CreateHeadless(
            new RendererOptions
            {
                Width = 64,
                Height = 64,
                WarmUpFrames = 0,
                Validation = true,
                Upscaling = new NoUpscaling(),
                Exposure = new FixedExposure(exposureStops),
                EnvironmentMap = new NoEnvironmentMap(),
                ScreenTrace = screenTrace ? ScreenTrace.Default : new NoScreenTrace(),
                LightVolume = true,
            }
        );

    private static RenderInstance BoxPanel(MeshId box, MaterialId material, Panel panel) =>
        Gpu.Instance(
            mesh: box,
            material: material,
            position: panel.Position,
            scale: panel.Scale,
            chainDepth: TestChains.CamerasFrame,
            entity: panel.Entity
        );

    private static RenderSnapshot DarkSnapshot(Vector3 lightDirection)
    {
        RenderSnapshot scene = Gpu.Snapshot(lightDirection);
        scene.Lights.Clear();
        scene.Ambient = default;
        return scene;
    }

    private static float RenderCenterRed(VulkanRenderer renderer, RenderSnapshot scene)
    {
        scene.Time += 1.0 / 60;
        renderer.Render(scene, 1f);
        return renderer.ReadHdr()[4 * (32 * 64 + 32)];
    }

    [Theory]
    [InlineData(1.0f, true)]
    [InlineData(0.1f, false)]
    public void AnOffscreenSunlitRedSurfaceAppearsInBroadReflections(float roughness, bool broad)
    {
        VulkanRenderer renderer = Gpu.Headless(width: 64, height: 64, warmUpFrames: 0, lightVolume: true);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(
                new(BaseColor: Vector4.UnitW, Roughness: roughness, Reflectance: 1)
            );
            MaterialId red = renderer.CreateMaterial(MaterialDesc.Color(Vector4.UnitX + Vector4.UnitW, 1));
            RenderSnapshot scene = Gpu.Snapshot(-Vector3.UnitZ, starIrradiance: 20);
            scene.Ambient = default;
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 0.75f), new(2, 2, 0.08f), 2)));
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            int center = 4 * (32 * 64 + 32);
            renderer.ReadVolumeLightEvaluations().Should().BeInRange(1u, 200u);
            if (broad)
                hdr[center].Should().BeGreaterThan(hdr[center + 1] + 0.005f);
            else
                hdr[center].Should().BeLessThan(0.001f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnOffscreenEmitterLightsTheReceiverAndOpaqueSheetsReduceItByProjectedArea()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));

            float open = RenderCenterRed(renderer, scene);
            open.Should().BeGreaterThan(0.01f, "default lighting includes the offscreen emitter");
            renderer.ReadVolumeLightEvaluations().Should().Be(0, "there are no direct lights to evaluate");

            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, 0.6f), new(6, 6, 0.08f), 3)));
            RenderCenterRed(renderer, scene).Should().BeLessThan(open * 0.25f);

            scene.Instances[2] = BoxPanel(box, matte, new(new(0, 0, 0.6f), new(0.6f, 0.6f, 0.08f), 3));
            float partial = RenderCenterRed(renderer, scene);
            partial.Should().BeInRange(open * 0.05f, open * 0.98f);

            RenderInstance edgeOn = scene.Instances[2];
            edgeOn.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            edgeOn.PreviousRotation = edgeOn.Rotation;
            scene.Instances[2] = edgeOn;
            RenderCenterRed(renderer, scene).Should().BeGreaterThan(partial, "an edge-on sheet intercepts less light");

            scene.Instances.RemoveAt(2);
            RenderCenterRed(renderer, scene).Should().BeApproximately(open, open * 0.02f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void RebasingTheSnapshotOriginAcrossACameraCutPreservesVolumeLighting()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));

            float beforeCut = RenderCenterRed(renderer, scene);
            Vector3 shift = new(0.19f, -0.1f, 0.31f);
            scene.OriginInFrame += (Double3)shift;
            scene.Camera.Position -= shift;
            scene.Camera.PreviousPosition -= shift;
            for (int i = 0; i < scene.Instances.Count; i++)
            {
                RenderInstance instance = scene.Instances[i];
                instance.Position -= shift;
                instance.PreviousPosition -= shift;
                scene.Instances[i] = instance;
            }
            scene.CameraCuts++;

            RenderCenterRed(renderer, scene).Should().BeApproximately(beforeCut, beforeCut * 0.02f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASourceOutsideTheFineGridStillLightsTheReceiverThroughTheCoarseGrid()
    {
        VulkanRenderer renderer = CreateVolumeRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, matte, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, red, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));
            float near = RenderCenterRed(renderer, scene);

            scene.Instances[1] = BoxPanel(box, red, new(new(0, 0, 6.5f), new(2, 2, 0.08f), 2));
            RenderCenterRed(renderer, scene)
                .Should()
                .BeInRange(near * 0.001f, near * 0.5f, "a source outside the fine grid subtends a smaller solid angle");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANeutralOffscreenReflectorAddsFarLessIndirectLightThanAColoredOneOfTheSamePeakAlbedo()
    {
        float colored = MeasureOffscreenReflectorRed(baseColor: new(x: 1, y: 0, z: 0, w: 1), exposureStops: 0);
        float neutral = MeasureOffscreenReflectorRed(baseColor: Vector4.One, exposureStops: 0);

        colored.Should().BeGreaterThan(0.01f, "a saturated red reflector keeps its full indirect bounce");
        neutral.Should().BeGreaterThan(0.0001f, "neutral bounce is restrained rather than removed");
        neutral.Should().BeLessThan(colored * 0.1f, "low-saturation diffuse fill is restrained relative to color");
    }

    [Fact]
    public void ANeutralOffscreenEmitterAddsFarLessIndirectLightThanAColoredOneOfTheSamePeakRadiance()
    {
        float colored = MeasureOffscreenEmitterRed(emissive: new(8, 0, 0), exposureStops: 0);
        float neutral = MeasureOffscreenEmitterRed(emissive: new(8, 8, 8), exposureStops: 0);

        colored.Should().BeGreaterThan(0.01f, "a saturated red emitter keeps its full indirect contribution");
        neutral.Should().BeGreaterThan(0.0001f, "neutral emission is restrained rather than removed");
        neutral.Should().BeLessThan(colored * 0.1f, "low-saturation emission is restrained relative to color");
    }

    [Fact]
    public void ChangingExposureKeepsTheVolumesSceneRadianceUnchanged()
    {
        float baseReflector = MeasureOffscreenReflectorRed(baseColor: Vector4.One, exposureStops: 0);
        float boostedReflector = MeasureOffscreenReflectorRed(baseColor: Vector4.One, exposureStops: 4);
        float baseEmitter = MeasureOffscreenEmitterRed(emissive: new(8, 8, 8), exposureStops: 0);
        float boostedEmitter = MeasureOffscreenEmitterRed(emissive: new(8, 8, 8), exposureStops: 4);

        (boostedReflector / baseReflector)
            .Should()
            .BeApproximately(1f, 0.025f, "display exposure does not change the scene-referred diffuse bounce");
        (boostedEmitter / baseEmitter)
            .Should()
            .BeApproximately(1f, 0.025f, "display exposure does not change the scene-referred emission bounce");
    }

    [Fact]
    public void AGlossySurfaceOccludesOffscreenEnvironmentMissesBehindAnOpaqueWall()
    {
        VulkanRenderer renderer = CreateVolumeRenderer(screenTrace: true);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId glossy = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 0.2f, Reflectance: 1));
            MaterialId black = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0));
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Ambient = new AmbientLight
            {
                SkyColor = new(10),
                GroundColor = new(10),
                Intensity = 1,
            };
            scene.Instances.Add(BoxPanel(box, glossy, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            float open = RenderCenterRed(renderer, scene);

            scene.Instances.Add(BoxPanel(box, black, new(new(0, 0, 1.0f), new(8, 8, 0.08f), 2)));
            scene.CameraCuts++;
            float occluded = RenderCenterRed(renderer, scene);

            open.Should().BeGreaterThan(0.1f, "an open room reflects the offscreen environment on a miss");
            occluded
                .Should()
                .BeLessThan(open * 0.25f, "an opaque wall behind the camera occludes offscreen environment misses");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGlossyHitSurfaceWithoutSpecularHistoryOccludesEnvironmentReflectionsOnAResetFrame()
    {
        VulkanRenderer renderer = CreateVolumeRenderer(screenTrace: true);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId floorMat = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.05f, Model: new StandardShading(Metallic: 1))
            );
            MaterialId hitMat = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.2f, Model: new StandardShading(Metallic: 1))
            );
            MaterialId black = renderer.CreateMaterial(new(BaseColor: Vector4.UnitW, Roughness: 1, Reflectance: 0));
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Ambient = new AmbientLight
            {
                SkyColor = new(10),
                GroundColor = new(10),
                Intensity = 1,
            };
            scene.Instances.Add(BoxPanel(box, floorMat, new(new(0, -0.5f, -2.0f), new(6, 0.08f, 6), 1)));
            scene.Instances.Add(BoxPanel(box, hitMat, new(new(0, 0, -3.2f), new(6, 4, 0.08f), 2)));
            renderer.Render(scene, 1f);
            float open = renderer.ReadHdr()[4 * (54 * 64 + 32)];

            scene.Instances.Add(BoxPanel(box, black, new(new(0, 0, 1.0f), new(8, 6, 0.08f), 3)));
            scene.Instances.Add(BoxPanel(box, black, new(new(0, 2.0f, -1.0f), new(8, 0.08f, 8), 4)));
            scene.Instances.Add(BoxPanel(box, black, new(new(-2.8f, 0, -1.0f), new(0.08f, 6, 8), 5)));
            scene.Instances.Add(BoxPanel(box, black, new(new(2.8f, 0, -1.0f), new(0.08f, 6, 8), 6)));
            scene.CameraCuts++;
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1f);
            float closed = renderer.ReadHdr()[4 * (54 * 64 + 32)];

            open.Should().BeGreaterThan(1f, "an open room reflects the bright environment via the glossy wall");
            closed
                .Should()
                .BeLessThan(
                    open * 0.25f,
                    "a glossy hit surface with no specular history must obey volume occlusion inside a closed room"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static float MeasureOffscreenReflectorRed(Vector4 baseColor, float exposureStops)
    {
        VulkanRenderer renderer = CreateVolumeRenderer(exposureStops: exposureStops);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId source = renderer.CreateMaterial(MaterialDesc.Color(baseColor, 1));
            RenderSnapshot scene = Gpu.Snapshot(-Vector3.UnitZ, starIrradiance: 20);
            scene.Ambient = default;
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -1.5f), new(3, 3, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, source, new(new(0, 0, 0.75f), new(2, 2, 0.08f), 2)));
            return RenderCenterRed(renderer, scene);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static float MeasureOffscreenEmitterRed(Vector3 emissive, float exposureStops)
    {
        VulkanRenderer renderer = CreateVolumeRenderer(exposureStops: exposureStops);
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId receiver = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId source = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: emissive)
            );
            RenderSnapshot scene = DarkSnapshot(Vector3.UnitZ);
            scene.Instances.Add(BoxPanel(box, receiver, new(new(0, 0, -2), new(4, 4, 0.08f), 1)));
            scene.Instances.Add(BoxPanel(box, source, new(new(0, 0, 1.5f), new(2, 2, 0.08f), 2)));
            return RenderCenterRed(renderer, scene);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

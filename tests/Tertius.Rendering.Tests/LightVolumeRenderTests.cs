using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class LightVolumeRenderTests
{
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
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: receiver,
                    position: new(0, 0, -1.5f),
                    scale: new(3, 3, 0.08f),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 1
                )
            );
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: box,
                    material: red,
                    position: new(0, 0, 0.75f),
                    scale: new(2, 2, 0.08f),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            int center = 4 * (32 * 64 + 32);
            uint evaluations = renderer.ReadVolumeLightEvaluations();
            Console.WriteLine(
                $"Receiver red {hdr[center]}, green {hdr[center + 1]}, shadowed lighting evaluations {evaluations}."
            );
            evaluations.Should().BeInRange(1u, 200u);
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
    public void AnOffscreenEmitterLightsTheReceiverUntilAnOpaqueSheetInterceptsIt()
    {
        VulkanRenderer renderer = VulkanRenderer.CreateHeadless(
            new RendererOptions
            {
                Width = 64,
                Height = 64,
                WarmUpFrames = 0,
                Validation = true,
                Upscaling = new NoUpscaling(),
                Exposure = new FixedExposure(0),
                EnvironmentMap = new NoEnvironmentMap(),
                ScreenTrace = new NoScreenTrace(),
            }
        );
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId matte = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, 1));
            MaterialId red = renderer.CreateMaterial(
                new(BaseColor: Vector4.Zero with { W = 1 }, Roughness: 1, Emissive: new(8, 0, 0))
            );
            RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitZ);
            scene.Lights.Clear();
            scene.Ambient = default;
            RenderInstance Panel(MaterialId material, float z, float size, uint entity) =>
                Gpu.Instance(
                    mesh: box,
                    material: material,
                    position: new(0, 0, z),
                    scale: new(size, size, 0.08f),
                    chainDepth: TestChains.CamerasFrame,
                    entity: entity
                );
            scene.Instances.Add(Panel(material: matte, z: -2, size: 4, entity: 1));
            scene.Instances.Add(Panel(material: red, z: 1.5f, size: 2, entity: 2));
            float CenterRed()
            {
                scene.Time += 1.0 / 60;
                renderer.Render(scene, 1f);
                return renderer.ReadHdr()[4 * (32 * 64 + 32)];
            }
            float open = CenterRed();
            open.Should().BeGreaterThan(0.01f, "default lighting includes the offscreen emitter");
            uint evaluations = renderer.ReadVolumeLightEvaluations();
            Console.WriteLine($"Open receiver red {open}, dynamic lighting evaluations {evaluations}.");
            evaluations.Should().Be(0, "there are no direct lights to evaluate");
            scene.Instances.Add(Panel(material: matte, z: 0.6f, size: 6, entity: 3));
            float blocked = CenterRed();
            Console.WriteLine($"Blocked receiver red {blocked}.");
            blocked.Should().BeLessThan(open * 0.25f);
            scene.Instances[2] = Panel(material: matte, z: 0.6f, size: 0.6f, entity: 3);
            float partiallyBlocked = CenterRed();
            partiallyBlocked.Should().BeInRange(open * 0.05f, open * 0.98f);
            RenderInstance edgeOn = scene.Instances[2];
            edgeOn.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
            edgeOn.PreviousRotation = edgeOn.Rotation;
            scene.Instances[2] = edgeOn;
            CenterRed().Should().BeGreaterThan(partiallyBlocked, "the edge of the sheet intercepts less light");
            scene.Instances.RemoveAt(2);
            CenterRed().Should().BeApproximately(open, open * 0.02f);
            // A rebased snapshot describes exactly the same physical camera and surfaces.
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
            CenterRed().Should().BeApproximately(open, open * 0.02f);
            scene.Instances[1] = Panel(material: red, z: 6.5f, size: 2, entity: 2);
            float distant = CenterRed();
            Console.WriteLine($"Distant receiver red {distant}.");
            distant
                .Should()
                .BeInRange(open * 0.001f, open * 0.5f, "a source outside the fine grid subtends a smaller solid angle");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

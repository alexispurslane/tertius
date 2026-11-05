using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>The tracer's GPU record encoding and the walk's receiver-card exclusion.</summary>
[Collection(GpuCollection.Name)]
public sealed class ScreenTraceRecordTests
{
    [Fact]
    public void DensityCodesPreserveTheirDensityAndDecodeAZeroDensityAsExactlyZero()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: 160,
            height: 90,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            ScreenTraceProbe probe = ScreenTraceProbe.On(renderer, "screen_trace_density_probe.comp");
            renderer.Render(Dark(), 1f);

            float[] values = probe.Read();
            for (int index = 0; index < 8; index++)
            {
                if (index < 2)
                {
                    values[4 * index + 2].Should().Be(0, "zero and densities below 2^-100 decode to exactly zero");
                    values[4 * index + 3].Should().Be(1);
                    continue;
                }
                values[4 * index + 2]
                    .Should()
                    .BeApproximately(1, 0.024f, "a half-float logarithm near -100 rounds by at most 1/32 stop");
                int code = (int)values[4 * index] | (int)values[4 * index + 1] << 8;
                double density = new double[] { 0, 1e-35, 1e-30, 0.001, 0.1, 1, 4, 100000 }[index];
                Math.Pow(2, (double)BitConverter.UInt16BitsToHalf((ushort)code))
                    .Should()
                    .BeApproximately(density, 0.024 * density);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AHairReceiversWalkPassesItsOwnCardAndHitsAnotherCard()
    {
        const int width = 160,
            height = 90;
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1));
            MaterialId hair = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0.3f, Model: new HairShading(StrandDirection.PlusV))
            );
            RenderSnapshot scene = Dark();
            foreach (float horizontal in new[] { -1.5f, 1.5f })
                scene.Instances.Add(
                    Gpu.Instance(
                        mesh: plane,
                        material: hair,
                        position: new(horizontal, 0, -10),
                        rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                        scale: new(1, 1, 2),
                        radius: 3,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
            ScreenTraceProbe probe = ScreenTraceProbe.On(renderer, "screen_trace_hair_probe.comp");
            renderer.Render(scene, 1f);

            byte[] shading = renderer.ReadShading();
            float[] values = probe.Read();
            int compared = 0;
            for (int y = 30; y < 60; y++)
            for (int x = 50; x < width / 2 - 5; x++)
            {
                int at = 4 * (y * width + x);
                if (CardCode.Read(shading[at + 2], shading[at + 3]) is null)
                    continue;
                values[at]
                    .Should()
                    .BeGreaterThan(width / 2, "the walk passes the receiver's card and reaches the right card");
                values[at + 1]
                    .Should()
                    .BeLessThan(width / 2, "without the receiver-card exclusion the same ray hits its own card");
                compared++;
            }
            compared.Should().BeGreaterThan(100);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ABlocksRecordedDensityMatchesItsRecordedDirection(int kind)
    {
        const float roughness = 77 / 255f;
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            RenderSnapshot scene = Dark();
            scene.Instances.Add(Floor(renderer, Mirror(roughness)));
            renderer.Render(scene, 1f);

            float[] plan = renderer.ReadImage(renderer.ScreenTraceImages.Plan);
            float[] records = renderer.ReadImageLayer(renderer.ScreenTraceImages.BlockRay, kind);
            float[] normals = renderer.ReadNormals();
            CameraView camera = renderer.LastScene.Camera;
            var lobe = new GgxLobe(roughness);
            int compared = 0;
            for (int y = 260; y < 300; y += 2)
            for (int x = 200; x < 440; x += 2)
            {
                int chosen = Enumerable
                    .Range(0, 4)
                    .First(corner => ((int)plan[(y + corner / 2) * Width + x + corner % 2] & 1 << kind) == 0);
                int pixel = (y + chosen / 2) * Width + x + chosen % 2;
                int record = 4 * (y / 2 * ((Width + 1) / 2) + x / 2);
                Vector3 normal = OctahedralNormal.Decode(new(normals[4 * pixel], normals[4 * pixel + 1]));
                Vector3 direction = OctahedralNormal.Decode(
                    new(records[record] * (2 / 65535f) - 1, records[record + 1] * (2 / 65535f) - 1)
                );
                Vector3 view = -Vector3.Normalize(
                    camera.ViewRayThrough(new(x + chosen % 2 + 0.5f, y + chosen / 2 + 0.5f))
                );
                double cosine = Math.Max(Vector3.Dot(normal, view), 1e-4);
                double alphaSquared = lobe.Alpha * lobe.Alpha;
                double expected =
                    kind == 1
                        ? Math.Max(Vector3.Dot(normal, direction), 0) / Math.PI
                        : lobe.Distribution(Vector3.Dot(normal, Vector3.Normalize(view + direction)))
                            / (2 * (cosine + Math.Sqrt(alphaSquared + (1 - alphaSquared) * cosine * cosine)));
                int code = (int)records[record + 2];
                double decoded = Math.Pow(2, (double)BitConverter.UInt16BitsToHalf((ushort)code));
                decoded.Should().BeApproximately(expected, Math.Max(expected * 0.012, 1e-6));
                compared++;
            }
            compared.Should().Be(2400);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}

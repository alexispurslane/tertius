using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>The real shader walk, finite record and resolve limits, and compatible block sharing.</summary>
[Collection(GpuCollection.Name)]
public sealed class ScreenTraceHardeningTests
{
    private const int Width = 64,
        Height = 36;

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public void TheShaderWalkRejectsSlabBacksAndBackfacesAndDistinguishesTheTwoSidesOfATie(int channel, int expected)
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = Wall();
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "screen_walk_probe.comp");

            renderer.Render(scene, 1f);

            probe.Read()[4 * (Height / 2 * Width + Width / 2) + channel].Should().Be(expected);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARecordedHitCapsItsDistanceBeforeHalfFloatStorage()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = Wall();
        try
        {
            var probe = ScreenTraceProbe.On(renderer, "screen_trace_record_limit_probe.comp");

            renderer.Render(scene, 1f);

            float[] values = probe.Read();
            float[] expected = [0, 0.5f, 1, 1];
            for (int index = 0; index < expected.Length; index++)
                values[4 * index + 1].Should().Be(expected[index]);
            values[4 * 3].Should().Be(2, "the probe itself preserves the uncapped distance after normalization");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheDiffuseResolveCapsAWeightedRayBeforeHalfFloatStorage()
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = Wall();
        try
        {
            ScreenTraceProbe.On(renderer, "screen_trace_resolve_setup.comp");
            ScreenTraceProbe.On(renderer, "screen_trace_resolve.comp");

            renderer.Render(scene, 1f);

            float[] sample = renderer.ReadImage(renderer.ScreenTraceSignals.Diffuse.Sample(0));
            int at = 4 * (Height / 2 * Width + Width / 2);
            new Vector3(sample[at], sample[at + 1], sample[at + 2]).Should().Be(new Vector3(32256));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConvergedPixelKeepsItsOwnRayAcrossAnIncompatibleSurface(bool normalBreak)
    {
        (VulkanRenderer renderer, RenderSnapshot scene) = Wall();
        try
        {
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f));
            scene.Instances.Add(
                normalBreak
                    ? Gpu.Instance(
                        mesh: renderer.CreateMesh(Primitives.Plane(1)),
                        material: white,
                        position: new(0.65f + 50 / MathF.Sqrt(2), 0, -19.5f + 50 / MathF.Sqrt(2)),
                        rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 4)
                            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                        scale: new(50, 1, 30),
                        radius: 80,
                        chainDepth: TestChains.CamerasFrame
                    )
                    : Gpu.Instance(
                        mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                        material: white,
                        position: new(30.325f, 0, -10.5f),
                        scale: new(60, 40, 1),
                        chainDepth: TestChains.CamerasFrame
                    )
            );
            var probe = ScreenTraceProbe.On(renderer, "screen_trace_plan_probe.comp");
            for (int frame = 0; frame < 13; frame++)
            {
                scene.Time += 1.0 / 60;
                renderer.Render(scene, 1f);
            }

            float[] values = probe.Read();
            int compared = 0;
            for (int pixel = 0; pixel < Width * Height; pixel++)
            {
                int at = 4 * pixel;
                bool incompatible = normalBreak
                    ? values[at + 1] < 0.09f && values[at + 2] < 0.9f
                    : values[at + 1] > 0.1001f && values[at + 2] > 0.91f;
                if (values[at + 3] < 8 || !incompatible)
                    continue;
                values[at].Should().Be(1, "a block ray from another surface must not replace this pixel's own ray");
                compared++;
            }
            compared.Should().BeGreaterThan(10, "the scene must exercise converged nonchosen pixels across the break");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static (VulkanRenderer Renderer, RenderSnapshot Scene) Wall()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        RenderSnapshot scene = Gpu.Snapshot(Vector3.UnitY);
        scene.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.3f)),
                position: new(0, 0, -20),
                scale: new(60, 40, 1),
                chainDepth: TestChains.CamerasFrame
            )
        );
        return (renderer, scene);
    }
}

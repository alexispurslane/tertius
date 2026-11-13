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
    public void ALightAgeAboveTheRecordsCodesCodesAsTheLargestAndLeavesTheMovedMarkAlone()
    {
        // The light code fills the 14 bits between RayTraced and the moved mark, so an age above 16,382 codes as
        // 16,382, code 16,383, and never reaches bit 15 (docs/plans/bounce-light-confidence.md, section 8.5). The probe
        // writes the codes minus 16,000.
        (VulkanRenderer renderer, RenderSnapshot scene) = Wall();
        try
        {
            renderer.ScreenTraceSignals.Specular.SampleAge(0).Should().NotBeNull("the default history is precise");
            var probe = ScreenTraceProbe.On(renderer, "record_code_limit_probe.comp");

            renderer.Render(scene, 1f);

            float[] values = probe.Read();
            float[] codes = [382, 383, 383, 383];
            for (int column = 0; column < 8; column++)
            {
                values[4 * column].Should().Be(codes[column & 3], "column {0}", column);
                values[4 * column + 1].Should().Be(column >= 4 ? 1 : 0, "column {0} carries its own mark", column);
            }
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

    /// <summary>The paired history modes keep ray allocation independent of specular age, including after resize.</summary>
    [Collection(GpuCollection.Name)]
    public sealed class PreciseHistoryRayAllocation
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("original")]
        [InlineData("precise-capped")]
        [InlineData("precise-uncapped")]
        public void DisabledDistanceGuidesStillBindMatchingSynchronizedImages(string? mode) =>
            WithHistoryMode(
                mode,
                (renderer, scene) =>
                {
                    renderer.Render(scene, 1);
                    scene.Time += 1.0 / 60;
                    renderer.Render(scene, 1);

                    float[] history = renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Slow(0));
                    history[4 * (Height / 2 * Width + Width / 2) + 3].Should().BeGreaterThan(0);
                },
                sharing: false
            );

        [Theory]
        [InlineData(null)]
        [InlineData("precise-capped")]
        [InlineData("precise-uncapped")]
        [InlineData("original")]
        public void TheActualReconstructionProbeReadsTheSameUnfilteredHistoryAsTheIndependentLightProbe(string? mode) =>
            WithHistoryMode(
                mode,
                (renderer, scene) =>
                {
                    var probe = ReflectionReference.ReflectionComparisonProbe.On(renderer, Primitives.Box(new(0.5f)));

                    renderer.Render(scene, 1);
                    scene.Time += 1.0 / 60;
                    renderer.Render(scene, 1);

                    float[] shared = probe.Read(8);
                    float[] independent = probe.Read(0);
                    for (int pixel = 0; pixel < Width * Height; pixel++)
                    {
                        shared[2 * pixel + 1]
                            .Should()
                            .Be(
                                independent[2 * pixel],
                                "the actual shared attachment must read the same slow history through the matching declared storage format"
                            );
                        float.IsFinite(shared[2 * pixel]).Should().BeTrue();
                    }
                    independent
                        .Where((_, index) => index % 2 == 0)
                        .Max()
                        .Should()
                        .BeGreaterThan(0, "an all-zero probe cannot establish that the storage reader works");
                }
            );

        [Theory]
        [InlineData(null)]
        [InlineData("precise-capped")]
        [InlineData("precise-uncapped")]
        public void AConvergedFlatRoughWallSharesItsBlocksRaysOfBothKindsUnderEitherPreciseHistory(string? mode) =>
            WithHistoryMode(
                mode,
                (renderer, scene) =>
                {
                    renderer.Render(scene, 1);
                    (int reflection, int diffuse) = renderer.ReadOwnRayCounts();

                    for (int frame = 0; frame < 20; frame++)
                    {
                        scene.Time += 1.0 / 60;
                        renderer.Render(scene, 1);
                    }

                    renderer
                        .ReadOwnRayCounts()
                        .Reflection.Should()
                        .BeLessThan(reflection / 10, "the wall's lobe at roughness 0.3 holds its neighbors' rays");
                    renderer
                        .ReadOwnRayCounts()
                        .Diffuse.Should()
                        .BeLessThan(diffuse / 10, "the specular control must not force full-rate diffuse rays");
                }
            );

        [Theory]
        [InlineData(null)]
        [InlineData("precise-capped")]
        [InlineData("precise-uncapped")]
        public void ResizingThePreciseHistoryKeepsSampleFastAndGuideImagesInHalfPrecision(string? mode) =>
            WithHistoryMode(
                mode,
                (renderer, scene) =>
                {
                    renderer.Render(scene, 1);

                    renderer.Resize(Width / 2, Height / 2);
                    scene.Time += 1.0 / 60;
                    renderer.Render(scene, 1);
                    scene.Time += 1.0 / 60;
                    renderer.Render(scene, 1);

                    TemporalSignal specular = renderer.ScreenTraceSignals.Specular;
                    TemporalSignal diffuse = renderer.ScreenTraceSignals.Diffuse;
                    specular.Slow(0).Format.Should().Be(Silk.NET.Vulkan.Format.R32G32B32A32Sfloat);
                    specular.Sample(0).Format.Should().Be(Silk.NET.Vulkan.Format.R16G16B16A16Sfloat);
                    specular.ReflectionGuide(0).Format.Should().Be(Silk.NET.Vulkan.Format.R16G16B16A16Sfloat);
                    specular.HitImages(0, 0).LastFast.Format.Should().Be(Silk.NET.Vulkan.Format.R16G16B16A16Sfloat);
                    diffuse.Slow(0).Format.Should().Be(Silk.NET.Vulkan.Format.R16G16B16A16Sfloat);
                    diffuse
                        .HitImages(0, 0)
                        .LastFast.Should()
                        .BeSameAs(
                            diffuse.Slow(0),
                            "the H13 single diffuse history must retain its intentional fast/slow alias"
                        );
                }
            );

        private static void WithHistoryMode(
            string? mode,
            Action<VulkanRenderer, RenderSnapshot> action,
            bool sharing = true
        )
        {
            string[] names =
            [
                "TERTIUS_SPECULAR_HISTORY",
                "TERTIUS_INDIRECT_LIGHT_PROTOTYPE",
                "TERTIUS_INDIRECT_LIGHT_NEIGHBOR_CAP",
                "TERTIUS_REFLECTION_SHARING",
            ];
            string?[] previous = [.. names.Select(Environment.GetEnvironmentVariable)];
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable(names[0], mode);
                Environment.SetEnvironmentVariable(names[1], "1");
                Environment.SetEnvironmentVariable(names[2], "0");
                Environment.SetEnvironmentVariable(names[3], sharing ? "1" : null);
                (VulkanRenderer created, RenderSnapshot scene) = Wall();
                renderer = created;
                action(renderer, scene);
            }
            finally
            {
                for (int index = 0; index < names.Length; index++)
                    Environment.SetEnvironmentVariable(names[index], previous[index]);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
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

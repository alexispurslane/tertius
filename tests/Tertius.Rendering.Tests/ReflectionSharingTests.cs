using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.ReflectionReference;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ReflectionSharingTests
{
    private const int Width = 32;
    private const int Height = 24;

    [Fact]
    public void ASmoothedGuideReconstructsAnIsolatedPeakBelowOneQuarterOfItsInput()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false);
        int center = 2 * (Height / 2 * Width + Width / 2);

        output.Values[center].Should().BeLessThan(output.Values[center + 1] * 0.25f);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void AConstantRadianceFieldSurvivesGuidedReconstruction()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 9);

        for (int pixel = 0; pixel < Width * Height; pixel++)
            output.Values[2 * pixel].Should().BeApproximately(output.Values[2 * pixel + 1], 0.0001f);
    }

    [Fact]
    public void AnIlluminationStepOnOneFlatMaterialStaysBrighterOnItsLitSide()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 10);
        int center = 2 * (Height / 2 * Width + Width / 2);

        output.Values[center].Should().BeGreaterThan(output.Values[center - 2] * 2);
        output.Values[center].Should().BeGreaterThan(output.Values[center + 1] * 0.6f);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void SharingLowersAnIsolatedPeakByGivingLightToItsNeighbors()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false);
        int center = 2 * (Height / 2 * Width + Width / 2);

        output.Values[center].Should().BeLessThan(output.Values[center + 1] * 0.95f);
        output.Values[center - 2].Should().BeGreaterThan(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
        output.Values.Where((_, scalar) => scalar % 2 == 0).Min().Should().BeGreaterThanOrEqualTo(0);
        output.History.Should().Equal(output.Uploaded);
    }

    [Fact]
    public void TheDisabledSharingControlPreservesEveryOutputValue()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: false, boundary: false);

        for (int pixel = 0; pixel < Width * Height; pixel++)
            output.Values[2 * pixel].Should().Be(output.Values[2 * pixel + 1]);
        output.History.Should().Equal(output.Uploaded);
    }

    [Fact]
    public void AReceiverMaterialBoundaryDoesNotReceiveTheOtherMaterialsLight()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: true);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        output.Values[left].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void SmootherReflectionsShareLessOfTheSameIsolatedSample()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) smooth = DrawPulse(roughness: 0.045f, sharing: true, boundary: false);
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) rough = DrawPulse(roughness: 0.2f, sharing: true, boundary: false);
        int center = 2 * (Height / 2 * Width + Width / 2);

        float smoothRetained = smooth.Values[center] / smooth.Values[center + 1];
        float roughRetained = rough.Values[center] / rough.Values[center + 1];
        smoothRetained.Should().BeGreaterThan(roughRetained);
    }

    [Theory]
    [InlineData(-2, 0)]
    [InlineData(2, 0)]
    [InlineData(0, -2)]
    [InlineData(0, 2)]
    [InlineData(-2, -2)]
    [InlineData(2, 2)]
    [InlineData(-2, 1)]
    [InlineData(1, -2)]
    public void GuidedReconstructionStaysFiniteAtTwoPixelOffsetsOnAContinuousReceiver(int columnOffset, int rowOffset)
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false);
        int pixel = (Height / 2 + rowOffset) * Width + Width / 2 + columnOffset;

        output.Values[2 * pixel].Should().BeGreaterThanOrEqualTo(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
        output.History.Should().Equal(output.Uploaded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SharingDoesNotCrossAnInterveningReceiverGap(int rowOffset)
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 1);
        int pixel = (Height / 2 + rowOffset) * Width + Width / 2 - 2;

        output.Values[2 * pixel].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MatchingEndpointsDoNotShareAcrossAnInterveningDepthDiscontinuity(int rowOffset)
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 2);
        int pixel = (Height / 2 + rowOffset) * Width + Width / 2 - 2;

        output.Values[2 * pixel].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void ASmallDepthStepDoesNotBorrowLightFromTheOtherReceiver()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 3);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        output.Values[left].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void AShadingNormalBoundaryDoesNotBorrowLightFromTheOtherReceiver()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 4);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        output.Values[left].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void ATiltedContinuousReceiverStillReconstructsThePeakAndItsImmediateNeighbor()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 5);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        output.Values[left].Should().BeGreaterThan(0);
        output.Values[left + 2].Should().BeLessThan(output.Values[left + 3] * 0.25f);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void AGeometricNormalBoundaryDoesNotBorrowLightFromTheOtherReceiver()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: 6);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        output.Values[left].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void NearbyReflectionHitsDoNotSpreadOutsideTheirProjectedFootprint()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitDistance: 0.01f);
        int center = 2 * (Height / 2 * Width + Width / 2);

        output.Values[center].Should().Be(output.Values[center + 1]);
        output.Values[center - 2].Should().Be(0);
    }

    [Fact]
    public void FartherReflectionHitsPermitMoreSharingAtTheSameRoughness()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) nearby = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitDistance: 1);
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) distant = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitDistance: 300);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        distant.Values[left].Should().BeGreaterThan(nearby.Values[left]);
    }

    [Fact]
    public void NearMirrorReflectionsDoNotSpreadEvenWhenTheHitIsDistant()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.045f, sharing: true, boundary: false, hitDistance: 300);
        int center = 2 * (Height / 2 * Width + Width / 2);

        output.Values[center].Should().Be(output.Values[center + 1]);
        output.Values[center - 2].Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(65504)]
    public void AnUnknownOrSaturatedHitDistanceDoesNotInventASharingFootprint(float hitDistance)
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitDistance: hitDistance);
        int center = 2 * (Height / 2 * Width + Width / 2);

        output.Values[center].Should().Be(output.Values[center + 1]);
        output.Values[center - 2].Should().Be(0);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void BothReceiversMustAdmitTheExchangeWithinTheirOwnFootprint(int surfaceCase)
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, surfaceCase: surfaceCase);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        output.Values[left].Should().Be(0);
        output
            .Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    [Fact]
    public void SharingLeavesTheExistingHitDistanceGuideUntouched()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) output = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitDistance: 300);

        output.Samples.Where((_, scalar) => scalar % 4 == 3).Should().AllBeEquivalentTo(300f);
        output.History.Should().Equal(output.Uploaded);
    }

    [Fact]
    public void WeakDistanceSupportReducesSharingWithoutChangingItsMeasuredDistance()
    {
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) strong = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitSupport: 1);
        (
            float[] Values,
            float[] History,
            float[] Uploaded,
            double SharedSum,
            double UnfilteredSum,
            float[] Samples
        ) weak = DrawPulse(roughness: 0.2f, sharing: true, boundary: false, hitSupport: 0.25f);
        int left = 2 * (Height / 2 * Width + Width / 2 - 1);

        weak.Values[left].Should().BeGreaterThan(0);
        weak.Values[left].Should().BeLessThan(strong.Values[left] * 0.5f);
        weak.Values.Where((_, scalar) => scalar % 2 == 0)
            .Should()
            .OnlyContain(value => float.IsFinite(value) && value >= 0);
    }

    private static (
        float[] Values,
        float[] History,
        float[] Uploaded,
        double SharedSum,
        double UnfilteredSum,
        float[] Samples
    ) DrawPulse(
        float roughness,
        bool sharing,
        bool boundary,
        int surfaceCase = 0,
        float hitDistance = 300,
        float hitSupport = 1
    )
    {
        string? previous = Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_SHARING");
        Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_SHARING", sharing ? "1" : null);
        using VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            cascadedLights: 0,
            deformation: false
        );
        try
        {
            MeshData geometry = Primitives.Box(new(0.5f));
            MeshId mesh = renderer.CreateMesh(geometry);
            MaterialId right = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: roughness));
            MaterialId left = boundary
                ? renderer.CreateMaterial(MaterialDesc.Color(new(new Vector3(0.25f), 1), roughness: roughness))
                : right;
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1, 0.4f));
            scene.Camera = scene.Camera with { VerticalFov = 0.25f };
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: mesh,
                    material: left,
                    position: new(-30, 0, -20),
                    scale: new(60, 80, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 1
                )
            );
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: mesh,
                    material: right,
                    position: new(30, 0, -20),
                    scale: new(60, 80, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            var pulse = new PulsePass(
                renderer: renderer,
                target: renderer.ScreenTraceSignals.Specular.Slow(0),
                surfaceCase: surfaceCase,
                hitDistance: hitDistance,
                hitSupport: hitSupport
            );
            renderer.Graph.Add(FrameGraph.Stage.Composite, pulse);
            var probe = ReflectionComparisonProbe.On(renderer, geometry, includeReconstruction: true);
            renderer.Render(scene, 1);
            float[] values = probe.Read(8);
            double shared = 0;
            double original = 0;
            for (int pixel = 0; pixel < Width * Height; pixel++)
            {
                shared += values[2 * pixel];
                original += values[2 * pixel + 1];
            }
            Gpu.AssertClean(renderer);
            return (
                values,
                renderer.ReadImage(pulse.Target),
                pulse.Uploaded,
                shared,
                original,
                renderer.ReadImage(renderer.ScreenTraceSignals.Specular.Sample(0))
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_SHARING", previous);
        }
    }

    private sealed unsafe class PulsePass(
        VulkanRenderer renderer,
        GpuImage target,
        int surfaceCase,
        float hitDistance,
        float hitSupport
    ) : RenderPass
    {
        private VulkanDevice? device;
        private ShaderLibrary? shaders;
        private Pipeline pipeline;
        public GpuImage Target => target;
        public float[] Uploaded { get; } = new float[Width * Height * 4];
        public override string Name => "reflection sharing test pulse";

        public override void Prepare(FrameContext context)
        {
            device = context.Device;
            shaders = new ShaderLibrary(device, typeof(ReflectionSharingTests).Assembly);
            pipeline = new GraphicsPipelineDesc
            {
                Name = Name,
                VertexShader = "lighting.vert",
                FragmentShader = renderer.ScreenTraceSignals.Specular.HistoryShader("reflection_sharing_pulse.frag"),
                Layout = context.Layouts.CompositeLayout,
                ColorFormats = [RenderTargets.DistanceFormat, RenderTargets.NormalFormat],
                CullMode = CullModeFlags.None,
                Constants = renderer.ShadingConstants,
            }.Create(device, shaders);
            int center = 4 * (Height / 2 * Width + Width / 2);
            for (int pixel = 0; pixel < Width * Height; pixel++)
                Uploaded[4 * pixel + 3] = 1;
            for (int channel = 0; channel < 3; channel++)
                Uploaded[center + channel] = 90;
        }

        public override void Record(FrameContext context)
        {
            GpuImage distance = context.Targets.GBuffer[4];
            GpuImage normal = context.Targets.GBuffer[1];
            GpuImage sample = renderer.ScreenTraceSignals.Specular.Sample(0);
            GpuImage guide = renderer.ScreenTraceSignals.Specular.ReflectionGuide(0);
            context.Barriers.Image(target, ImageUse.StorageFragment);
            context.Barriers.Image(sample, ImageUse.StorageFragment);
            context.Barriers.Image(guide, ImageUse.StorageFragment);
            context.Barriers.Image(distance, ImageUse.ColorAttachment, discard: true);
            context.Barriers.Image(normal, ImageUse.ColorAttachment, discard: true);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            float* fixtureValues = stackalloc float[3];
            *(int*)fixtureValues = surfaceCase;
            fixtureValues[1] = hitDistance;
            fixtureValues[2] = hitSupport;
            context.Vk.CmdPushConstants(
                commandBuffer: context.CommandBuffer,
                layout: context.Layouts.CompositeLayout,
                stageFlags: ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
                offset: 0,
                size: 3 * sizeof(float),
                pValues: fixtureValues
            );
            context.DrawSceneFullScreen(
                pipeline: pipeline,
                layout: context.Layouts.CompositeLayout,
                passSet: renderer.CompositeLightSet,
                targets: [(distance, AttachmentLoadOp.DontCare), (normal, AttachmentLoadOp.DontCare)]
            );
            context.Barriers.Image(sample, ImageUse.StorageFragment);
            context.Barriers.Image(guide, ImageUse.StorageFragment);
            context.Barriers.Image(target, ImageUse.StorageFragment);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
        }

        public override void Dispose()
        {
            shaders?.Dispose();
            device?.Vk.DestroyPipeline(device.Device, pipeline, null);
            base.Dispose();
        }
    }
}

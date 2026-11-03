using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The host calculations of <see cref="Fsr3Upscaler"/> against the golden values that tools/fsr3-golden/ prints from
/// AMD's own host code of FSR 3.1.4 (docs/plans/temporal-renderer.md, "The host"): the jitter, the constants of each
/// frame, the luminance pyramid's constants, the dispatches, the reactive mask generator and the images. A value from
/// AMD's math library, such as the tangent of the field of view, may differ by one unit in the last place on another C
/// library, so those compare within one.
/// </summary>
public sealed class Fsr3HostTests
{
    // AMD's pipeline names (ffx_fsr3upscaler.cpp, createPipelineStates), which the golden jobs carry.
    private static readonly Dictionary<Fsr3Pass, string> AmdNames = new()
    {
        [Fsr3Pass.GenerateReactive] = "FSR3-GEN_REACTIVE",
        [Fsr3Pass.PrepareInputs] = "FSR3-PREPARE-INPUTS",
        [Fsr3Pass.LumaPyramid] = "FSR3-LUMA-PYRAMID",
        [Fsr3Pass.ShadingChangePyramid] = "FSR3-SHADING-CHANGE-PYRAMID",
        [Fsr3Pass.ShadingChange] = "FSR3-SHADING-CHANGE",
        [Fsr3Pass.PrepareReactivity] = "FSR3-PREPARE-REACTIVITY",
        [Fsr3Pass.LumaInstability] = "FSR3-LUMA-INSTABILITY",
        [Fsr3Pass.Accumulate] = "FSR3-ACCUMULATE",
    };

    // The Vulkan format of each of AMD's surface formats, as AMD's Vulkan backend maps them (ffx_vk.cpp,
    // ffxGetVkFormatFromSurfaceFormat).
    private static readonly Dictionary<string, Format> VulkanFormats = new()
    {
        ["R8_UNORM"] = Format.R8Unorm,
        ["R16_FLOAT"] = Format.R16Sfloat,
        ["R16G16_FLOAT"] = Format.R16G16Sfloat,
        ["R16G16B16A16_FLOAT"] = Format.R16G16B16A16Sfloat,
        ["R32_UINT"] = Format.R32Uint,
        ["R32_FLOAT"] = Format.R32Sfloat,
        ["R8G8B8A8_UNORM"] = Format.R8G8B8A8Unorm,
        ["R32G32_FLOAT"] = Format.R32G32Sfloat,
        ["R32G32B32A32_FLOAT"] = Format.R32G32B32A32Sfloat,
    };

    // The constants that AMD's host computes with its math library's tangent, cosine and sine.
    private static readonly HashSet<string> Transcendental =
    [
        "tanHalfFOV",
        "deviceToViewDepth2",
        "deviceToViewDepth3",
    ];

    [Fact]
    public void ThePhaseCountOfEachRenderAndOutputWidthIsAmds()
    {
        foreach (Fsr3Golden.PhaseCountCase golden in Fsr3Golden.Values.PhaseCounts)
            Fsr3Upscaler
                .JitterPhaseCount(renderWidth: (uint)golden.RenderWidth, outputWidth: (uint)golden.OutputWidth)
                .Should()
                .Be(golden.PhaseCount, $"AMD's count for {golden.RenderWidth} to {golden.OutputWidth}");
    }

    [Fact]
    public void TheJitterOffsetOfEachIndexAndPhaseCountIsAmds()
    {
        foreach (Fsr3Golden.OffsetCase golden in Fsr3Golden.Values.Offsets)
            Fsr3Upscaler
                .JitterOffset(index: golden.Index, phaseCount: golden.PhaseCount)
                .Should()
                .Be(new Vector2(golden.X, golden.Y), $"AMD's offset of index {golden.Index} of {golden.PhaseCount}");
    }

    [Fact]
    public void TheConstantsHaveTheLayoutOfAmdsConstantBuffer()
    {
        Fsr3Golden.Layout golden = Fsr3Golden.Values.ConstantsLayout;

        int[] layout =
        [
            Marshal.SizeOf<Fsr3Constants>(),
            (int)Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.JitterOffsetX)),
            (int)Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.TanHalfFov)),
            (int)Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.DeltaPreExposure)),
        ];

        layout.Should().Equal(golden.Size, golden.JitterOffset, golden.TanHalfFov, golden.DeltaPreExposure);
    }

    [Fact]
    public void TheConstantsOfEveryFrameAreAmdsApartFromThePreExposureRatioThatTheGpuWrites()
    {
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            Fsr3Constants constants = Fsr3Constants.Created(MaxRenderSize(sequence), OutputSize(sequence));
            for (int index = 0; index < sequence.Frames.Length; index++)
            {
                Fsr3Golden.Frame golden = sequence.Frames[index];
                constants = Fsr3Constants.Next(
                    last: constants,
                    frame: FrameOf(sequence, golden),
                    resetAccumulation: golden.Reset || index == 0
                );

                Dictionary<string, float> actual = Fields(constants);
                foreach ((string name, float expected) in golden.Constants)
                {
                    if (name == "deltaPreExposure")
                        continue;
                    string because = $"AMD's {name} of frame {index} of {sequence.Name}";
                    if (Transcendental.Contains(name))
                        actual[name].Should().BeApproximately(expected, OneUlp(expected), because);
                    else
                        actual[name].Should().Be(expected, because);
                }
            }
        }
    }

    [Fact]
    public void TheLuminancePyramidsConstantsOfEveryFrameAreAmds()
    {
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        foreach (Fsr3Golden.Frame golden in sequence.Frames)
        {
            Fsr3SpdConstants spd = Fsr3SpdConstants.For(new(golden.RenderWidth, golden.RenderHeight));

            Words(spd).Should().Equal(golden.SpdWords, $"AMD's SPD constants at {golden.RenderWidth} x {golden.RenderHeight}");
        }
    }

    [Fact]
    public void TheReactiveMaskGeneratorTakesTheValuesOfAmdsSample()
    {
        uint[] golden = Fsr3Golden.Values.Sequences[0].Frames[0].ReactiveWords;

        Words(Fsr3ReactiveConstants.Sample).Should().Equal(golden);
    }

    [Fact]
    public void EveryPassOfEveryFrameDispatchesAmdsGroups()
    {
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            Fsr3Constants constants = Fsr3Constants.Created(MaxRenderSize(sequence), OutputSize(sequence));
            for (int index = 0; index < sequence.Frames.Length; index++)
            {
                Fsr3Golden.Frame golden = sequence.Frames[index];
                constants = Fsr3Constants.Next(
                    last: constants,
                    frame: FrameOf(sequence, golden),
                    resetAccumulation: golden.Reset || index == 0
                );

                foreach (Fsr3Golden.Job job in golden.Jobs.Where(job => job.Kind == "dispatch"))
                {
                    Fsr3Pass pass = AmdNames.Single(name => name.Value == job.Label).Key;
                    Fsr3Upscaler
                        .Groups(pass, constants)
                        .Should()
                        .Be((job.X, job.Y), $"AMD's groups of {job.Label} in frame {index} of {sequence.Name}");
                }
            }
        }
    }

    [Fact]
    public void TheUpscaleDispatchesItsPassesInAmdsOrder()
    {
        string[] golden = Fsr3Golden
            .Values.Sequences[0]
            .Frames[1]
            .Jobs.Where(job => job.Kind == "dispatch" && job.Label != AmdNames[Fsr3Pass.GenerateReactive])
            .Select(job => job.Label)
            .ToArray();

        Fsr3Upscaler.UpscalePasses.ToArray().Select(pass => AmdNames[pass]).Should().Equal(golden);
    }

    [Fact]
    public void EveryFrameClearsAmdsImagesWithAmdsValuesInAmdsOrder()
    {
        // AMD's discard jobs do nothing in its Vulkan backend (ffx_vk.cpp, executeGpuJobs), so the host records none.
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            for (int index = 0; index < sequence.Frames.Length; index++)
            {
                Fsr3Golden.Frame golden = sequence.Frames[index];
                var amds = golden
                    .Jobs.Where(job => job.Kind == "clear")
                    .Select(job =>
                        (Target: job.Target!, Color: new Vector4(x: job.Color![0], y: job.Color[1], z: job.Color[2], w: job.Color[3]))
                    );

                Fsr3Upscaler
                    .Clears(firstExecution: index == 0, reset: golden.Reset, odd: index % 2 == 1)
                    .Select(clear => (Target: $"FSR3UPSCALER_{clear.Target}", clear.Color))
                    .Should()
                    .Equal(amds, $"AMD's clears of frame {index} of {sequence.Name}");
            }
        }
    }

    [Fact]
    public void ADeviceWhoseComputeStageSeesFewerStorageImagesThanAPassBindsIsRefused()
    {
        // The shading change pyramid binds the atomic counter and six levels of the luminance pyramid.
        Fsr3Upscaler.StorageImageProblem(maxPerStageStorageImages: 6).Should().NotBeNull();
        Fsr3Upscaler.StorageImageProblem(maxPerStageStorageImages: 7).Should().BeNull();
    }

    [Fact]
    public void TheImagesHaveAmdsNamesFormatsAndSizesWithoutTheLanczosTable()
    {
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            // The reference Lanczos of the portable build binds no table (ThirdParty/Fsr3/README.md).
            var golden = sequence
                .Resources.Concat(sequence.SharedResources)
                .Where(resource => resource.Name != "FSR3UPSCALER_LanczosLutData")
                .Select(resource =>
                    new Fsr3ImageDescription(
                        Name: resource.Name,
                        Format: VulkanFormats[resource.Format],
                        Extent: new(resource.Width, resource.Height)
                    )
                    {
                        MipLevels = FullChainWhenZero(resource),
                    }
                );

            Fsr3Upscaler
                .ImageDescriptions(MaxRenderSize(sequence), OutputSize(sequence))
                .Should()
                .Equal(golden, $"AMD's images of {sequence.Name}");
        }
    }

    // AMD's Vulkan backend gives a mip count of 0 the full chain (ffx_vk.cpp, CreateResourceVK).
    private static uint FullChainWhenZero(Fsr3Golden.Resource resource) =>
        resource.MipCount != 0
            ? resource.MipCount
            : 1 + (uint)Math.Floor(Math.Log2(Math.Max(resource.Width, resource.Height)));

    private static Extent2D MaxRenderSize(Fsr3Golden.Sequence sequence) =>
        new(sequence.MaxRenderWidth, sequence.MaxRenderHeight);

    private static Extent2D OutputSize(Fsr3Golden.Sequence sequence) => new(sequence.OutputWidth, sequence.OutputHeight);

    private static Fsr3Frame FrameOf(Fsr3Golden.Sequence sequence, Fsr3Golden.Frame golden) =>
        new()
        {
            RenderSize = new(golden.RenderWidth, golden.RenderHeight),
            Jitter = new(golden.JitterX, golden.JitterY),
            FrameMilliseconds = golden.FrameMilliseconds,
            NearMeters = sequence.NearMeters,
            VerticalFov = golden.VerticalFov,
            Reset = golden.Reset,
        };

    private static float OneUlp(float value) => MathF.BitIncrement(MathF.Abs(value)) - MathF.Abs(value);

    private static uint[] Words<T>(T value)
        where T : unmanaged
    {
        uint[] words = new uint[Marshal.SizeOf<T>() / sizeof(uint)];
        MemoryMarshal.Write(MemoryMarshal.AsBytes(words.AsSpan()), in value);
        return words;
    }

    // The constants under AMD's field names (Fsr3UpscalerConstants), as the golden file names them.
    private static Dictionary<string, float> Fields(in Fsr3Constants c) =>
        new()
        {
            ["renderSizeX"] = c.RenderSizeX,
            ["renderSizeY"] = c.RenderSizeY,
            ["previousFrameRenderSizeX"] = c.PreviousFrameRenderSizeX,
            ["previousFrameRenderSizeY"] = c.PreviousFrameRenderSizeY,
            ["upscaleSizeX"] = c.UpscaleSizeX,
            ["upscaleSizeY"] = c.UpscaleSizeY,
            ["previousFrameUpscaleSizeX"] = c.PreviousFrameUpscaleSizeX,
            ["previousFrameUpscaleSizeY"] = c.PreviousFrameUpscaleSizeY,
            ["maxRenderSizeX"] = c.MaxRenderSizeX,
            ["maxRenderSizeY"] = c.MaxRenderSizeY,
            ["maxUpscaleSizeX"] = c.MaxUpscaleSizeX,
            ["maxUpscaleSizeY"] = c.MaxUpscaleSizeY,
            ["deviceToViewDepth0"] = c.DeviceToViewDepth0,
            ["deviceToViewDepth1"] = c.DeviceToViewDepth1,
            ["deviceToViewDepth2"] = c.DeviceToViewDepth2,
            ["deviceToViewDepth3"] = c.DeviceToViewDepth3,
            ["jitterOffsetX"] = c.JitterOffsetX,
            ["jitterOffsetY"] = c.JitterOffsetY,
            ["previousFrameJitterOffsetX"] = c.PreviousFrameJitterOffsetX,
            ["previousFrameJitterOffsetY"] = c.PreviousFrameJitterOffsetY,
            ["motionVectorScaleX"] = c.MotionVectorScaleX,
            ["motionVectorScaleY"] = c.MotionVectorScaleY,
            ["downscaleFactorX"] = c.DownscaleFactorX,
            ["downscaleFactorY"] = c.DownscaleFactorY,
            ["motionVectorJitterCancellationX"] = c.MotionVectorJitterCancellationX,
            ["motionVectorJitterCancellationY"] = c.MotionVectorJitterCancellationY,
            ["tanHalfFOV"] = c.TanHalfFov,
            ["jitterPhaseCount"] = c.JitterPhaseCount,
            ["deltaTime"] = c.DeltaTime,
            ["deltaPreExposure"] = c.DeltaPreExposure,
            ["viewSpaceToMetersFactor"] = c.ViewSpaceToMetersFactor,
            ["frameIndex"] = c.FrameIndex,
            ["velocityFactor"] = c.VelocityFactor,
            ["reactivenessScale"] = c.ReactivenessScale,
            ["shadingChangeScale"] = c.ShadingChangeScale,
            ["accumulationAddedPerFrame"] = c.AccumulationAddedPerFrame,
            ["minDisocclusionAccumulation"] = c.MinDisocclusionAccumulation,
        };
}

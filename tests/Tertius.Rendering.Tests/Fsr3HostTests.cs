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

    // What each variable of AMD's passes holds, by AMD's tables of names (ffx_fsr3upscaler.cpp, srvTextureBindingTable,
    // uavTextureBindingTable and constantBufferBindingTable). AMD's dispatch gives a pair's sampled name the image that
    // the frame reads and its storage name the image that the frame writes.
    private static readonly Dictionary<string, Fsr3Slot> AmdSlots = new()
    {
        ["r_input_color_jittered"] = Fsr3Slot.InputColor,
        ["r_input_opaque_only"] = Fsr3Slot.InputOpaqueOnly,
        ["r_input_motion_vectors"] = Fsr3Slot.InputMotionVectors,
        ["r_input_depth"] = Fsr3Slot.InputDepth,
        ["r_input_exposure"] = Fsr3Slot.InputExposure,
        ["r_reactive_mask"] = Fsr3Slot.ReactiveMask,
        ["rw_output_autoreactive"] = Fsr3Slot.ReactiveMask,
        ["r_transparency_and_composition_mask"] = Fsr3Slot.TransparencyAndCompositionMask,
        ["rw_upscaled_output"] = Fsr3Slot.UpscaledOutput,
        ["r_reconstructed_previous_nearest_depth"] = Fsr3Slot.ReconstructedPreviousNearestDepth,
        ["rw_reconstructed_previous_nearest_depth"] = Fsr3Slot.ReconstructedPreviousNearestDepth,
        ["r_dilated_motion_vectors"] = Fsr3Slot.DilatedMotionVectors,
        ["rw_dilated_motion_vectors"] = Fsr3Slot.DilatedMotionVectors,
        ["r_dilated_depth"] = Fsr3Slot.DilatedDepth,
        ["rw_dilated_depth"] = Fsr3Slot.DilatedDepth,
        ["r_internal_upscaled_color"] = Fsr3Slot.InternalUpscaledColorRead,
        ["rw_internal_upscaled_color"] = Fsr3Slot.InternalUpscaledColorWrite,
        ["r_accumulation"] = Fsr3Slot.AccumulationRead,
        ["rw_accumulation"] = Fsr3Slot.AccumulationWrite,
        ["r_luma_history"] = Fsr3Slot.LumaHistoryRead,
        ["rw_luma_history"] = Fsr3Slot.LumaHistoryWrite,
        ["r_current_luma"] = Fsr3Slot.CurrentLuma,
        ["rw_current_luma"] = Fsr3Slot.CurrentLuma,
        ["r_previous_luma"] = Fsr3Slot.PreviousLuma,
        ["r_farthest_depth"] = Fsr3Slot.FarthestDepth,
        ["rw_farthest_depth"] = Fsr3Slot.FarthestDepth,
        ["r_farthest_depth_mip1"] = Fsr3Slot.FarthestDepthMip1,
        ["rw_farthest_depth_mip1"] = Fsr3Slot.FarthestDepthMip1,
        ["r_luma_instability"] = Fsr3Slot.LumaInstability,
        ["rw_luma_instability"] = Fsr3Slot.LumaInstability,
        ["r_shading_change"] = Fsr3Slot.ShadingChange,
        ["rw_shading_change"] = Fsr3Slot.ShadingChange,
        ["r_new_locks"] = Fsr3Slot.NewLocks,
        ["rw_new_locks"] = Fsr3Slot.NewLocks,
        ["r_dilated_reactive_masks"] = Fsr3Slot.DilatedReactiveMasks,
        ["rw_dilated_reactive_masks"] = Fsr3Slot.DilatedReactiveMasks,
        ["r_spd_mips"] = Fsr3Slot.SpdMips,
        ["rw_spd_mip0"] = Fsr3Slot.SpdMip0,
        ["rw_spd_mip1"] = Fsr3Slot.SpdMip1,
        ["rw_spd_mip2"] = Fsr3Slot.SpdMip2,
        ["rw_spd_mip3"] = Fsr3Slot.SpdMip3,
        ["rw_spd_mip4"] = Fsr3Slot.SpdMip4,
        ["rw_spd_mip5"] = Fsr3Slot.SpdMip5,
        ["rw_spd_global_atomic"] = Fsr3Slot.SpdAtomic,
        ["r_frame_info"] = Fsr3Slot.FrameInfo,
        ["rw_frame_info"] = Fsr3Slot.FrameInfo,
        ["cbFSR3Upscaler"] = Fsr3Slot.Constants,
        ["cbRCAS"] = Fsr3Slot.RcasConstants,
        ["r_rcas_input"] = Fsr3Slot.RcasInput,
        ["cbSPD"] = Fsr3Slot.SpdConstants,
        ["cbGenerateReactive"] = Fsr3Slot.ReactiveConstants,
        ["s_LinearClamp"] = Fsr3Slot.LinearClamp,
    };

    // The least per-stage limits that the spec guarantees (ARCHITECTURE.md, "Required device features").
    private static readonly PhysicalDeviceLimits LeastLimits = new()
    {
        MaxPerStageDescriptorStorageBuffers = 4,
        MaxPerStageDescriptorStorageImages = 4,
        MaxPerStageDescriptorSampledImages = 16,
    };

    // The limits of a device that has room for every pass of FSR and for its clear step.
    private static readonly PhysicalDeviceLimits RoomyLimits = LeastLimits with
    {
        MaxPerStageDescriptorStorageImages = 15,
    };

    // The constants that AMD's host computes with its math library's tangent, cosine and sine.
    private static readonly HashSet<string> Transcendental = ["tanHalfFOV", "deviceToViewDepth2", "deviceToViewDepth3"];

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

    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    public void AnOutputNarrowerThanTheMinimumActiveWidthStillHasAJitterPhase(uint outputWidth)
    {
        int phases = Fsr3Upscaler.JitterPhaseCount(renderWidth: 8, outputWidth: outputWidth);
        phases.Should().Be(1);
        Vector2 jitter = Fsr3Upscaler.JitterOffset(index: 3, phaseCount: phases);
        float.IsFinite(jitter.X).Should().BeTrue();
        float.IsFinite(jitter.Y).Should().BeTrue();
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
            var constants = Fsr3Constants.Created(MaxRenderSize(sequence), OutputSize(sequence));
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
            var spd = Fsr3SpdConstants.For(new(golden.RenderWidth, golden.RenderHeight));

            Words(spd)
                .Should()
                .Equal(golden.SpdWords, $"AMD's SPD constants at {golden.RenderWidth} x {golden.RenderHeight}");
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
            var constants = Fsr3Constants.Created(MaxRenderSize(sequence), OutputSize(sequence));
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
        string[] golden =
        [
            .. Fsr3Golden
                .Values.Sequences[0]
                .Frames[1]
                .Jobs.Where(job => job.Kind == "dispatch" && job.Label != AmdNames[Fsr3Pass.GenerateReactive])
                .Select(job => job.Label),
        ];

        Fsr3Upscaler.UpscalePasses.ToArray().Select(pass => AmdNames[pass]).Should().Equal(golden);
    }

    [Fact]
    public void EveryClearOfAmdsOnAnImageThatTheClearStepDoesNotZeroIsATransferClearWithAmdsValueInAmdsOrder()
    {
        // AMD's discard jobs do nothing in its Vulkan backend (ffx_vk.cpp, executeGpuJobs), so the host records none.
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            for (int index = 0; index < sequence.Frames.Length; index++)
            {
                Fsr3Golden.Frame golden = sequence.Frames[index];
                IEnumerable<(string Target, Vector4 Color)> amds = AmdsClears(golden)
                    .Where(clear => !ZeroedNames.Contains(clear.Target));

                Fsr3Upscaler
                    .Clears(firstExecution: index == 0, reset: golden.Reset, odd: index % 2 == 1)
                    .Select(clear => (Target: $"FSR3UPSCALER_{clear.Target}", clear.Color))
                    .Should()
                    .Equal(amds, $"AMD's transfer clears of frame {index} of {sequence.Name}");
            }
        }
    }

    [Fact]
    public void EveryFrameOfAmdsWritesZerosIntoEachImageThatTheClearStepZeroesAndNoOtherValue()
    {
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            for (int index = 0; index < sequence.Frames.Length; index++)
            {
                (string Target, Vector4 Color)[] zeroed =
                [
                    .. AmdsClears(sequence.Frames[index]).Where(clear => ZeroedNames.Contains(clear.Target)),
                ];

                // A reset frame clears the luminance pyramid twice.
                zeroed
                    .Select(clear => clear.Target)
                    .Distinct()
                    .Should()
                    .BeEquivalentTo(ZeroedNames, $"AMD clears each of them in frame {index} of {sequence.Name}");
                zeroed
                    .Select(clear => clear.Color)
                    .Should()
                    .AllSatisfy(color => color.Should().Be(Vector4.Zero, "the clear step writes zeros only"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFrameAfterTheFirstWithoutAResetClearsNoImageWithATransfer(bool odd) =>
        // Each transfer clear is a render pass of its own under MoltenVK, so a steady frame clears in the clear step only.
        Fsr3Upscaler.Clears(firstExecution: false, reset: false, odd: odd).Should().BeEmpty();

    [Fact]
    public void TheClearStepBindsAStorageImageAtEachBindingThatItsCompiledModuleDeclares()
    {
        // glslc's optimizer strips the names, so the GPU test of the clear step pins which image each binding holds.
        byte[] spirv = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, Fsr3Upscaler.ClearStepShader);

        Fsr3Bindings
            .ClearStep.Should()
            .AllSatisfy(binding => binding.Type.Should().Be(DescriptorType.StorageImage))
            .And.Subject.Select(binding => binding.Binding)
            .Should()
            .BeEquivalentTo(
                Spirv.BindingNumbers(spirv),
                "the bindings that glslc kept in the module of the clear step"
            );
    }

    [Fact]
    public void EveryPassBindsWhatItsCompiledModuleDeclaresUnderAmdsNames()
    {
        foreach (Fsr3Pass pass in Enum.GetValues<Fsr3Pass>())
        {
            byte[] spirv = ShaderLibrary.LoadSpirv(ShaderLibrary.EngineShaders, Fsr3Upscaler.ShaderOf(pass));
            IOrderedEnumerable<(uint Binding, Fsr3Slot Slot, DescriptorType Type)> declared = Spirv
                .Bindings(spirv)
                .Select(binding => (binding.Binding, Slot: AmdSlots[binding.Name], Type: TypeByName(binding.Name)))
                .OrderBy(binding => binding.Binding);

            Fsr3Bindings
                .Of(pass)
                .Select(binding => (binding.Binding, binding.Slot, binding.Type))
                .OrderBy(binding => binding.Binding)
                .Should()
                .Equal(declared, $"the bindings that glslang kept in the module of {pass}");
        }
    }

    [Fact]
    public void EachStorageSlotOfTheLuminancePyramidWritesItsOwnLevelAndTheSampledSlotAllOfThem()
    {
        // A storage descriptor of the whole chain writes level 0, which Vulkan allows, so no validation message shows
        // a wrong level.
        Fsr3Slot[] slots =
        [
            Fsr3Slot.SpdMips,
            Fsr3Slot.SpdMip0,
            Fsr3Slot.SpdMip1,
            Fsr3Slot.SpdMip2,
            Fsr3Slot.SpdMip3,
            Fsr3Slot.SpdMip4,
            Fsr3Slot.SpdMip5,
            Fsr3Slot.SpdMip6,
            Fsr3Slot.SpdMip12,
            Fsr3Slot.SpdAtomic,
        ];

        slots.Select(Fsr3Bindings.SpdLevel).Should().Equal(null, 0u, 1u, 2u, 3u, 4u, 5u, 6u, 12u, null);
    }

    [Fact]
    public void ADeviceWhoseComputeStageSeesFewerStorageImagesThanAPassBindsIsRefused()
    {
        // The shading change pyramid binds the atomic counter and six levels of the luminance pyramid.
        Action check = () => CheckLimits(RoomyLimits with { MaxPerStageDescriptorStorageImages = 6 });

        check.Should().Throw<NotSupportedException>().WithMessage("*7 storage images*");
    }

    [Fact]
    public void ADeviceWhoseComputeStageSeesFewerSampledImagesThanAPassBindsIsRefused()
    {
        // Prepare reactivity samples nine images.
        Action check = () => CheckLimits(RoomyLimits with { MaxPerStageDescriptorSampledImages = 8 });

        check.Should().Throw<NotSupportedException>().WithMessage("*9 sampled images*");
    }

    [Fact]
    public void ADeviceWhoseComputeStageSeesFewerStorageImagesThanTheClearStepBindsIsRefused()
    {
        // The clear step binds the reconstructed depth, the atomic counter and 13 levels of the luminance pyramid.
        Action check = () => CheckLimits(RoomyLimits with { MaxPerStageDescriptorStorageImages = 14 });

        check.Should().Throw<NotSupportedException>().WithMessage("*15 storage images*");
    }

    [Fact]
    public void ADeviceWhoseComputeStageSeesFifteenStorageImagesAndTheSpecsLeastOfTheOthersCanRunEveryPass()
    {
        Action check = () => CheckLimits(RoomyLimits);

        check.Should().NotThrow();
    }

    [Fact]
    public void ADeviceWhoseComputeStageSeesFewerStorageBuffersThanTheExposureStepBindsIsRefused()
    {
        // The exposure step binds the exposure buffer and FSR's constants, and none of AMD's passes binds one.
        Action check = () => CheckLimits(RoomyLimits with { MaxPerStageDescriptorStorageBuffers = 1 });

        check.Should().Throw<NotSupportedException>().WithMessage("*2 storage buffers*");
    }

    [Theory]
    [InlineData(0u, 72u)]
    [InlineData(96u, 0u)]
    public void AnOutputSizeWithASideOfNoPixelsIsRefused(uint width, uint height)
    {
        Action check = () =>
            Fsr3Upscaler.CheckArguments(
                limits: RoomyLimits,
                maxRenderSize: new(64, 48),
                outputSize: new(width, height),
                framesInFlight: 2
            );

        check.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AnUpscalerWithNoFrameInFlightIsRefused()
    {
        // The upscaler keeps one set of each pass per frame in flight.
        Action check = () =>
            Fsr3Upscaler.CheckArguments(
                limits: RoomyLimits,
                maxRenderSize: new(64, 48),
                outputSize: new(96, 72),
                framesInFlight: 0
            );

        check.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(63u, 48u)]
    [InlineData(48u, 63u)]
    public void ALargestRenderSizeWhoseLongerSideIsBelow64PixelsIsRefused(uint width, uint height)
    {
        // The luminance pyramid at half of it would have fewer than the six levels that AMD's passes write.
        Action check = () =>
            Fsr3Upscaler.CheckArguments(
                limits: RoomyLimits,
                maxRenderSize: new(width, height),
                outputSize: new(96, 72),
                framesInFlight: 2
            );

        check.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(16384u, false)]
    [InlineData(16383u, true)]
    public void ALargestRenderSizeIsRefusedWhenItsLuminancePyramidHasMoreLevelsThanTheClearStepBinds(
        uint width,
        bool accepted
    )
    {
        // A longer side of 16,384 pixels gives the pyramid at half of it 14 levels, one more than the clear step's 13.
        Action check = () =>
            Fsr3Upscaler.CheckArguments(
                limits: RoomyLimits,
                maxRenderSize: new(width, 48),
                outputSize: new(96, 72),
                framesInFlight: 2
            );

        if (accepted)
            check.Should().NotThrow();
        else
            check.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*14 levels*");
    }

    [Fact]
    public void ALargestRenderSizeOneOfWhoseSidesIsOnePixelIsRefused()
    {
        // AMD's images at half the largest render size would have no pixels on that side.
        Action check = () =>
            Fsr3Upscaler.CheckArguments(
                limits: RoomyLimits,
                maxRenderSize: new(64, 1),
                outputSize: new(96, 72),
                framesInFlight: 2
            );

        check.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void TheImagesHaveAmdsNamesFormatsAndSizesWithoutTheLanczosTable()
    {
        foreach (Fsr3Golden.Sequence sequence in Fsr3Golden.Values.Sequences)
        {
            // The reference Lanczos of the portable build binds no table (ThirdParty/Fsr3/README.md).
            IEnumerable<Fsr3ImageDescription> golden = sequence
                .Resources.Concat(sequence.SharedResources)
                .Where(resource => resource.Name != "FSR3UPSCALER_LanczosLutData")
                .Select(resource => new Fsr3ImageDescription(
                    Name: resource.Name,
                    Format: VulkanFormats[resource.Format],
                    Extent: new(resource.Width, resource.Height)
                )
                {
                    MipLevels = FullChainWhenZero(resource),
                });

            Fsr3Upscaler
                .ImageDescriptions(MaxRenderSize(sequence), OutputSize(sequence))
                .Should()
                .Equal(golden, $"AMD's images of {sequence.Name}");
        }
    }

    // AMD's names of the images that the clear step zeroes in every frame.
    private static readonly string[] ZeroedNames =
    [
        .. Fsr3Upscaler.ZeroedEachFrame.Select(image => $"FSR3UPSCALER_{image}"),
    ];

    // AMD's clears of a frame, in AMD's order.
    private static IEnumerable<(string Target, Vector4 Color)> AmdsClears(Fsr3Golden.Frame golden) =>
        golden
            .Jobs.Where(job => job.Kind == "clear")
            .Select(job =>
                (
                    Target: job.Target!,
                    Color: new Vector4(x: job.Color![0], y: job.Color[1], z: job.Color[2], w: job.Color[3])
                )
            );

    // The descriptor type of a variable of AMD's passes by its name's prefix (ffx_fsr3upscaler_callbacks_glsl.h): r_ for
    // a sampled image, rw_ for a storage image, cb for a constant buffer and s_ for a sampler.
    private static DescriptorType TypeByName(string name) =>
        name switch
        {
            _ when name.StartsWith("rw_", StringComparison.Ordinal) => DescriptorType.StorageImage,
            _ when name.StartsWith("r_", StringComparison.Ordinal) => DescriptorType.SampledImage,
            _ when name.StartsWith("cb", StringComparison.Ordinal) => DescriptorType.UniformBuffer,
            _ when name.StartsWith("s_", StringComparison.Ordinal) => DescriptorType.Sampler,
            _ => throw new ArgumentException($"{name} is not one of AMD's names.", nameof(name)),
        };

    private static void CheckLimits(in PhysicalDeviceLimits limits) =>
        Fsr3Upscaler.CheckArguments(
            limits: limits,
            maxRenderSize: new(64, 48),
            outputSize: new(96, 72),
            framesInFlight: 2
        );

    // AMD's Vulkan backend gives a mip count of 0 the full chain (ffx_vk.cpp, CreateResourceVK).
    private static uint FullChainWhenZero(Fsr3Golden.Resource resource) =>
        resource.MipCount != 0
            ? resource.MipCount
            : 1 + (uint)Math.Floor(Math.Log2(Math.Max(resource.Width, resource.Height)));

    private static Extent2D MaxRenderSize(Fsr3Golden.Sequence sequence) =>
        new(sequence.MaxRenderWidth, sequence.MaxRenderHeight);

    private static Extent2D OutputSize(Fsr3Golden.Sequence sequence) =>
        new(sequence.OutputWidth, sequence.OutputHeight);

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

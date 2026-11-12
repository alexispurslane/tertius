using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>The dispatches of AMD's FSR 3.1.4 that the engine records.</summary>
internal enum Fsr3Pass
{
    GenerateReactive,
    PrepareInputs,
    LumaPyramid,
    ShadingChangePyramid,
    ShadingChange,
    PrepareReactivity,
    LumaInstability,
    Accumulate,
    Rcas,
}

/// <summary>One of FSR's own images: AMD's name, its format, its size and its mip levels.</summary>
internal readonly record struct Fsr3ImageDescription(string Name, Format Format, Extent2D Extent)
{
    public uint MipLevels { get; init; } = 1;
}

/// <summary>The constant buffer <c>cbSPD</c> of the two luminance pyramids, as AMD's host fills it.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Fsr3SpdConstants(
    uint Mips,
    uint NumWorkGroups,
    uint WorkGroupOffsetX,
    uint WorkGroupOffsetY,
    uint RenderSizeX,
    uint RenderSizeY
)
{
    /// <summary>
    /// AMD's <c>ffxSpdSetup</c> over the whole render size, with the mip count that it derives: the floor of the base-2
    /// logarithm of the longer side, at most 12.
    /// </summary>
    public static Fsr3SpdConstants For(Extent2D renderSize)
    {
        (uint x, uint y) = Groups(renderSize);
        uint longer = Math.Max(renderSize.Width, renderSize.Height);
        uint mips = (uint)MathF.Min(MathF.Floor(MathF.Log2(longer)), 12f);
        return new(
            Mips: mips,
            NumWorkGroups: x * y,
            WorkGroupOffsetX: 0,
            WorkGroupOffsetY: 0,
            RenderSizeX: renderSize.Width,
            RenderSizeY: renderSize.Height
        );
    }

    /// <summary>The workgroups of a pyramid over <paramref name="renderSize"/>: one per tile of 64 × 64 pixels.</summary>
    public static (uint X, uint Y) Groups(Extent2D renderSize) =>
        ((renderSize.Width - 1) / 64 + 1, (renderSize.Height - 1) / 64 + 1);
}

/// <summary>The constant buffer <c>cbGenerateReactive</c> of AMD's reactive mask generator.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct Fsr3ReactiveConstants(float Scale, float Threshold, float BinaryValue, uint Flags)
{
    /// <summary>The values of AMD's sample (samples/fsrapi/fsrapirendermodule.cpp), which ticket 5 tunes.</summary>
    public static Fsr3ReactiveConstants Sample =>
        new(Scale: 1f, Threshold: 0.2f, BinaryValue: 0.9f, Flags: ApplyTonemap | ApplyThreshold | UseComponentsMax);

    // AMD's flags of the generator (ffx_fsr3upscaler_resources.h).
    private const uint ApplyTonemap = 1;
    private const uint ApplyThreshold = 4;
    private const uint UseComponentsMax = 8;
}

/// <summary>A clear that AMD's host schedules: the image and the value.</summary>
internal readonly record struct Fsr3Clear(Fsr3Image Target, Vector4 Color);

/// <summary>
/// The images and values of one upscale. Every image is at least the largest render size, but the output, which is the
/// output size. The exposure buffer holds this frame's stops and the last frame's at bytes 0 and 4.
/// </summary>
internal readonly record struct Fsr3Inputs
{
    /// <summary>The jittered, pre-exposed HDR color: sampled.</summary>
    public required GpuImage Color { get; init; }

    /// <summary>The color before the transparent pass: sampled by the reactive mask generator.</summary>
    public required GpuImage OpaqueOnly { get; init; }

    /// <summary>The reactive mask, R8_UNORM, which the generator writes as a storage image and the upscale samples.</summary>
    public required GpuImage Reactive { get; init; }

    /// <summary>
    /// The transparency and composition mask, R8_UNORM, sampled, or null for AMD's default mask of 0. The renderer feeds
    /// the screen tracer's light-change mask (ticket t1-4hij).
    /// </summary>
    public required GpuImage? TransparencyAndComposition { get; init; }

    /// <summary>The device depth, reversed and infinite: sampled.</summary>
    public required GpuImage Depth { get; init; }

    /// <summary>The motion vectors without jitter in units of the render size, in xy: sampled.</summary>
    public required GpuImage Motion { get; init; }

    /// <summary>The upscaled color at the output size, written as a storage image.</summary>
    public required GpuImage Output { get; init; }

    /// <summary>The exposure buffer, read as a storage buffer.</summary>
    public required GpuBuffer Exposure { get; init; }

    /// <summary>This frame's sizes, jitter, camera and reset, from which FSR's constants derive.</summary>
    public required Fsr3Frame Frame { get; init; }
}

/// <summary>
/// AMD's FSR 3.1 upscaler over AMD's own shaders, with AMD's host calculations repeated (ARCHITECTURE.md, "Upscaling: FSR
/// 3.1"). It upscales the jittered, pre-exposed HDR color of the render size to the output size. Each
/// <see cref="Record"/> takes one frame's <see cref="Fsr3Inputs"/>. <see cref="JitterOffset"/> gives the jitter that the
/// frame draws with.
/// </summary>
internal sealed unsafe class Fsr3Upscaler : IDisposable
{
    // The side of the square of pixels that one workgroup of AMD's 8 × 8 passes covers.
    private const uint GroupSide = 8;

    // The longer side of the largest render size, at least, so that the luminance pyramid at half of it has the six
    // levels that AMD's passes write.
    private const uint SmallestLargestSide = 64;

    private static readonly Fsr3Clear[][] ClearLists = AllClearLists();

    // The word of FSR's constants that the exposure step writes.
    private static readonly uint DeltaPreExposureWord =
        (uint)Marshal.OffsetOf<Fsr3Constants>(nameof(Fsr3Constants.DeltaPreExposure)) / sizeof(uint);

    // The bindings of the exposure step: the exposure buffer and FSR's constants, both storage buffers.
    private static readonly Fsr3Binding[] ExposureBindings =
    [
        new(Binding: 0, Slot: Fsr3Slot.ExposureBuffer, Type: DescriptorType.StorageBuffer),
        new(Binding: 1, Slot: Fsr3Slot.Constants, Type: DescriptorType.StorageBuffer),
    ];

    // The bindings of each set layout: one per pass of AMD's, then the exposure step's and the clear step's.
    private static IEnumerable<Fsr3Binding[]> LayoutBindings =>
        Enum.GetValues<Fsr3Pass>().Select(Fsr3Bindings.Of).Append(ExposureBindings).Append(Fsr3Bindings.ClearStep);

    /// <summary>The passes of the upscale in the order of AMD's <c>fsr3upscalerDispatch</c>, after the generator.</summary>
    public static ReadOnlySpan<Fsr3Pass> UpscalePasses =>
        [
            Fsr3Pass.PrepareInputs,
            Fsr3Pass.LumaPyramid,
            Fsr3Pass.ShadingChangePyramid,
            Fsr3Pass.ShadingChange,
            Fsr3Pass.PrepareReactivity,
            Fsr3Pass.LumaInstability,
            Fsr3Pass.Accumulate,
        ];

    private readonly VulkanDevice device;
    private readonly DeviceAllocator allocator;
    private readonly Extent2D maxRenderSize;
    private readonly Extent2D outputSize;
    private readonly int framesInFlight;
    private readonly GpuImage[] images;
    private readonly ImageView[] spdLevelViews;
    private readonly GpuBuffer spdConstants;
    private readonly GpuBuffer reactiveConstants;
    private readonly GpuBuffer rcasConstants;
    private readonly Sampler linearClamp;
    private readonly DescriptorPool pool;
    private readonly FsrPipeline[] pipelines;
    private readonly FsrPipeline exposurePipeline;
    private readonly FsrPipeline clearPipeline;

    // The images that the pass being recorded already prepared, so that each gets one barrier.
    private readonly List<GpuImage> prepared = [];

    // The constants of the last recorded frame, which the next frame's derive from.
    private Fsr3Constants constants;

    // The frames recorded so far. AMD's frame index is this modulo 16, of which only the parity matters.
    private long executions;

    /// <param name="maxRenderSize">
    /// The largest render size: a longer side of at least 64 pixels, a shorter one of at least 2.
    /// </param>
    /// <param name="outputSize">The size of the upscaled image in pixels, which the output of every Record has.</param>
    /// <param name="framesInFlight">The frames that the GPU may still run when Record is called.</param>
    /// <exception cref="ArgumentOutOfRangeException">A size or the count of frames is out of range.</exception>
    /// <exception cref="NotSupportedException">One of AMD's passes binds more descriptors of a type than the device's
    /// compute stage sees.</exception>
    public Fsr3Upscaler(
        VulkanDevice device,
        DeviceAllocator allocator,
        ShaderLibrary shaders,
        Extent2D maxRenderSize,
        Extent2D outputSize,
        int framesInFlight
    )
    {
        CheckArguments(
            limits: device.Properties.Limits,
            maxRenderSize: maxRenderSize,
            outputSize: outputSize,
            framesInFlight: framesInFlight
        );
        this.device = device;
        this.allocator = allocator;
        this.maxRenderSize = maxRenderSize;
        this.outputSize = outputSize;
        this.framesInFlight = framesInFlight;
        constants = Fsr3Constants.Created(maxRenderSize, outputSize);

        images = [.. ImageDescriptions(maxRenderSize, outputSize).Select(CreateImage)];
        GpuImage spdMips = images[(int)Fsr3Image.SpdMips];
        spdLevelViews =
        [
            .. Enumerable
                .Range(0, (int)spdMips.MipLevels)
                .Select(level => spdMips.CreateLayerView(layer: 0, mipLevel: (uint)level)),
        ];

        (Constants, spdConstants, reactiveConstants) = CreateConstantBuffers();
        rcasConstants = CreateBuffer(
            size: 16,
            usage: BufferUsageFlags.UniformBufferBit,
            name: "FSR3UPSCALER RCAS constants"
        );
        linearClamp = CreateLinearClamp(device);
        pool = CreatePool(device, framesInFlight);
        pipelines =
        [
            .. Enum.GetValues<Fsr3Pass>()
                .Select(pass =>
                    CreatePipeline(
                        shaders: shaders,
                        shader: ShaderOf(pass),
                        bindings: Fsr3Bindings.Of(pass),
                        pushConstantBytes: 0
                    )
                ),
        ];
        // The exposure step's push constant is the word that it writes.
        exposurePipeline = CreatePipeline(
            shaders: shaders,
            shader: "fsr_exposure.comp",
            bindings: ExposureBindings,
            pushConstantBytes: sizeof(uint)
        );
        // The clear step's push constant is the count of the luminance pyramid's levels.
        clearPipeline = CreatePipeline(
            shaders: shaders,
            shader: ClearStepShader,
            bindings: Fsr3Bindings.ClearStep,
            pushConstantBytes: sizeof(uint)
        );
        WriteClearSets();
    }

    /// <summary>FSR's constant buffer, <c>cbFSR3Upscaler</c>, which Record writes. A test reads it back.</summary>
    public GpuBuffer Constants { get; }

    /// <summary>
    /// The transfer clears of a frame: AMD's clears of <c>fsr3upscalerDispatch</c> but those of
    /// <see cref="ZeroedEachFrame"/>, in AMD's order. On the first execution the accumulations and the lumas, and on a
    /// reset or the first execution the accumulation that the frame reads and the frame info. A frame is odd by the count
    /// of frames before it.
    /// </summary>
    public static IReadOnlyList<Fsr3Clear> Clears(bool firstExecution, bool reset, bool odd) =>
        ClearLists[(firstExecution ? 4 : 0) + (reset ? 2 : 0) + (odd ? 1 : 0)];

    /// <summary>The images that the clear step zeroes in every frame, which AMD clears with zeros in every frame.</summary>
    public static IReadOnlyList<Fsr3Image> ZeroedEachFrame { get; } =
    [Fsr3Image.ReconstructedPrevNearestDepth, Fsr3Image.SpdAtomicCounter, Fsr3Image.SpdMips];

    /// <summary>The embedded SPIR-V of the clear step, one compute dispatch that zeroes <see cref="ZeroedEachFrame"/>.</summary>
    internal const string ClearStepShader = "fsr_clear.comp";

    /// <summary>
    /// Declares the clear step's images as storage images whose contents it discards. The next flush of
    /// <paramref name="barriers"/> must come before <see cref="DispatchClearStep"/>.
    /// </summary>
    internal void DeclareClearStep(BarrierBatch barriers)
    {
        foreach (Fsr3Image image in ZeroedEachFrame)
            barriers.Image(Image(image), ImageUse.StorageCompute, discard: true);
    }

    /// <summary>
    /// Records the clear step: one dispatch that writes zeros into every texel of every level of
    /// <see cref="ZeroedEachFrame"/>, after the barriers of <see cref="DeclareClearStep"/>.
    /// </summary>
    internal void DispatchClearStep(CommandBuffer commandBuffer)
    {
        Vk vk = device.Vk;
        // The sets of the clear step hold FSR's own images only, which never change, so every frame binds the first.
        clearPipeline.Bind(vk, commandBuffer, set: 0);
        uint levels = Image(Fsr3Image.SpdMips).MipLevels;
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: clearPipeline.Layout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: sizeof(uint),
            pValues: &levels
        );
        // The reconstructed depth, at the largest render size, covers every level of the pyramid at half of it.
        (uint x, uint y) = Covering(Image(Fsr3Image.ReconstructedPrevNearestDepth).Extent);
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: x, groupCountY: y, groupCountZ: 1);
    }

    /// <summary>
    /// Records this frame's upscale: the pre-exposure ratio into the constants, the clear step, AMD's reactive mask
    /// generator, AMD's other clears and the seven passes. It writes the sets of the frame <c>framesInFlight</c> frames ago, which must have finished.
    /// Every touched image stays in its last use's state, which <paramref name="barriers"/> carries.
    /// </summary>
    /// <exception cref="ArgumentException">The render size is not within the largest render size, the output is not of
    /// the output size, or the reactive mask is not R8_UNORM.</exception>
    public void Record(CommandBuffer commandBuffer, BarrierBatch barriers, in Fsr3Inputs inputs)
    {
        CheckInputs(inputs);
        bool first = executions == 0;
        constants = Fsr3Constants.Next(
            last: constants,
            frame: inputs.Frame,
            resetAccumulation: inputs.Frame.Reset || first
        );
        var frame = new Recording(
            CommandBuffer: commandBuffer,
            Barriers: barriers,
            Inputs: inputs,
            Odd: executions % 2 == 1,
            Set: (int)(executions % framesInFlight)
        );

        if (first)
            ClearDefaults(frame);
        WriteConstants(frame, Fsr3SpdConstants.For(inputs.Frame.RenderSize));
        WriteSets(frame);
        // The clear step shares the exposure step's barrier and its Metal encoder under MoltenVK: neither touches an
        // image or a buffer of the other. AMD clears its images after the reactive mask generator, which does not touch
        // them either, so the earlier place gives the same images.
        DeclareClearStep(frame.Barriers);
        RecordExposureStep(frame);
        DispatchClearStep(frame.CommandBuffer);
        RecordPass(frame, Fsr3Pass.GenerateReactive);
        IReadOnlyList<Fsr3Clear> clears = Clears(firstExecution: first, reset: inputs.Frame.Reset, odd: frame.Odd);
        for (int index = 0; index < clears.Count; index++)
            Clear(frame, clears[index]);
        foreach (Fsr3Pass pass in UpscalePasses)
            RecordPass(frame, pass);
        if (inputs.Frame.Sharpness > 0)
            RecordPass(frame, Fsr3Pass.Rcas);
        executions++;
    }

    public void Dispose()
    {
        exposurePipeline.Dispose(device);
        clearPipeline.Dispose(device);
        foreach (FsrPipeline pipeline in pipelines)
            pipeline.Dispose(device);
        device.Vk.DestroyDescriptorPool(device.Device, pool, null);
        device.Vk.DestroySampler(device.Device, linearClamp, null);
        reactiveConstants.Dispose();
        rcasConstants.Dispose();
        spdConstants.Dispose();
        Constants.Dispose();
        foreach (ImageView view in spdLevelViews)
            device.Vk.DestroyImageView(device.Device, view, null);
        foreach (GpuImage image in images)
            image.Dispose();
    }

    /// <summary>
    /// AMD's count of jitter phases for a render width and an output width: 8 times the square of the upscale ratio,
    /// rounded down, such as 18 from 1280 to 1920.
    /// </summary>
    public static int JitterPhaseCount(uint renderWidth, uint outputWidth) =>
        Math.Max(1, (int)(8f * MathF.Pow((float)outputWidth / renderWidth, 2f)));

    /// <summary>Pads only FSR's internal allocation to fit its six-level pyramid, without changing the active size.</summary>
    internal static Extent2D AllocationSize(Extent2D renderSize) =>
        Math.Max(renderSize.Width, renderSize.Height) >= SmallestLargestSide
            ? renderSize
            : new(Math.Max(renderSize.Width, SmallestLargestSide), renderSize.Height);

    /// <summary>
    /// AMD's jitter of draw <paramref name="index"/> in a sequence of <paramref name="phaseCount"/> phases, in render
    /// pixels from -0.5 to 0.5: the Halton sequence of bases 2 and 3 from its second point. AMD's sample draws with the
    /// projection moved by this offset and passes it negated to the upscale, as <see cref="Fsr3Frame.Jitter"/>.
    /// </summary>
    public static Vector2 JitterOffset(int index, int phaseCount) =>
        new(Halton(index % phaseCount + 1, 2) - 0.5f, Halton(index % phaseCount + 1, 3) - 0.5f);

    /// <summary>The workgroups that AMD's host dispatches for <paramref name="pass"/> with <paramref name="constants"/>.</summary>
    public static (uint X, uint Y) Groups(Fsr3Pass pass, in Fsr3Constants constants)
    {
        var render = new Extent2D((uint)constants.RenderSizeX, (uint)constants.RenderSizeY);
        return pass switch
        {
            Fsr3Pass.GenerateReactive => Covering(render),
            Fsr3Pass.PrepareInputs => Covering(render),
            Fsr3Pass.LumaPyramid => Fsr3SpdConstants.Groups(render),
            Fsr3Pass.ShadingChangePyramid => Fsr3SpdConstants.Groups(render),
            // AMD halves the render size in floats and rounds down.
            Fsr3Pass.ShadingChange => Covering(
                new((uint)(int)(constants.RenderSizeX * 0.5f), (uint)(int)(constants.RenderSizeY * 0.5f))
            ),
            Fsr3Pass.PrepareReactivity => Covering(render),
            Fsr3Pass.LumaInstability => Covering(render),
            Fsr3Pass.Accumulate => Covering(new((uint)constants.UpscaleSizeX, (uint)constants.UpscaleSizeY)),
            Fsr3Pass.Rcas => (((uint)constants.UpscaleSizeX + 15) / 16, ((uint)constants.UpscaleSizeY + 15) / 16),
        };
    }

    /// <summary>
    /// FSR's own images for a largest render size and an output size, in the order of <see cref="Fsr3Image"/>, under AMD's
    /// names, formats and sizes. It leaves out AMD's Lanczos table, which the reference Lanczos of the portable build does
    /// not bind.
    /// </summary>
    public static IReadOnlyList<Fsr3ImageDescription> ImageDescriptions(Extent2D maxRenderSize, Extent2D outputSize) =>
        [.. Enum.GetValues<Fsr3Image>().Select(image => Describe(image, maxRenderSize, outputSize))];

    private static Fsr3ImageDescription Describe(Fsr3Image image, Extent2D maxRenderSize, Extent2D outputSize)
    {
        var half = new Extent2D(maxRenderSize.Width / 2, maxRenderSize.Height / 2);
        var one = new Extent2D(1, 1);
        string name = $"FSR3UPSCALER_{image}";
        return image switch
        {
            Fsr3Image.Accumulation1 or Fsr3Image.Accumulation2 => new(name, Format.R8Unorm, maxRenderSize),
            Fsr3Image.Luma1 or Fsr3Image.Luma2 or Fsr3Image.IntermediateFp16x1 => new(
                name,
                Format.R16Sfloat,
                maxRenderSize
            ),
            Fsr3Image.ShadingChange => new(name, Format.R8Unorm, half),
            Fsr3Image.NewLocks => new(name, Format.R8Unorm, outputSize),
            Fsr3Image.InternalUpscaled1 or Fsr3Image.InternalUpscaled2 => new(
                name,
                Format.R16G16B16A16Sfloat,
                outputSize
            ),
            // The full chain of mips.
            Fsr3Image.SpdMips => new(name, Format.R16G16Sfloat, half)
            {
                MipLevels = 1 + (uint)Math.Floor(Math.Log2(Math.Max(half.Width, half.Height))),
            },
            Fsr3Image.FarthestDepthMip1 => new(name, Format.R16Sfloat, half),
            Fsr3Image.LumaHistory1 or Fsr3Image.LumaHistory2 => new(name, Format.R16G16B16A16Sfloat, maxRenderSize),
            Fsr3Image.SpdAtomicCounter => new(name, Format.R32Uint, one),
            Fsr3Image.DilatedReactiveMasks => new(name, Format.R8G8B8A8Unorm, maxRenderSize),
            Fsr3Image.DefaultReactivityMask => new(name, Format.R8Unorm, one),
            Fsr3Image.DefaultExposure => new(name, Format.R32G32Sfloat, one),
            Fsr3Image.FrameInfo => new(name, Format.R32G32B32A32Sfloat, one),
            Fsr3Image.DilatedDepth => new(name, Format.R32Sfloat, maxRenderSize),
            Fsr3Image.DilatedVelocity => new(name, Format.R16G16Sfloat, maxRenderSize),
            Fsr3Image.ReconstructedPrevNearestDepth => new(name, Format.R32Uint, maxRenderSize),
        };
    }

    /// <summary>The workgroups of 8 × 8 invocations that cover <paramref name="size"/>.</summary>
    private static (uint X, uint Y) Covering(Extent2D size) =>
        ((size.Width + GroupSide - 1) / GroupSide, (size.Height + GroupSide - 1) / GroupSide);

    /// <summary>AMD's Halton number of <paramref name="index"/> in <paramref name="radix"/>, in single floats as AMD's is.</summary>
    private static float Halton(int index, int radix)
    {
        float f = 1f;
        float result = 0f;
        for (int current = index; current > 0; )
        {
            f /= radix;
            result += f * (current % radix);
            current = (int)MathF.Floor((float)current / radix);
        }

        return result;
    }

    /// <summary>The embedded SPIR-V of AMD's pass.</summary>
    internal static string ShaderOf(Fsr3Pass pass) =>
        pass switch
        {
            Fsr3Pass.GenerateReactive => "ffx_fsr3upscaler_autogen_reactive_pass.glsl",
            Fsr3Pass.PrepareInputs => "ffx_fsr3upscaler_prepare_inputs_pass.glsl",
            Fsr3Pass.LumaPyramid => "ffx_fsr3upscaler_luma_pyramid_pass.glsl",
            Fsr3Pass.ShadingChangePyramid => "ffx_fsr3upscaler_shading_change_pyramid_pass.glsl",
            Fsr3Pass.ShadingChange => "ffx_fsr3upscaler_shading_change_pass.glsl",
            Fsr3Pass.PrepareReactivity => "ffx_fsr3upscaler_prepare_reactivity_pass.glsl",
            Fsr3Pass.LumaInstability => "ffx_fsr3upscaler_luma_instability_pass.glsl",
            Fsr3Pass.Accumulate => "ffx_fsr3upscaler_accumulate_pass.glsl",
            Fsr3Pass.Rcas => "fsr_rcas.glsl",
        };

    // The lists of Clears, at the index that it computes from its three flags.
    private static Fsr3Clear[][] AllClearLists() =>
        [
            .. Enumerable
                .Range(0, 8)
                .Select(index =>
                    BuildClears(firstExecution: (index & 4) != 0, reset: (index & 2) != 0, odd: (index & 1) != 0)
                ),
        ];

    private static Fsr3Clear[] BuildClears(bool firstExecution, bool reset, bool odd)
    {
        var clears = new List<Fsr3Clear>();
        if (firstExecution)
        {
            clears.Add(new(Fsr3Image.Accumulation1, Vector4.Zero));
            clears.Add(new(Fsr3Image.Accumulation2, Vector4.Zero));
            clears.Add(new(Fsr3Image.Luma1, Vector4.Zero));
            clears.Add(new(Fsr3Image.Luma2, Vector4.Zero));
        }

        if (reset || firstExecution)
        {
            clears.Add(new(Fsr3Bindings.Read(Fsr3Image.Accumulation1, Fsr3Image.Accumulation2, odd), Vector4.Zero));
            // AMD's frame info after a reset: no exposure yet, and a previous luminance of 1.
            clears.Add(new(Fsr3Image.FrameInfo, new Vector4(x: -1f, y: 1f, z: 0f, w: 0f)));
        }

        return [.. clears];
    }

    /// <summary>
    /// One binding of a pass's set layout: one descriptor that the compute stage sees, with AMD's immutable sampler.
    /// </summary>
    private static DescriptorSetLayoutBinding LayoutBinding(Fsr3Binding binding, Sampler* sampler) =>
        new()
        {
            Binding = binding.Binding,
            DescriptorType = binding.Type,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.ComputeBit,
            PImmutableSamplers = binding.Type == DescriptorType.Sampler ? sampler : null,
        };

    private static Sampler CreateLinearClamp(VulkanDevice device)
    {
        // AMD's FFX_FILTER_TYPE_MINMAGMIP_LINEAR with clamped addresses, as AMD's Vulkan backend makes it (ffx_vk.cpp).
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = Vk.LodClampNone,
            BorderColor = BorderColor.IntTransparentBlack,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "FSR3UPSCALER linear clamp");
        return sampler;
    }

    /// <summary>A pool of one set per pass and per frame in flight, with the descriptors that their bindings hold.</summary>
    private static DescriptorPool CreatePool(VulkanDevice device, int framesInFlight)
    {
        Fsr3Binding[] all = [.. LayoutBindings.SelectMany(bindings => bindings)];
        DescriptorPoolSize[] sizes =
        [
            .. all.GroupBy(binding => binding.Type)
                .Select(group => new DescriptorPoolSize(group.Key, (uint)(group.Count() * framesInFlight))),
        ];
        fixed (DescriptorPoolSize* pSizes = sizes)
        {
            var info = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = (uint)(LayoutBindings.Count() * framesInFlight),
                PoolSizeCount = (uint)sizes.Length,
                PPoolSizes = pSizes,
            };
            device
                .Vk.CreateDescriptorPool(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pDescriptorPool: out DescriptorPool pool
                )
                .Check("vkCreateDescriptorPool");
            return pool;
        }
    }

    /// <summary>Refuses arguments of the constructor and a device whose limits one of AMD's passes exceeds.</summary>
    internal static void CheckArguments(
        in PhysicalDeviceLimits limits,
        Extent2D maxRenderSize,
        Extent2D outputSize,
        int framesInFlight
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            Math.Max(maxRenderSize.Width, maxRenderSize.Height),
            SmallestLargestSide
        );
        // AMD's images at half the largest render size need a pixel on each side.
        ArgumentOutOfRangeException.ThrowIfLessThan(Math.Min(maxRenderSize.Width, maxRenderSize.Height), 2u);
        ArgumentOutOfRangeException.ThrowIfZero(Math.Min(outputSize.Width, outputSize.Height));
        uint spdLevels = Describe(Fsr3Image.SpdMips, maxRenderSize, outputSize).MipLevels;
        if (spdLevels > Fsr3Bindings.ClearedSpdLevels)
            throw new ArgumentOutOfRangeException(
                nameof(maxRenderSize),
                $"The largest render size gives FSR's luminance pyramid {spdLevels} levels, and the clear step binds {Fsr3Bindings.ClearedSpdLevels}."
            );
        ArgumentOutOfRangeException.ThrowIfLessThan(framesInFlight, 1);
        // Before any layout exists: a pipeline layout above a per-stage limit breaks the valid usage of
        // vkCreatePipelineLayout.
        foreach (Fsr3Binding[] bindings in LayoutBindings)
        {
            var counts = default(LimitedDescriptors);
            foreach (Fsr3Binding binding in bindings)
                counts = counts.With(binding: LayoutBinding(binding, sampler: null), updatedAfterBind: false);
            if (counts.Problem(limits) is { } problem)
                throw new NotSupportedException($"A pass of FSR 3.1: {problem}");
        }
    }

    private void CheckInputs(in Fsr3Inputs inputs)
    {
        Extent2D render = inputs.Frame.RenderSize;
        bool renderFits = render.Width <= maxRenderSize.Width && render.Height <= maxRenderSize.Height;
        if (render.Width == 0 || render.Height == 0 || !renderFits)
            throw new ArgumentException(
                $"The render size {render.Width} × {render.Height} is not within the largest render size.",
                nameof(inputs)
            );
        if (inputs.Output.Extent.Width != outputSize.Width || inputs.Output.Extent.Height != outputSize.Height)
            throw new ArgumentException("The output is not of the output size.", nameof(inputs));
        // AMD's generator stores the mask through an r8 declaration, and a store through another format is undefined
        // (ThirdParty/Fsr3/README.md, "The changed declarations").
        if (inputs.Reactive.Format != Format.R8Unorm)
            throw new ArgumentException(
                $"The reactive mask is {inputs.Reactive.Format}, not R8_UNORM.",
                nameof(inputs)
            );
    }

    // Every image of FSR is a storage image, a sampled image, a clear's destination and a copy's source, which a test
    // reads back.
    private GpuImage CreateImage(Fsr3ImageDescription description) =>
        GpuImage.Create(
            device: device,
            allocator: allocator,
            extent: description.Extent,
            format: description.Format,
            usage: ImageUsageFlags.StorageBit
                | ImageUsageFlags.SampledBit
                | ImageUsageFlags.TransferDstBit
                | ImageUsageFlags.TransferSrcBit,
            mipLevels: description.MipLevels,
            name: description.Name
        );

    /// <summary>FSR's three constant buffers: <c>cbFSR3Upscaler</c>, <c>cbSPD</c> and <c>cbGenerateReactive</c>.</summary>
    private (GpuBuffer Main, GpuBuffer Spd, GpuBuffer Reactive) CreateConstantBuffers()
    {
        // The main constants are a storage buffer too, for the exposure step, and a transfer source, for a test to read.
        GpuBuffer main = CreateBuffer(
            size: (ulong)sizeof(Fsr3Constants),
            usage: BufferUsageFlags.UniformBufferBit
                | BufferUsageFlags.StorageBufferBit
                | BufferUsageFlags.TransferSrcBit,
            name: "FSR3UPSCALER constants"
        );
        GpuBuffer spd = CreateBuffer(
            size: (ulong)sizeof(Fsr3SpdConstants),
            usage: BufferUsageFlags.UniformBufferBit,
            name: "FSR3UPSCALER SPD constants"
        );
        GpuBuffer reactive = CreateBuffer(
            size: (ulong)sizeof(Fsr3ReactiveConstants),
            usage: BufferUsageFlags.UniformBufferBit,
            name: "FSR3UPSCALER reactive constants"
        );
        return (Main: main, Spd: spd, Reactive: reactive);
    }

    private FsrPipeline CreatePipeline(
        ShaderLibrary shaders,
        string shader,
        Fsr3Binding[] bindings,
        uint pushConstantBytes
    ) =>
        FsrPipeline.Create(
            device: device,
            shaders: shaders,
            pool: pool,
            shader: shader,
            bindings: bindings,
            sampler: linearClamp,
            sets: framesInFlight,
            pushConstantBytes: pushConstantBytes
        );

    private GpuBuffer CreateBuffer(ulong size, BufferUsageFlags usage, string name) =>
        GpuBuffer.Create(
            device: device,
            allocator: allocator,
            size: size,
            usage: usage | BufferUsageFlags.TransferDstBit,
            memoryUsage: MemoryUsage.GpuOnly,
            name: name
        );

    /// <summary>FSR's own <paramref name="image"/>. A test fills and reads it.</summary>
    internal GpuImage Image(Fsr3Image image) => images[(int)image];

    /// <summary>The image that <paramref name="slot"/> holds in this frame.</summary>
    private GpuImage ImageOf(Fsr3Slot slot, in Fsr3Inputs inputs, bool odd) =>
        slot switch
        {
            Fsr3Slot.InputColor => inputs.Color,
            Fsr3Slot.InputOpaqueOnly => inputs.OpaqueOnly,
            Fsr3Slot.InputMotionVectors => inputs.Motion,
            Fsr3Slot.InputDepth => inputs.Depth,
            // AMD's default exposure, 0, which AMD's shaders read as 1: the color is already pre-exposed.
            Fsr3Slot.InputExposure => Image(Fsr3Image.DefaultExposure),
            Fsr3Slot.ReactiveMask => inputs.Reactive,
            Fsr3Slot.TransparencyAndCompositionMask => inputs.TransparencyAndComposition
                ?? Image(Fsr3Image.DefaultReactivityMask),
            Fsr3Slot.UpscaledOutput => inputs.Output,
            Fsr3Slot.RcasInput => Image(
                Fsr3Bindings.Written(Fsr3Image.InternalUpscaled1, Fsr3Image.InternalUpscaled2, odd)
            ),
            Fsr3Slot.ReconstructedPreviousNearestDepth => Image(Fsr3Image.ReconstructedPrevNearestDepth),
            Fsr3Slot.DilatedMotionVectors => Image(Fsr3Image.DilatedVelocity),
            Fsr3Slot.DilatedDepth => Image(Fsr3Image.DilatedDepth),
            Fsr3Slot.InternalUpscaledColorRead => Image(
                Fsr3Bindings.Read(Fsr3Image.InternalUpscaled1, Fsr3Image.InternalUpscaled2, odd)
            ),
            Fsr3Slot.InternalUpscaledColorWrite => Image(
                Fsr3Bindings.Written(Fsr3Image.InternalUpscaled1, Fsr3Image.InternalUpscaled2, odd)
            ),
            Fsr3Slot.AccumulationRead => Image(
                Fsr3Bindings.Read(Fsr3Image.Accumulation1, Fsr3Image.Accumulation2, odd)
            ),
            Fsr3Slot.AccumulationWrite => Image(
                Fsr3Bindings.Written(Fsr3Image.Accumulation1, Fsr3Image.Accumulation2, odd)
            ),
            Fsr3Slot.LumaHistoryRead => Image(Fsr3Bindings.Read(Fsr3Image.LumaHistory1, Fsr3Image.LumaHistory2, odd)),
            Fsr3Slot.LumaHistoryWrite => Image(
                Fsr3Bindings.Written(Fsr3Image.LumaHistory1, Fsr3Image.LumaHistory2, odd)
            ),
            // This frame's luma goes where the pair's read image is, and the last frame's is the other.
            Fsr3Slot.CurrentLuma => Image(Fsr3Bindings.Read(Fsr3Image.Luma1, Fsr3Image.Luma2, odd)),
            Fsr3Slot.PreviousLuma => Image(Fsr3Bindings.Written(Fsr3Image.Luma1, Fsr3Image.Luma2, odd)),
            // AMD aliases the farthest depth and the luma instability onto one image, which they use in turn.
            Fsr3Slot.FarthestDepth => Image(Fsr3Image.IntermediateFp16x1),
            Fsr3Slot.LumaInstability => Image(Fsr3Image.IntermediateFp16x1),
            Fsr3Slot.FarthestDepthMip1 => Image(Fsr3Image.FarthestDepthMip1),
            Fsr3Slot.ShadingChange => Image(Fsr3Image.ShadingChange),
            Fsr3Slot.NewLocks => Image(Fsr3Image.NewLocks),
            Fsr3Slot.DilatedReactiveMasks => Image(Fsr3Image.DilatedReactiveMasks),
            // The whole luminance pyramid, then each of its levels, which follow it in Fsr3Slot.
            >= Fsr3Slot.SpdMips and <= Fsr3Slot.SpdMip12 => Image(Fsr3Image.SpdMips),
            Fsr3Slot.SpdAtomic => Image(Fsr3Image.SpdAtomicCounter),
            Fsr3Slot.FrameInfo => Image(Fsr3Image.FrameInfo),
            Fsr3Slot.ExposureBuffer
            or Fsr3Slot.Constants
            or Fsr3Slot.SpdConstants
            or Fsr3Slot.ReactiveConstants
            or Fsr3Slot.RcasConstants
            or Fsr3Slot.LinearClamp => throw new UnreachableException($"{slot} holds no image."),
        };

    /// <summary>The buffer that <paramref name="slot"/> holds.</summary>
    private GpuBuffer BufferOf(Fsr3Slot slot, in Fsr3Inputs inputs) =>
        slot switch
        {
            Fsr3Slot.ExposureBuffer => inputs.Exposure,
            Fsr3Slot.Constants => Constants,
            Fsr3Slot.SpdConstants => spdConstants,
            Fsr3Slot.ReactiveConstants => reactiveConstants,
            Fsr3Slot.RcasConstants => rcasConstants,
            // The image slots come first in Fsr3Slot.
            <= Fsr3Slot.FrameInfo or Fsr3Slot.LinearClamp => throw new UnreachableException($"{slot} holds no buffer."),
        };

    /// <summary>Clears AMD's default exposure and default reactivity, which AMD creates with zeros.</summary>
    private void ClearDefaults(in Recording frame)
    {
        Clear(frame, new(Fsr3Image.DefaultExposure, Vector4.Zero));
        Clear(frame, new(Fsr3Image.DefaultReactivityMask, Vector4.Zero));
    }

    private void Clear(in Recording frame, Fsr3Clear clear)
    {
        GpuImage image = Image(clear.Target);
        frame.Barriers.Image(image, ImageUse.TransferDestination, discard: true);
        frame.Barriers.Flush(device.Vk, frame.CommandBuffer);
        // AMD's Vulkan backend clears every level with the float values, whose bits are those of 0 for an integer image.
        var color = new ClearColorValue
        {
            Float32_0 = clear.Color.X,
            Float32_1 = clear.Color.Y,
            Float32_2 = clear.Color.Z,
            Float32_3 = clear.Color.W,
        };
        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            LevelCount = image.MipLevels,
            LayerCount = 1,
        };
        device.Vk.CmdClearColorImage(
            commandBuffer: frame.CommandBuffer,
            image: image.Handle,
            imageLayout: ImageLayout.TransferDstOptimal,
            pColor: &color,
            rangeCount: 1,
            pRanges: &range
        );
    }

    /// <summary>
    /// Writes this frame's three constant buffers as AMD's host stages them, all but the word of the pre-exposure ratio,
    /// which the exposure step owns.
    /// </summary>
    private void WriteConstants(in Recording frame, Fsr3SpdConstants spd)
    {
        frame.Barriers.Buffer(Constants, BufferUse.TransferDestination);
        frame.Barriers.Buffer(spdConstants, BufferUse.TransferDestination);
        frame.Barriers.Buffer(reactiveConstants, BufferUse.TransferDestination);
        frame.Barriers.Buffer(rcasConstants, BufferUse.TransferDestination);
        frame.Barriers.Flush(device.Vk, frame.CommandBuffer);

        WriteAllButTheRatio(frame);
        Update(frame: frame, buffer: spdConstants, data: new Span<byte>(&spd, sizeof(Fsr3SpdConstants)), offset: 0);
        Fsr3ReactiveConstants reactive = Fsr3ReactiveConstants.Sample;
        float sharpening = MathF.Pow(2, -(2 - 2 * frame.Inputs.Frame.Sharpness));
        uint half = BitConverter.HalfToUInt16Bits((Half)sharpening);
        uint* rcas = stackalloc uint[] { BitConverter.SingleToUInt32Bits(sharpening), half | (half << 16), 0, 0 };
        Update(frame: frame, buffer: rcasConstants, data: new Span<byte>(rcas, 16), offset: 0);
        Update(
            frame: frame,
            buffer: reactiveConstants,
            data: new Span<byte>(&reactive, sizeof(Fsr3ReactiveConstants)),
            offset: 0
        );
    }

    /// <summary>Uploads FSR's constants in two ranges, around the word of the pre-exposure ratio.</summary>
    private void WriteAllButTheRatio(in Recording frame)
    {
        Fsr3Constants main = constants;
        var bytes = new Span<byte>(&main, sizeof(Fsr3Constants));
        int ratio = (int)DeltaPreExposureWord * sizeof(uint);
        int afterRatio = ratio + sizeof(uint);
        Update(frame: frame, buffer: Constants, data: bytes[..ratio], offset: 0);
        Update(frame: frame, buffer: Constants, data: bytes[afterRatio..], offset: afterRatio);
    }

    private void Update(in Recording frame, GpuBuffer buffer, Span<byte> data, int offset)
    {
        fixed (byte* bytes = data)
            device.Vk.CmdUpdateBuffer(
                commandBuffer: frame.CommandBuffer,
                dstBuffer: buffer.Handle,
                dstOffset: (ulong)offset,
                dataSize: (ulong)data.Length,
                pData: bytes
            );
    }

    /// <summary>Writes this frame's sets of every pipeline with this frame's images and buffers.</summary>
    private void WriteSets(in Recording frame)
    {
        foreach (FsrPipeline pipeline in pipelines)
            WriteSet(frame, pipeline);
        WriteSet(frame, exposurePipeline);
    }

    private void WriteSet(in Recording frame, FsrPipeline pipeline)
    {
        Fsr3Binding[] bindings = pipeline.Bindings;
        DescriptorImageInfo* imageInfos = stackalloc DescriptorImageInfo[bindings.Length];
        DescriptorBufferInfo* bufferInfos = stackalloc DescriptorBufferInfo[bindings.Length];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[bindings.Length];
        int count = 0;
        foreach (Fsr3Binding binding in bindings)
        {
            if (binding.Type == DescriptorType.Sampler)
                continue;
            writes[count] = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = pipeline.Sets[frame.Set],
                DstBinding = binding.Binding,
                DescriptorCount = 1,
                DescriptorType = binding.Type,
            };
            if (binding.Type is DescriptorType.UniformBuffer or DescriptorType.StorageBuffer)
            {
                GpuBuffer buffer = BufferOf(binding.Slot, frame.Inputs);
                bufferInfos[count] = new DescriptorBufferInfo(buffer.Handle, 0, buffer.Size);
                writes[count].PBufferInfo = &bufferInfos[count];
            }
            else
            {
                bool storage = binding.Type == DescriptorType.StorageImage;
                imageInfos[count] = new DescriptorImageInfo
                {
                    ImageView = Fsr3Bindings.SpdLevel(binding.Slot) is { } level
                        ? spdLevelViews[level]
                        : ImageOf(binding.Slot, frame.Inputs, frame.Odd).View,
                    ImageLayout = storage ? ImageLayout.General : ImageLayout.ShaderReadOnlyOptimal,
                };
                writes[count].PImageInfo = &imageInfos[count];
            }

            count++;
        }

        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: (uint)count,
            pDescriptorWrites: writes,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    /// <summary>
    /// Writes every set of the clear step: the reconstructed depth, the atomic counter and each level of the luminance
    /// pyramid. A binding past the pyramid's last level holds that level again, a valid view that the step never writes.
    /// </summary>
    private void WriteClearSets()
    {
        Fsr3Binding[] bindings = Fsr3Bindings.ClearStep;
        DescriptorImageInfo* infos = stackalloc DescriptorImageInfo[bindings.Length];
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[bindings.Length];
        foreach (DescriptorSet set in clearPipeline.Sets)
        {
            for (int index = 0; index < bindings.Length; index++)
            {
                infos[index] = new DescriptorImageInfo
                {
                    ImageView = Fsr3Bindings.SpdLevel(bindings[index].Slot) is { } level
                        ? spdLevelViews[Math.Min(level, spdLevelViews.Length - 1)]
                        : Image(ZeroedImageOf(bindings[index].Slot)).View,
                    ImageLayout = ImageLayout.General,
                };
                writes[index] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = set,
                    DstBinding = bindings[index].Binding,
                    DescriptorCount = 1,
                    DescriptorType = DescriptorType.StorageImage,
                    PImageInfo = &infos[index],
                };
            }

            device.Vk.UpdateDescriptorSets(
                device: device.Device,
                descriptorWriteCount: (uint)bindings.Length,
                pDescriptorWrites: writes,
                descriptorCopyCount: 0,
                pDescriptorCopies: null
            );
        }
    }

    /// <summary>The image of a slot of the clear step other than a level of the luminance pyramid.</summary>
    private static Fsr3Image ZeroedImageOf(Fsr3Slot slot)
    {
        if (slot == Fsr3Slot.ReconstructedPreviousNearestDepth)
            return Fsr3Image.ReconstructedPrevNearestDepth;
        if (slot == Fsr3Slot.SpdAtomic)
            return Fsr3Image.SpdAtomicCounter;
        throw new UnreachableException($"The clear step binds no {slot}.");
    }

    /// <summary>The exposure step: one invocation that writes the pre-exposure ratio into FSR's constants.</summary>
    private void RecordExposureStep(in Recording frame)
    {
        Vk vk = device.Vk;
        CommandBuffer commandBuffer = frame.CommandBuffer;
        frame.Barriers.Buffer(frame.Inputs.Exposure, BufferUse.ComputeRead);
        frame.Barriers.Buffer(Constants, BufferUse.ComputeWrite);
        frame.Barriers.Flush(vk, commandBuffer);
        exposurePipeline.Bind(vk, commandBuffer, frame.Set);
        uint word = DeltaPreExposureWord;
        vk.CmdPushConstants(
            commandBuffer: commandBuffer,
            layout: exposurePipeline.Layout,
            stageFlags: ShaderStageFlags.ComputeBit,
            offset: 0,
            size: sizeof(uint),
            pValues: &word
        );
        vk.CmdDispatch(commandBuffer: commandBuffer, groupCountX: 1, groupCountY: 1, groupCountZ: 1);
    }

    /// <summary>Prepares every image and buffer that <paramref name="pass"/> binds, then dispatches AMD's groups.</summary>
    private void RecordPass(in Recording frame, Fsr3Pass pass)
    {
        Vk vk = device.Vk;
        FsrPipeline pipeline = pipelines[(int)pass];
        prepared.Clear();
        foreach (Fsr3Binding binding in pipeline.Bindings)
        {
            // The sampler is immutable in the set layout.
            if (binding.Type == DescriptorType.UniformBuffer)
                frame.Barriers.Buffer(BufferOf(binding.Slot, frame.Inputs), BufferUse.ComputeUniformRead);
            else if (binding.Type == DescriptorType.SampledImage)
                Prepare(frame.Barriers, ImageOf(binding.Slot, frame.Inputs, frame.Odd), ImageUse.SampledCompute);
            else if (binding.Type == DescriptorType.StorageImage)
                Prepare(frame.Barriers, ImageOf(binding.Slot, frame.Inputs, frame.Odd), ImageUse.StorageCompute);
        }

        frame.Barriers.Flush(vk, frame.CommandBuffer);
        pipeline.Bind(vk, frame.CommandBuffer, frame.Set);
        (uint x, uint y) = Groups(pass, constants);
        vk.CmdDispatch(commandBuffer: frame.CommandBuffer, groupCountX: x, groupCountY: y, groupCountZ: 1);
    }

    /// <summary>Prepares <paramref name="image"/> for <paramref name="use"/>, once in a pass: the luminance pyramid's levels share one image.</summary>
    private void Prepare(BarrierBatch barriers, GpuImage image, ImageUse use)
    {
        if (prepared.Contains(image))
            return;
        prepared.Add(image);
        barriers.Image(image, use);
    }

    /// <summary>
    /// What one Record hands its steps: the command buffer, the barriers, this frame's inputs, whether the frame is odd by
    /// the count of frames before it, and the index of the sets that it writes.
    /// </summary>
    private readonly record struct Recording(
        CommandBuffer CommandBuffer,
        BarrierBatch Barriers,
        Fsr3Inputs Inputs,
        bool Odd,
        int Set
    );

    /// <summary>
    /// The compute pipeline of one pass: its set layout, with AMD's sampler immutable, its pipeline layout, and one set per
    /// frame in flight.
    /// </summary>
    private sealed class FsrPipeline(
        Fsr3Binding[] bindings,
        DescriptorSetLayout setLayout,
        PipelineLayout layout,
        Pipeline pipeline,
        DescriptorSet[] sets
    )
    {
        public Fsr3Binding[] Bindings { get; } = bindings;
        public PipelineLayout Layout { get; } = layout;
        public DescriptorSet[] Sets { get; } = sets;

        public static FsrPipeline Create(
            VulkanDevice device,
            ShaderLibrary shaders,
            DescriptorPool pool,
            string shader,
            Fsr3Binding[] bindings,
            Sampler sampler,
            int sets,
            uint pushConstantBytes
        )
        {
            DescriptorSetLayout setLayout = CreateSetLayout(device, bindings, sampler);
            var range = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, pushConstantBytes);
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = pushConstantBytes == 0 ? 0u : 1u,
                PPushConstantRanges = &range,
            };
            device
                .Vk.CreatePipelineLayout(
                    device: device.Device,
                    pCreateInfo: &layoutInfo,
                    pAllocator: null,
                    pPipelineLayout: out PipelineLayout layout
                )
                .Check("vkCreatePipelineLayout");
            Pipeline pipeline = new ComputePipelineDesc
            {
                Name = shader,
                Shader = shader,
                Layout = layout,
            }.Create(device, shaders);
            var allocated = new DescriptorSet[sets];
            for (int index = 0; index < sets; index++)
            {
                var allocate = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = pool,
                    DescriptorSetCount = 1,
                    PSetLayouts = &setLayout,
                };
                device
                    .Vk.AllocateDescriptorSets(device.Device, &allocate, out allocated[index])
                    .Check("vkAllocateDescriptorSets");
            }

            return new(bindings: bindings, setLayout: setLayout, layout: layout, pipeline: pipeline, sets: allocated);
        }

        public void Bind(Vk vk, CommandBuffer commandBuffer, int set)
        {
            vk.CmdBindPipeline(commandBuffer, PipelineBindPoint.Compute, pipeline);
            DescriptorSet bound = Sets[set];
            vk.CmdBindDescriptorSets(
                commandBuffer: commandBuffer,
                pipelineBindPoint: PipelineBindPoint.Compute,
                layout: Layout,
                firstSet: 0,
                descriptorSetCount: 1,
                pDescriptorSets: &bound,
                dynamicOffsetCount: 0,
                pDynamicOffsets: null
            );
        }

        public void Dispose(VulkanDevice device)
        {
            device.Vk.DestroyPipeline(device.Device, pipeline, null);
            device.Vk.DestroyPipelineLayout(device.Device, Layout, null);
            device.Vk.DestroyDescriptorSetLayout(device.Device, setLayout, null);
        }

        private static DescriptorSetLayout CreateSetLayout(VulkanDevice device, Fsr3Binding[] bindings, Sampler sampler)
        {
            DescriptorSetLayoutBinding* layoutBindings = stackalloc DescriptorSetLayoutBinding[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
                layoutBindings[index] = LayoutBinding(bindings[index], &sampler);
            var info = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = layoutBindings,
            };
            device
                .Vk.CreateDescriptorSetLayout(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pSetLayout: out DescriptorSetLayout setLayout
                )
                .Check("vkCreateDescriptorSetLayout");
            return setLayout;
        }
    }
}

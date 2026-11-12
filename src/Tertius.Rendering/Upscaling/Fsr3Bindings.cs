using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// FSR's own images, under AMD's names without the prefix <c>FSR3UPSCALER_</c>: the internal images of AMD's
/// <c>fsr3upscalerCreate</c>, without the Lanczos table, then the three that AMD's FSR 3 shares with frame generation.
/// </summary>
internal enum Fsr3Image
{
    Accumulation1,
    Accumulation2,
    Luma1,
    Luma2,
    IntermediateFp16x1,
    ShadingChange,
    NewLocks,
    InternalUpscaled1,
    InternalUpscaled2,
    SpdMips,
    FarthestDepthMip1,
    LumaHistory1,
    LumaHistory2,
    SpdAtomicCounter,
    DilatedReactiveMasks,
    DefaultReactivityMask,
    DefaultExposure,
    FrameInfo,
    DilatedDepth,
    DilatedVelocity,
    ReconstructedPrevNearestDepth,
}

/// <summary>
/// What a binding of one of AMD's passes holds: AMD's resource identifiers (ffx_fsr3upscaler_resources.h), with each
/// ping-pong pair split into the image that the frame reads and the one that it writes. The images come first, up to
/// <see cref="FrameInfo"/>, then the buffers, then the sampler.
/// </summary>
internal enum Fsr3Slot
{
    InputColor,
    InputOpaqueOnly,
    InputMotionVectors,
    InputDepth,
    InputExposure,

    /// <summary>The reactive mask: the generator's output and the upscale's input.</summary>
    ReactiveMask,
    TransparencyAndCompositionMask,
    UpscaledOutput,
    ReconstructedPreviousNearestDepth,
    DilatedMotionVectors,
    DilatedDepth,
    InternalUpscaledColorRead,
    InternalUpscaledColorWrite,
    AccumulationRead,
    AccumulationWrite,
    LumaHistoryRead,
    LumaHistoryWrite,
    CurrentLuma,
    PreviousLuma,
    FarthestDepth,
    FarthestDepthMip1,
    LumaInstability,
    ShadingChange,
    NewLocks,
    DilatedReactiveMasks,

    /// <summary>Every level of the luminance pyramid, sampled.</summary>
    SpdMips,
    SpdMip0,
    SpdMip1,
    SpdMip2,
    SpdMip3,
    SpdMip4,
    SpdMip5,

    /// <summary>Level 6 of the luminance pyramid, which only the engine's clear step binds, as it binds levels 7 to 12.</summary>
    SpdMip6,
    SpdMip7,
    SpdMip8,
    SpdMip9,
    SpdMip10,
    SpdMip11,
    SpdMip12,
    SpdAtomic,
    RcasInput,
    FrameInfo,

    /// <summary>The engine's exposure buffer, which the exposure step reads.</summary>
    ExposureBuffer,
    Constants,
    SpdConstants,
    ReactiveConstants,
    RcasConstants,

    /// <summary>AMD's linear clamp sampler, immutable in the set layout.</summary>
    LinearClamp,
}

/// <summary>One binding of set 0 of one of AMD's passes: its number, what it holds and its descriptor type.</summary>
internal readonly record struct Fsr3Binding(uint Binding, Fsr3Slot Slot, DescriptorType Type);

/// <summary>
/// The bindings of AMD's compiled passes, by the reflection of their SPIR-V (ThirdParty/Fsr3/README.md, "Bindings"):
/// only the bindings that a module keeps after glslang removes the unused ones. AMD's host binds by name
/// (ffx_fsr3upscaler.cpp, the binding tables), and the names here are those of AMD's tables.
/// </summary>
internal static class Fsr3Bindings
{
    private static readonly Fsr3Binding[] Rcas =
    [
        Sampled(0, Fsr3Slot.InputExposure),
        Sampled(1, Fsr3Slot.RcasInput),
        Storage(2, Fsr3Slot.UpscaledOutput),
        Uniform(3, Fsr3Slot.Constants),
        Uniform(4, Fsr3Slot.RcasConstants),
    ];

    /// <summary>The most levels of the luminance pyramid that the clear step binds: SpdMip0 to SpdMip12.</summary>
    public const int ClearedSpdLevels = 13;

    // AMD's Vulkan backend shifts sampler bindings by 1000 (ffx_vk.cpp, SAMPLER_BINDING_SHIFT): s_LinearClamp is 1001.
    private const uint LinearClampBinding = 1001;

    private static readonly Fsr3Binding[] GenerateReactive =
    [
        Sampled(0, Fsr3Slot.InputOpaqueOnly),
        Sampled(1, Fsr3Slot.InputColor),
        Storage(2, Fsr3Slot.ReactiveMask),
        Uniform(5, Fsr3Slot.ReactiveConstants),
    ];

    private static readonly Fsr3Binding[] PrepareInputs =
    [
        Sampled(0, Fsr3Slot.InputMotionVectors),
        Sampled(1, Fsr3Slot.InputDepth),
        Sampled(2, Fsr3Slot.InputColor),
        Storage(3, Fsr3Slot.DilatedMotionVectors),
        Storage(4, Fsr3Slot.DilatedDepth),
        Storage(5, Fsr3Slot.ReconstructedPreviousNearestDepth),
        Storage(6, Fsr3Slot.FarthestDepth),
        Storage(7, Fsr3Slot.CurrentLuma),
        Uniform(8, Fsr3Slot.Constants),
    ];

    private static readonly Fsr3Binding[] LumaPyramid =
    [
        Sampled(0, Fsr3Slot.CurrentLuma),
        Sampled(1, Fsr3Slot.FarthestDepth),
        Storage(2, Fsr3Slot.SpdAtomic),
        Storage(3, Fsr3Slot.FrameInfo),
        Storage(9, Fsr3Slot.SpdMip5),
        Storage(10, Fsr3Slot.FarthestDepthMip1),
        Uniform(11, Fsr3Slot.Constants),
        Uniform(12, Fsr3Slot.SpdConstants),
    ];

    private static readonly Fsr3Binding[] ShadingChangePyramid =
    [
        Sampled(0, Fsr3Slot.CurrentLuma),
        Sampled(1, Fsr3Slot.PreviousLuma),
        Sampled(2, Fsr3Slot.DilatedMotionVectors),
        Sampled(3, Fsr3Slot.InputExposure),
        Storage(4, Fsr3Slot.SpdAtomic),
        Storage(5, Fsr3Slot.SpdMip0),
        Storage(6, Fsr3Slot.SpdMip1),
        Storage(7, Fsr3Slot.SpdMip2),
        Storage(8, Fsr3Slot.SpdMip3),
        Storage(9, Fsr3Slot.SpdMip4),
        Storage(10, Fsr3Slot.SpdMip5),
        Uniform(11, Fsr3Slot.Constants),
        Uniform(12, Fsr3Slot.SpdConstants),
    ];

    private static readonly Fsr3Binding[] ShadingChange =
    [
        Sampled(0, Fsr3Slot.SpdMips),
        Storage(1, Fsr3Slot.ShadingChange),
        Uniform(2, Fsr3Slot.Constants),
        Sampler(),
    ];

    private static readonly Fsr3Binding[] PrepareReactivity =
    [
        Sampled(0, Fsr3Slot.ReconstructedPreviousNearestDepth),
        Sampled(1, Fsr3Slot.DilatedMotionVectors),
        Sampled(2, Fsr3Slot.DilatedDepth),
        Sampled(3, Fsr3Slot.ReactiveMask),
        Sampled(4, Fsr3Slot.TransparencyAndCompositionMask),
        Sampled(5, Fsr3Slot.AccumulationRead),
        Sampled(6, Fsr3Slot.ShadingChange),
        Sampled(7, Fsr3Slot.CurrentLuma),
        Sampled(8, Fsr3Slot.InputExposure),
        Storage(9, Fsr3Slot.DilatedReactiveMasks),
        Storage(10, Fsr3Slot.NewLocks),
        Storage(11, Fsr3Slot.AccumulationWrite),
        Uniform(12, Fsr3Slot.Constants),
        Sampler(),
    ];

    private static readonly Fsr3Binding[] LumaInstability =
    [
        Sampled(0, Fsr3Slot.InputExposure),
        Sampled(1, Fsr3Slot.DilatedReactiveMasks),
        Sampled(2, Fsr3Slot.DilatedMotionVectors),
        Sampled(4, Fsr3Slot.LumaHistoryRead),
        Sampled(6, Fsr3Slot.CurrentLuma),
        Storage(7, Fsr3Slot.LumaHistoryWrite),
        Storage(8, Fsr3Slot.LumaInstability),
        Uniform(9, Fsr3Slot.Constants),
        Sampler(),
    ];

    private static readonly Fsr3Binding[] Accumulate =
    [
        Sampled(0, Fsr3Slot.InputExposure),
        Sampled(1, Fsr3Slot.DilatedReactiveMasks),
        Sampled(2, Fsr3Slot.DilatedMotionVectors),
        Sampled(3, Fsr3Slot.InternalUpscaledColorRead),
        Sampled(5, Fsr3Slot.FarthestDepthMip1),
        Sampled(7, Fsr3Slot.LumaInstability),
        Sampled(8, Fsr3Slot.InputColor),
        Storage(9, Fsr3Slot.InternalUpscaledColorWrite),
        Storage(10, Fsr3Slot.UpscaledOutput),
        Storage(11, Fsr3Slot.NewLocks),
        Uniform(12, Fsr3Slot.Constants),
        Sampler(),
    ];

    /// <summary>
    /// The bindings of the engine's clear step, <c>fsr_clear.comp</c>: the images that AMD clears in every frame, the
    /// luminance pyramid level by level, up to <see cref="ClearedSpdLevels"/> levels.
    /// </summary>
    public static readonly Fsr3Binding[] ClearStep =
    [
        Storage(0, Fsr3Slot.ReconstructedPreviousNearestDepth),
        Storage(1, Fsr3Slot.SpdAtomic),
        .. Enumerable.Range(0, ClearedSpdLevels).Select(level => Storage(2 + (uint)level, Fsr3Slot.SpdMip0 + level)),
    ];

    /// <summary>The bindings of <paramref name="pass"/>.</summary>
    public static Fsr3Binding[] Of(Fsr3Pass pass) =>
        pass switch
        {
            Fsr3Pass.GenerateReactive => GenerateReactive,
            Fsr3Pass.PrepareInputs => PrepareInputs,
            Fsr3Pass.LumaPyramid => LumaPyramid,
            Fsr3Pass.ShadingChangePyramid => ShadingChangePyramid,
            Fsr3Pass.ShadingChange => ShadingChange,
            Fsr3Pass.PrepareReactivity => PrepareReactivity,
            Fsr3Pass.LumaInstability => LumaInstability,
            Fsr3Pass.Accumulate => Accumulate,
            Fsr3Pass.Rcas => Rcas,
        };

    /// <summary>
    /// The image of a ping-pong pair that a frame reads, as AMD's <c>fsr3upscalerDispatch</c> picks it: an odd frame reads
    /// the second image of the pair, and an even frame the first. A frame is odd by the count of frames before it.
    /// </summary>
    public static Fsr3Image Read(Fsr3Image first, Fsr3Image second, bool odd) => odd ? second : first;

    /// <summary>The image of a ping-pong pair that a frame writes: the one that it does not read.</summary>
    public static Fsr3Image Written(Fsr3Image first, Fsr3Image second, bool odd) => odd ? first : second;

    /// <summary>The level of the luminance pyramid that a storage slot writes, or null for any other slot.</summary>
    public static uint? SpdLevel(Fsr3Slot slot) =>
        slot is >= Fsr3Slot.SpdMip0 and <= Fsr3Slot.SpdMip12 ? (uint)(slot - Fsr3Slot.SpdMip0) : null;

    private static Fsr3Binding Sampled(uint binding, Fsr3Slot slot) => new(binding, slot, DescriptorType.SampledImage);

    private static Fsr3Binding Storage(uint binding, Fsr3Slot slot) => new(binding, slot, DescriptorType.StorageImage);

    private static Fsr3Binding Uniform(uint binding, Fsr3Slot slot) => new(binding, slot, DescriptorType.UniformBuffer);

    private static Fsr3Binding Sampler() => new(LinearClampBinding, Fsr3Slot.LinearClamp, DescriptorType.Sampler);
}

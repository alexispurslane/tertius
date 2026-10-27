using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The descriptor set layouts, pipeline layouts and pools shared by all passes.
/// <list type="table">
/// <item><term>set 0, frame</term><description>frame constants, instances, materials, lights, occluders, material sampler,
/// frame outlines, puffs, the BRDF tables (bindings 8 and 9) with their sampler (binding 10, immutable), and the exposure
/// buffer (binding 11), which every frame's set shares, and the environment map's specular map (binding 12, partially
/// bound), its sampler (binding 13, immutable) and its irradiance (binding 14, partially bound), which stay empty with the
/// map off, and the tile views of the local shadows (binding 15, partially bound), empty without the atlas. One per frame
/// in flight.</description></item>
/// <item><term>set 1, textures</term>
/// <description>the bindless texture array: variable count, partially bound, update-after-bind. One per frame in flight.</description>
/// </item>
/// <item><term>set 2, G-buffer</term>
/// <description>the G-buffer targets for the direct, ambient and transparent passes (rewritten on resize),
/// the shadow cascades of every cascade set (binding 5) and their comparison sampler (binding 6, immutable), which the vertex stage also reads
/// for the lit puffs, the estimate target (binding 7), the local shadow atlas (binding 8, partially bound), which the
/// comparison sampler also reads and which stays empty without the atlas, and the direct image (binding 9). The estimate
/// target and the direct image are rewritten on resize. One.</description></item>
/// <item><term>set 3, impostor atlas</term><description>the impostor atlas's color and depth-offset arrays and their
/// sampler (immutable), for the billboards in the G-buffer pass. One, written once.</description></item>
/// <item><term>post</term><description>the HDR image for tone mapping, and the summed bloom levels (binding 1) with the
/// linear clamp sampler (binding 2, immutable), which only the tone map with bloom reads. One, rewritten on resize.</description></item>
/// <item><term>glow source</term><description>the image that one draw of the bloom pass reads, with the linear clamp
/// sampler (binding 1, immutable): one for the HDR image and one per bloom level. Rewritten on resize.</description></item>
/// <item><term>meter</term><description>the HDR image and the G-buffer distance for the exposure's meter. One, rewritten on
/// resize.</description></item>
/// <item><term>exposure</term><description>the histogram and the exposure buffer for the meter and the adapt step. One,
/// written once.</description></item>
/// <item><term>environment prefilter</term><description>the environment map's radiance map with its sampler, the
/// irradiance buffer and the buffer of its partial sums, for the prefilter's compute stage. One with the map on, its
/// radiance map rewritten when the map's images grow.</description></item>
/// <item><term>environment levels</term><description>four level views of the radiance map or the specular map as storage
/// images. One per dispatch of the map's mips and of its specular levels with the map on, which the pool holds for the
/// largest map, rewritten when the map's images grow.</description></item>
/// <item><term>occlusion</term><description>set 3 of <see cref="OcclusionLayout"/>: the half-resolution images, the
/// estimate target, and the source and the destination term of a denoise dispatch, as storage images. Two, one per
/// direction of the denoise, rewritten on resize.</description></item>
/// <item><term>SMAA</term><description>the inputs of the three SMAA passes, each with its immutable sampler. One when SMAA
/// is on, its screen-sized images rewritten on resize.</description></item>
/// <item><term>pyramid level</term><description>set 3 of <see cref="PyramidLayout"/>: the level that one dispatch of a
/// pyramid's build reads and the level that it writes. One per level of each pyramid, rewritten on resize.</description></item>
/// <item><term>depth tail</term><description>set 3 of <see cref="DepthTailLayout"/> for the tail of the depth pyramid's
/// build: the mip before the tail and the tail's mips. One, rewritten on resize.</description></item>
/// <item><term>reflection</term><description>set 3 of <see cref="ReflectionLayout"/>: both pyramids, their trilinear
/// sampler (immutable), the HDR image as a storage image and, at half resolution, the trace image and the hit depth image
/// as storage images at bindings 4 and 5, for the trace and the upsample. One, rewritten on resize.</description></item>
/// <item><term>scatter</term><description>set 3 of <see cref="ScatterLayout"/>: the scatter image, the row image and the
/// HDR image as storage images, and the kernel as a uniform buffer, for the skin scatter's passes. One, allocated under
/// <see cref="SeparableSubsurface"/> and rewritten on resize.</description></item>
/// <item><term>deform</term><description>the set of <see cref="DeformLayout"/>: the deform region's window of the vertex
/// buffer, the deform vertices, the morph deltas, the frame's pose rows and its job table, five storage buffers for the
/// compute stage (docs/plans/skinning-and-morphs.md, "The pass"). One per frame in flight.</description></item>
/// </list>
/// Every pass that draws meshes or reads the G-buffer uses <see cref="SceneLayout"/>, so sets 0 and 1 stay bound
/// across pipeline changes. <see cref="ImpostorLayout"/> adds set 3 and is compatible with it for sets 0 to 2. The
/// compute layouts of the reflections, <see cref="PyramidLayout"/> and <see cref="ReflectionLayout"/>, and of the
/// occlusion, <see cref="OcclusionLayout"/>, and of the skin scatter, <see cref="ScatterLayout"/>, take sets 0 to 2 too, whose bindings that their shaders read include the
/// compute stage (docs/plans/screen-space-reflections.md, "Descriptor sets").
/// </summary>
internal sealed unsafe class DescriptorLayouts : IDisposable
{
    /// <summary>The bindings of <see cref="SmaaSet"/>.</summary>
    private const uint SmaaBindings = 6;

    /// <summary>
    /// The storage buffers of the frame set: bindings 1 to 4, 6, 7, 11, 14 and 15, which the vertex and fragment stages
    /// all see.
    /// </summary>
    public const uint FrameSetStorageBuffers = 9;

    /// <summary>
    /// The storage buffers of the deform set: the region's window, the deform vertices, the deltas, the pose rows and the
    /// job table. At most <see cref="FrameSetStorageBuffers"/>, which every device that the renderer accepts offers a stage.
    /// </summary>
    public const uint DeformSetStorageBuffers = 5;

    /// <summary>
    /// The pyramid level sets of the largest pyramids: levels 1 to 14 of the depth pyramid and 0 to 14 of the radiance
    /// pyramid, for a 16,384 px side (<see cref="DepthPyramid.MaxLevels"/>).
    /// </summary>
    public const uint PyramidLevelSets = 2 * DepthPyramid.MaxLevels + 1;

    /// <summary>The levels sets of the largest map: one per dispatch of its downsample and of its specular map.</summary>
    private static readonly uint EnvironmentLevelsSets = (uint)(
        PrefilterLevels.DownsampleDispatches(PrefilterLevels.RadianceLevels(EnvironmentMapSetting.MaxFaceSize))
        + PrefilterLevels.Dispatches(PrefilterLevels.Count(EnvironmentMapSetting.MaxFaceSize))
    );

    private readonly VulkanDevice device;
    private readonly DescriptorPool pool;
    private readonly DescriptorPool texturePool;

    /// <summary>The storage descriptors of each set layout that this object made, by handle.</summary>
    private readonly Dictionary<ulong, StorageDescriptors> setStorage = [];

    /// <summary>The storage descriptors of each pipeline layout that this object made, by handle.</summary>
    private readonly Dictionary<ulong, StorageDescriptors> layoutStorage = [];

    /// <param name="glowLevels">The bloom levels, 0 without bloom: the pool holds one glow source set more than that.</param>
    /// <exception cref="NotSupportedException">The device's stages see fewer storage buffers than the frame set holds.</exception>
    public DescriptorLayouts(VulkanDevice device, int framesInFlight, int glowLevels)
    {
        if (StorageBufferProblem(device.Properties.Limits.MaxPerStageDescriptorStorageBuffers) is { } storageBuffers)
            throw new NotSupportedException($"{storageBuffers} ({device.DeviceName})");
        this.device = device;
        LinearClampSampler = CreateLevelZeroSampler(Filter.Linear, "linear clamp sampler");
        PointSampler = CreateLevelZeroSampler(Filter.Nearest, "point sampler");
        EnvironmentSampler = CreateTrilinearClampSampler("environment sampler");
        FrameSet = CreateFrameSetLayout(LinearClampSampler, EnvironmentSampler);
        TextureSet = CreateTextureSetLayout();
        ShadowSampler = CreateShadowSampler();
        GBufferSet = CreateGBufferSetLayout(ShadowSampler);
        PostSet = CreateImagesLayout(images: 2, sampler: LinearClampSampler, name: "post set");
        GlowSourceSet = CreateImagesLayout(images: 1, sampler: LinearClampSampler, name: "glow source set");
        AtlasSampler = CreateTrilinearClampSampler("impostor atlas sampler");
        AtlasSet = CreateImagesLayout(images: 2, sampler: AtlasSampler, name: "impostor atlas set");
        (MeterSet, ExposureSet) = CreateExposureSetLayouts();
        (EnvironmentPrefilterSet, EnvironmentLevelsSet) = CreateEnvironmentSetLayouts(EnvironmentSampler);
        OcclusionSet = CreateOcclusionSetLayout();
        SmaaSet = CreateSmaaSetLayout();
        (PyramidLevelSet, DepthTailSet, ReflectionSet) = CreateReflectionSetLayouts(AtlasSampler);
        DeformSet = CreateDeformSetLayout();
        ScatterSet = CreateScatterSetLayout();

        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        var drawPush = new PushConstantRange(graphics, 0, (uint)Unsafe.SizeOf<DrawPushConstants>());
        ReadOnlySpan<DescriptorSetLayout> sceneSets = [FrameSet, TextureSet, GBufferSet, AtlasSet];
        SceneLayout = CreatePipelineLayout(sets: sceneSets[..3], push: drawPush, name: "scene layout");
        ImpostorLayout = CreatePipelineLayout(sets: sceneSets, push: drawPush, name: "impostor layout");
        OverlayLayout = CreatePipelineLayout(
            sets: sceneSets[..3],
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<OutlinePushConstants>()),
            name: "overlay layout"
        );
        PostLayout = CreatePipelineLayout(
            sets: [PostSet],
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<ToneMapPushConstants>()),
            name: "post layout"
        );
        BloomLayout = CreatePipelineLayout(
            sets: [GlowSourceSet],
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<BloomPushConstants>()),
            name: "bloom layout"
        );
        SmaaLayout = CreatePipelineLayout(
            sets: [SmaaSet],
            push: new(graphics, 0, (uint)Unsafe.SizeOf<SmaaPushConstants>()),
            name: "smaa layout"
        );
        (
            ExposureLayout,
            EnvironmentPrefilterLayout,
            OcclusionLayout,
            PyramidLayout,
            DepthTailLayout,
            ReflectionLayout,
            DeformLayout,
            ScatterLayout
        ) = CreateComputeLayouts();

        (pool, texturePool) = CreatePools(framesInFlight: (uint)framesInFlight, glowSourceSets: (uint)glowLevels + 1);
    }

    public DescriptorSetLayout FrameSet { get; }
    public DescriptorSetLayout TextureSet { get; }
    public DescriptorSetLayout GBufferSet { get; }
    public DescriptorSetLayout PostSet { get; }

    /// <summary>The one image that a draw of the bloom pass reads, with the linear clamp sampler.</summary>
    public DescriptorSetLayout GlowSourceSet { get; }

    /// <summary>Set 0 of <see cref="ExposureLayout"/>: the HDR image and the G-buffer distance, for the compute stage.</summary>
    public DescriptorSetLayout MeterSet { get; }

    /// <summary>Set 1 of <see cref="ExposureLayout"/>: the histogram and the exposure buffer, for the compute stage.</summary>
    public DescriptorSetLayout ExposureSet { get; }

    /// <summary>
    /// Set 0 of <see cref="EnvironmentPrefilterLayout"/>: the radiance map and its sampler, the irradiance buffer and the
    /// buffer of its partial sums (docs/plans/environment-map.md, "Images and buffers").
    /// </summary>
    public DescriptorSetLayout EnvironmentPrefilterSet { get; }

    /// <summary>
    /// Set 1 of <see cref="EnvironmentPrefilterLayout"/>: four level views of one of the map's images, each of every map's
    /// layers (six per map), as storage images in bindings 0 to 3. A dispatch of the specular map writes them, and a
    /// dispatch of the downsample reads binding 0 and writes the others.
    /// </summary>
    public DescriptorSetLayout EnvironmentLevelsSet { get; }

    /// <summary>
    /// Trilinear filtering over every level, clamped to the edge: the immutable sampler of the environment map's cubes,
    /// in the frame set (binding 13) and the prefilter set (binding 1). Vulkan filters a cube across its faces.
    /// </summary>
    public Sampler EnvironmentSampler { get; }

    /// <summary>
    /// The shadow map sampler: hardware PCF (linear filtering of GREATER_OR_EQUAL comparisons, so a receiver at least
    /// as near the light as the stored caster is lit), clamped to the edge.
    /// </summary>
    public Sampler ShadowSampler { get; }

    /// <summary>
    /// Linear filtering, clamped to the edge, one level and no anisotropy: the immutable sampler of the BRDF tables (frame
    /// set, binding 10), the post set (binding 2), every glow source set (binding 1) and every binding of the SMAA set but
    /// the edge pass's color.
    /// </summary>
    public Sampler LinearClampSampler { get; }

    /// <summary>Point sampling, clamped to the edge, one level, for the color that the SMAA edge pass reads.</summary>
    public Sampler PointSampler { get; }

    /// <summary>
    /// The inputs of the SMAA passes, combined image samplers for the fragment stage: 0 the tone-mapped image as sRGB-encoded
    /// values (point), 1 the edges, 2 the area table, 3 the search table, 4 the tone-mapped image as linear values, 5 the
    /// weights.
    /// </summary>
    public DescriptorSetLayout SmaaSet { get; }

    /// <summary>The SMAA set plus <see cref="SmaaPushConstants"/> for the vertex and fragment stages.</summary>
    public PipelineLayout SmaaLayout { get; }

    /// <summary>The impostor atlas: color and depth offset (2D arrays) and <see cref="AtlasSampler"/>.</summary>
    public DescriptorSetLayout AtlasSet { get; }

    /// <summary>Trilinear filtering, clamped to the edge, for the impostor atlas and the trace's reads of the pyramids.</summary>
    public Sampler AtlasSampler { get; }

    /// <summary>Sets 0 to 2 plus <see cref="DrawPushConstants"/> for the vertex and fragment stages.</summary>
    public PipelineLayout SceneLayout { get; }

    /// <summary>
    /// <see cref="SceneLayout"/> plus the atlas set as set 3, for impostor billboards. The two have the same push
    /// constants and sets 0 to 2, so binding set 3 with this layout keeps the scene sets bound.
    /// </summary>
    public PipelineLayout ImpostorLayout { get; }

    /// <summary>
    /// Sets 0 to 2, as in <see cref="SceneLayout"/>, plus <see cref="OutlinePushConstants"/> for the fragment stage, for
    /// the outline pass. Its push constants differ, so it is not compatible with <see cref="SceneLayout"/>: the pass binds
    /// the scene sets again with it.
    /// </summary>
    public PipelineLayout OverlayLayout { get; }

    /// <summary>The post set plus <see cref="ToneMapPushConstants"/>.</summary>
    public PipelineLayout PostLayout { get; }

    /// <summary>The glow source set plus <see cref="BloomPushConstants"/>.</summary>
    public PipelineLayout BloomLayout { get; }

    /// <summary>The meter set and the exposure set plus <see cref="ExposureAdaptPushConstants"/>, for both compute pipelines.</summary>
    public PipelineLayout ExposureLayout { get; }

    /// <summary>
    /// The prefilter set and the levels set of one dispatch, plus <see cref="EnvironmentPrefilterPushConstants"/>, for the
    /// radiance map's mips, the prefilter and the irradiance. So a pipeline needs four storage images, the four that
    /// every Vulkan device offers per stage.
    /// </summary>
    public PipelineLayout EnvironmentPrefilterLayout { get; }

    /// <summary>
    /// Set 3 of <see cref="OcclusionLayout"/>: the view depth, the edges, term A and the estimate target (bindings 0 to
    /// 3), the source and the destination term of a denoise dispatch (bindings 4 and 5), and the depth pass's copies of
    /// the direct light and the geometric normal (bindings 6 and 7), as storage images
    /// (docs/plans/screen-space-illumination.md, "Push constants and sets").
    /// </summary>
    public DescriptorSetLayout OcclusionSet { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/>, the occlusion set and <see cref="OcclusionPushConstants"/>, for the occlusion passes.</summary>
    public PipelineLayout OcclusionLayout { get; }

    /// <summary>
    /// The level that one dispatch of a pyramid's build reads, a sampled image (binding 0), and the level that it writes,
    /// a storage image (binding 1), for the compute stage.
    /// </summary>
    public DescriptorSetLayout PyramidLevelSet { get; }

    /// <summary>
    /// The mip before the depth pyramid's tail, a sampled image (binding 0), and the tail's mips, an array of
    /// <see cref="DepthPyramidTail.Mips"/> storage images (binding 1), for the compute stage.
    /// </summary>
    public DescriptorSetLayout DepthTailSet { get; }

    /// <summary>
    /// The trace's set: the depth pyramid and the radiance pyramid, sampled with all their mips (bindings 0 and 1),
    /// <see cref="AtlasSampler"/> (binding 2, immutable), the HDR image as a storage image (binding 3), and the
    /// half-resolution trace and its hit depths as storage images (bindings 4 and 5), written only at half resolution.
    /// </summary>
    public DescriptorSetLayout ReflectionSet { get; }

    /// <summary>
    /// Sets 0 to 2 of <see cref="SceneLayout"/>, a pyramid level set and <see cref="DepthTailPushConstants"/>, for the
    /// pyramid builds' dispatches of one level, which read no push constants.
    /// </summary>
    public PipelineLayout PyramidLayout { get; }

    /// <summary>
    /// <see cref="PyramidLayout"/> with the depth tail set in place of the level set, for the tail. Its push constants
    /// are the same, so the scene sets that the builds bind stay bound across the two layouts.
    /// </summary>
    public PipelineLayout DepthTailLayout { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/>, the reflection set and <see cref="ReflectionPushConstants"/>, for the trace and the upsample.</summary>
    public PipelineLayout ReflectionLayout { get; }

    /// <summary>
    /// Set 3 of <see cref="ScatterLayout"/>: the scatter image, the row image and the HDR image as storage images (bindings 0
    /// to 2), and the kernel as a uniform buffer (binding 3) (Shaders/skin_scatter.glsl).
    /// </summary>
    public DescriptorSetLayout ScatterSet { get; }

    /// <summary>Sets 0 to 2 of <see cref="SceneLayout"/> and the scatter set, with no push constants, for the scatter passes.</summary>
    public PipelineLayout ScatterLayout { get; }

    /// <summary>The deform pass's one set: the region's window, the deform vertices, the morph deltas, the pose rows and the job table.</summary>
    public DescriptorSetLayout DeformSet { get; }

    /// <summary>The deform pass's layout: the deform set and the push constants of its one dispatch.</summary>
    public PipelineLayout DeformLayout { get; }

    /// <summary>
    /// The storage image descriptors in all the sets of <paramref name="layout"/>: at least what one stage of it reads, the
    /// count that the device limits of docs/plans/environment-map.md, "Portable Vulkan", bound.
    /// </summary>
    public uint StorageImagesOf(PipelineLayout layout) => layoutStorage[layout.Handle].Images;

    /// <summary>
    /// The storage buffer descriptors in all the sets of <paramref name="layout"/>: at least what one stage of it reads, the
    /// count that maxPerStageDescriptorStorageBuffers bounds.
    /// </summary>
    public uint StorageBuffersOf(PipelineLayout layout) => layoutStorage[layout.Handle].Buffers;

    /// <summary>
    /// Why a device that binds at most <paramref name="maxPerStageStorageBuffers"/> storage buffers in one shader stage
    /// cannot bind the frame set, or null when it can: its vertex and fragment stages see
    /// <see cref="FrameSetStorageBuffers"/>, and Vulkan guarantees only 4.
    /// </summary>
    internal static string? StorageBufferProblem(uint maxPerStageStorageBuffers) =>
        maxPerStageStorageBuffers < FrameSetStorageBuffers
            ? $"The frame set binds {FrameSetStorageBuffers} storage buffers in the vertex and fragment stages, above the device's maxPerStageDescriptorStorageBuffers of {maxPerStageStorageBuffers}."
            : null;

    public DescriptorSet AllocateFrameSet() => Allocate(pool, FrameSet, null);

    public DescriptorSet AllocateGBufferSet() => Allocate(pool, GBufferSet, null);

    public DescriptorSet AllocatePostSet() => Allocate(pool, PostSet, null);

    public DescriptorSet AllocateGlowSourceSet() => Allocate(pool, GlowSourceSet, null);

    public DescriptorSet AllocateAtlasSet() => Allocate(pool, AtlasSet, null);

    public DescriptorSet AllocateMeterSet() => Allocate(pool, MeterSet, null);

    public DescriptorSet AllocateExposureSet() => Allocate(pool, ExposureSet, null);

    public DescriptorSet AllocateEnvironmentPrefilterSet() => Allocate(pool, EnvironmentPrefilterSet, null);

    public DescriptorSet AllocateEnvironmentLevelsSet() => Allocate(pool, EnvironmentLevelsSet, null);

    public DescriptorSet AllocateOcclusionSet() => Allocate(pool, OcclusionSet, null);

    public DescriptorSet AllocateSmaaSet() => Allocate(pool, SmaaSet, null);

    public DescriptorSet AllocatePyramidLevelSet() => Allocate(pool, PyramidLevelSet, null);

    public DescriptorSet AllocateDepthTailSet() => Allocate(pool, DepthTailSet, null);

    public DescriptorSet AllocateReflectionSet() => Allocate(pool, ReflectionSet, null);

    public DescriptorSet AllocateDeformSet() => Allocate(pool, DeformSet, null);

    public DescriptorSet AllocateScatterSet() => Allocate(pool, ScatterSet, null);

    public DescriptorSet AllocateTextureSet()
    {
        uint count = TextureStore.Capacity;
        var variable = new DescriptorSetVariableDescriptorCountAllocateInfo
        {
            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
            DescriptorSetCount = 1,
            PDescriptorCounts = &count,
        };
        return Allocate(texturePool, TextureSet, &variable);
    }

    public void WriteBuffer(DescriptorSet set, uint binding, DescriptorType type, GpuBuffer buffer) =>
        WriteBuffer(set: set, binding: binding, type: type, buffer: buffer, offset: 0, range: Vk.WholeSize);

    /// <summary>Points <paramref name="binding"/> at <paramref name="range"/> bytes of <paramref name="buffer"/> from <paramref name="offset"/>.</summary>
    public void WriteBuffer(
        DescriptorSet set,
        uint binding,
        DescriptorType type,
        GpuBuffer buffer,
        ulong offset,
        ulong range
    )
    {
        var info = new DescriptorBufferInfo(buffer.Handle, offset, range);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = type,
            PBufferInfo = &info,
        };
        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: 1,
            pDescriptorWrites: &write,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    public void WriteSampler(DescriptorSet set, uint binding, Sampler sampler)
    {
        var info = new DescriptorImageInfo(sampler, default, ImageLayout.Undefined);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.Sampler,
            PImageInfo = &info,
        };
        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: 1,
            pDescriptorWrites: &write,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    /// <summary>Writes a sampled image that shaders read in <see cref="ImageLayout.ShaderReadOnlyOptimal"/>.</summary>
    public void WriteImage(DescriptorSet set, uint binding, uint arrayElement, GpuImage image) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: arrayElement,
            type: DescriptorType.SampledImage,
            view: image.View,
            layout: ImageLayout.ShaderReadOnlyOptimal
        );

    /// <summary>Writes a view of a sampled image that shaders read in <paramref name="layout"/>.</summary>
    public void WriteImage(DescriptorSet set, uint binding, ImageView view, ImageLayout layout) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: 0,
            type: DescriptorType.SampledImage,
            view: view,
            layout: layout
        );

    /// <summary>Writes a storage image in the General layout.</summary>
    public void WriteStorageImage(DescriptorSet set, uint binding, GpuImage image) =>
        WriteStorageImage(set: set, binding: binding, view: image.View);

    /// <summary>Writes a view of a storage image in the General layout, such as one mip of a pyramid.</summary>
    public void WriteStorageImage(DescriptorSet set, uint binding, ImageView view) =>
        WriteStorageImage(set: set, binding: binding, arrayElement: 0, view: view);

    /// <summary>Writes a view of a storage image in the General layout into one element of an array binding.</summary>
    public void WriteStorageImage(DescriptorSet set, uint binding, uint arrayElement, ImageView view) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: arrayElement,
            type: DescriptorType.StorageImage,
            view: view,
            layout: ImageLayout.General
        );

    /// <summary>Writes the view of a combined image sampler whose sampler is immutable, read in <see cref="ImageLayout.ShaderReadOnlyOptimal"/>.</summary>
    public void WriteSampledView(DescriptorSet set, uint binding, ImageView view) =>
        WriteView(
            set: set,
            binding: binding,
            arrayElement: 0,
            type: DescriptorType.CombinedImageSampler,
            view: view,
            layout: ImageLayout.ShaderReadOnlyOptimal
        );

    /// <summary>Writes one image descriptor of <paramref name="type"/>.</summary>
    private void WriteView(
        DescriptorSet set,
        uint binding,
        uint arrayElement,
        DescriptorType type,
        ImageView view,
        ImageLayout layout
    )
    {
        var info = new DescriptorImageInfo(default, view, layout);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = arrayElement,
            DescriptorCount = 1,
            DescriptorType = type,
            PImageInfo = &info,
        };
        device.Vk.UpdateDescriptorSets(
            device: device.Device,
            descriptorWriteCount: 1,
            pDescriptorWrites: &write,
            descriptorCopyCount: 0,
            pDescriptorCopies: null
        );
    }

    /// <summary>A binding of one sampled image.</summary>
    private static DescriptorSetLayoutBinding Image(uint binding, ShaderStageFlags stages) =>
        new(binding: binding, descriptorType: DescriptorType.SampledImage, descriptorCount: 1, stageFlags: stages);

    /// <summary>A binding of one immutable sampler, which must outlive the creation of the layout.</summary>
    private static DescriptorSetLayoutBinding ImmutableSampler(
        uint binding,
        ShaderStageFlags stages,
        Sampler* sampler
    ) =>
        new(
            binding: binding,
            descriptorType: DescriptorType.Sampler,
            descriptorCount: 1,
            stageFlags: stages,
            pImmutableSamplers: sampler
        );

    /// <summary>
    /// A layout of <paramref name="images"/> sampled images for the fragment stage, at bindings 0 on, and
    /// <paramref name="sampler"/> as the immutable sampler of the binding after them.
    /// </summary>
    private DescriptorSetLayout CreateImagesLayout(int images, Sampler sampler, string name)
    {
        Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[images + 1];
        for (int i = 0; i < images; i++)
            bindings[i] = Image(binding: (uint)i, stages: ShaderStageFlags.FragmentBit);
        bindings[images] = ImmutableSampler(
            binding: (uint)images,
            stages: ShaderStageFlags.FragmentBit,
            sampler: &sampler
        );
        return CreateLayout(bindings: bindings, name: name);
    }

    /// <summary>
    /// The layout of the bindless textures. They live in their own set because only update-after-bind layouts may hold
    /// more than maxPerStageDescriptorSampledImages (256 on MoltenVK) descriptors.
    /// </summary>
    private DescriptorSetLayout CreateTextureSetLayout()
    {
        Span<DescriptorSetLayoutBinding> textures =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: TextureStore.Capacity,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
        ];
        DescriptorBindingFlags textureFlags =
            DescriptorBindingFlags.PartiallyBoundBit
            | DescriptorBindingFlags.VariableDescriptorCountBit
            | DescriptorBindingFlags.UpdateAfterBindBit;
        var bindingFlags = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &textureFlags,
        };
        return CreateLayout(
            bindings: textures,
            name: "texture set",
            next: &bindingFlags,
            flags: DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit
        );
    }

    /// <summary>
    /// The layout of the G-buffer set: five G-buffer images, the shadow cascades, <paramref name="shadowSampler"/> as an
    /// immutable sampler, the estimate target, the local shadow atlas, partially bound, the direct image and the shading
    /// target, the sixth G-buffer image. The sampler
    /// is immutable because portability implementations may not allow updating comparison sampler descriptors
    /// (VkPhysicalDevicePortabilitySubsetFeaturesKHR::mutableComparisonSamplers).
    /// </summary>
    private DescriptorSetLayout CreateGBufferSetLayout(Sampler shadowSampler)
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        // Compute shaders read the G-buffer: the pyramid builds and the trace of the reflections, and the occlusion
        // passes, which read the normals (binding 1), the distance (binding 4) and, in the sectors pass, the shading
        // target (binding 10).
        const ShaderStageFlags shading = ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit;
        Span<DescriptorSetLayoutBinding> gbuffer =
        [
            Image(binding: 0, stages: shading),
            Image(binding: 1, stages: shading),
            Image(binding: 2, stages: shading),
            Image(binding: 3, stages: shading),
            Image(binding: 4, stages: shading),
            // The vertex shader of the lit puffs samples the cascades once per puff, at its center.
            Image(binding: 5, stages: graphics),
            ImmutableSampler(binding: 6, stages: graphics, sampler: &shadowSampler),
            Image(binding: RenderTargets.EstimateBinding, stages: shading),
            // The lit puffs sample the tiles in the vertex stage, as they do the cascades. The reflections' and the
            // occlusion passes' compute shaders declare the atlas but never use it.
            Image(binding: ShadowAtlas.AtlasBinding, stages: graphics),
            // Compute too, for the occlusion passes that gather the bounce from the direct image
            // (docs/plans/screen-space-illumination.md, "Push constants and sets").
            Image(binding: RenderTargets.DirectBinding, stages: shading),
            // The reflections' compute passes read each pixel's surface, its model included, and the occlusion sectors
            // pass reads a receiver's model, because a hair receiver's own card blocks nothing.
            Image(binding: RenderTargets.ShadingBinding, stages: shading),
        ];
        Span<DescriptorBindingFlags> flags = stackalloc DescriptorBindingFlags[gbuffer.Length];
        flags[(int)ShadowAtlas.AtlasBinding] = DescriptorBindingFlags.PartiallyBoundBit;
        return CreateLayout(bindings: gbuffer, name: "g-buffer set", bindingFlags: flags);
    }

    /// <summary>
    /// The pipeline layouts of the compute passes: the exposure, the environment map's prefilter, the occlusion, the
    /// pyramids, the trace, the deformation and the skin scatter.
    /// </summary>
    private (
        PipelineLayout Exposure,
        PipelineLayout EnvironmentPrefilter,
        PipelineLayout Occlusion,
        PipelineLayout Pyramid,
        PipelineLayout DepthTail,
        PipelineLayout Reflection,
        PipelineLayout Deform,
        PipelineLayout Scatter
    ) CreateComputeLayouts()
    {
        PipelineLayout exposure = CreatePipelineLayout(
            sets: [MeterSet, ExposureSet],
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<ExposureAdaptPushConstants>()),
            name: "exposure layout"
        );
        PipelineLayout environmentPrefilter = CreatePipelineLayout(
            sets: [EnvironmentPrefilterSet, EnvironmentLevelsSet],
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<EnvironmentPrefilterPushConstants>()),
            name: "environment prefilter layout"
        );
        PipelineLayout occlusion = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, OcclusionSet],
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<OcclusionPushConstants>()),
            name: "occlusion layout"
        );
        var tailPush = new PushConstantRange(
            ShaderStageFlags.ComputeBit,
            0,
            (uint)Unsafe.SizeOf<DepthTailPushConstants>()
        );
        PipelineLayout pyramid = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, PyramidLevelSet],
            push: tailPush,
            name: "pyramid layout"
        );
        PipelineLayout depthTail = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, DepthTailSet],
            push: tailPush,
            name: "depth tail layout"
        );
        PipelineLayout reflection = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, ReflectionSet],
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<ReflectionPushConstants>()),
            name: "reflection layout"
        );
        // The deform set alone, so its compute stage sees five storage buffers, within the nine that StorageBufferProblem
        // already requires of every device.
        PipelineLayout deform = CreatePipelineLayout(
            sets: [DeformSet],
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<DeformPushConstants>()),
            name: "deform layout"
        );
        PipelineLayout scatter = CreatePipelineLayout(
            sets: [FrameSet, TextureSet, GBufferSet, ScatterSet],
            push: null,
            name: "scatter layout"
        );
        return (exposure, environmentPrefilter, occlusion, pyramid, depthTail, reflection, deform, scatter);
    }

    /// <summary>The shared pool, which holds <paramref name="glowSourceSets"/> glow source sets, and the pool of the texture sets.</summary>
    private (DescriptorPool Shared, DescriptorPool Textures) CreatePools(uint framesInFlight, uint glowSourceSets)
    {
        uint frames = framesInFlight;
        Span<DescriptorPoolSize> sizes =
        [
            // One per frame set, and the scatter set's kernel.
            new(DescriptorType.UniformBuffer, frames + 1),
            // Those of each frame set and each deform set, the two of the exposure set and the two of the prefilter set.
            new(DescriptorType.StorageBuffer, (FrameSetStorageBuffers + DeformSetStorageBuffers) * frames + 4),
            // Three per frame set (the material sampler and the immutable linear clamp and environment samplers), the
            // immutable samplers of the G-buffer, atlas, post, reflection and prefilter sets, and one per glow source set.
            new(DescriptorType.Sampler, 3 * frames + 5 + glowSourceSets),
            // 20 for the G-buffer, post and atlas sets, three per frame set for the tables and the specular map, one per
            // glow source set, two for the meter set, one per pyramid level set, one for the depth tail set, two for the
            // reflection set and one for the prefilter set.
            new(DescriptorType.SampledImage, 20 + 3 * frames + glowSourceSets + 2 + PyramidLevelSets + 1 + 2 + 1),
            // Those of each of the two occlusion sets, one per pyramid level set, the depth tail set's mips, three for the
            // reflection set, four per environment levels set and three for the scatter set.
            new(
                DescriptorType.StorageImage,
                2 * OcclusionImages.StorageImages
                    + PyramidLevelSets
                    + DepthPyramidTail.Mips
                    + 3
                    + 4 * EnvironmentLevelsSets
                    + 3
            ),
            // The SMAA set.
            new(DescriptorType.CombinedImageSampler, SmaaBindings),
        ];
        // The frame sets, the deform sets, the G-buffer, post, atlas, meter, exposure, SMAA, reflection, prefilter and
        // scatter sets, the two occlusion sets, the glow source sets, the pyramid level sets, the depth tail set and the
        // environment levels sets. The reflection sets exist even with reflections off: they cost nothing per frame.
        DescriptorPool shared = CreatePool(
            sizes: sizes,
            maxSets: 2 * frames + 12 + glowSourceSets + PyramidLevelSets + EnvironmentLevelsSets,
            flags: 0,
            name: "descriptor pool"
        );
        Span<DescriptorPoolSize> textureSizes = [new(DescriptorType.SampledImage, frames * TextureStore.Capacity)];
        DescriptorPool textures = CreatePool(
            sizes: textureSizes,
            maxSets: frames,
            flags: DescriptorPoolCreateFlags.UpdateAfterBindBit,
            name: "texture descriptor pool"
        );
        return (shared, textures);
    }

    /// <summary>The layouts of the meter set and the exposure set, each with two bindings that the compute stage reads.</summary>
    private (DescriptorSetLayout Meter, DescriptorSetLayout Exposure) CreateExposureSetLayouts()
    {
        Span<DescriptorSetLayoutBinding> meter =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            Image(binding: 1, stages: ShaderStageFlags.ComputeBit),
        ];
        Span<DescriptorSetLayoutBinding> exposure =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
            new(
                binding: 1,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
        ];
        return (
            CreateLayout(bindings: meter, name: "meter set"),
            CreateLayout(bindings: exposure, name: "exposure set")
        );
    }

    /// <summary>
    /// The layouts of the prefilter set, which holds the radiance map, <paramref name="environmentSampler"/> as its
    /// immutable sampler, the irradiance buffer and its partial sums, and of the levels set, which holds four storage
    /// images.
    /// </summary>
    private (DescriptorSetLayout Prefilter, DescriptorSetLayout Levels) CreateEnvironmentSetLayouts(
        Sampler environmentSampler
    )
    {
        Span<DescriptorSetLayoutBinding> prefilter =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            ImmutableSampler(binding: 1, stages: ShaderStageFlags.ComputeBit, sampler: &environmentSampler),
            new(
                binding: 2,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
            new(
                binding: 3,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
        ];
        Span<DescriptorSetLayoutBinding> levels =
        [
            StorageImage(binding: 0),
            StorageImage(binding: 1),
            StorageImage(binding: 2),
            StorageImage(binding: 3),
        ];
        return (
            CreateLayout(bindings: prefilter, name: "environment prefilter set"),
            CreateLayout(bindings: levels, name: "environment levels set")
        );
    }

    /// <summary>The layout of the occlusion set, whose bindings the compute stage reads.</summary>
    private DescriptorSetLayout CreateOcclusionSetLayout()
    {
        Span<DescriptorSetLayoutBinding> occlusion =
            stackalloc DescriptorSetLayoutBinding[OcclusionImages.StorageImages];
        for (int binding = 0; binding < occlusion.Length; binding++)
            occlusion[binding] = StorageImage(binding: (uint)binding);
        return CreateLayout(bindings: occlusion, name: "occlusion set");
    }

    /// <summary>
    /// The layouts of the pyramid level set, the depth tail set and the reflection set (docs/plans/screen-space-reflections.md, "Descriptor
    /// sets"), whose bindings the compute stage reads. <paramref name="mipSampler"/> is the trace's immutable sampler.
    /// </summary>
    private (
        DescriptorSetLayout Level,
        DescriptorSetLayout DepthTail,
        DescriptorSetLayout Reflection
    ) CreateReflectionSetLayouts(Sampler mipSampler)
    {
        Span<DescriptorSetLayoutBinding> level =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            StorageImage(binding: 1),
        ];
        Span<DescriptorSetLayoutBinding> tail =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            StorageImage(binding: 1) with
            {
                DescriptorCount = DepthPyramidTail.Mips,
            },
        ];
        Span<DescriptorSetLayoutBinding> reflection =
        [
            Image(binding: 0, stages: ShaderStageFlags.ComputeBit),
            Image(binding: 1, stages: ShaderStageFlags.ComputeBit),
            ImmutableSampler(binding: 2, stages: ShaderStageFlags.ComputeBit, sampler: &mipSampler),
            StorageImage(binding: 3),
            StorageImage(binding: 4),
            StorageImage(binding: 5),
        ];
        return (
            CreateLayout(bindings: level, name: "pyramid level set"),
            CreateLayout(bindings: tail, name: "depth tail set"),
            CreateLayout(bindings: reflection, name: "reflection set")
        );
    }

    /// <summary>The layout of the deform set: <see cref="DeformSetStorageBuffers"/> storage buffers for the compute stage.</summary>
    private DescriptorSetLayout CreateDeformSetLayout()
    {
        Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[(int)DeformSetStorageBuffers];
        for (uint binding = 0; binding < DeformSetStorageBuffers; binding++)
            bindings[(int)binding] = new(
                binding: binding,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            );
        return CreateLayout(bindings: bindings, name: "deform set");
    }

    /// <summary>The layout of the scatter set: three storage images and the kernel's uniform buffer, for the compute stage.</summary>
    private DescriptorSetLayout CreateScatterSetLayout()
    {
        Span<DescriptorSetLayoutBinding> scatter =
        [
            StorageImage(binding: 0),
            StorageImage(binding: 1),
            StorageImage(binding: 2),
            new(
                binding: 3,
                descriptorType: DescriptorType.UniformBuffer,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.ComputeBit
            ),
        ];
        return CreateLayout(bindings: scatter, name: "scatter set");
    }

    /// <summary>A binding of one storage image for the compute stage.</summary>
    private static DescriptorSetLayoutBinding StorageImage(uint binding) =>
        new(
            binding: binding,
            descriptorType: DescriptorType.StorageImage,
            descriptorCount: 1,
            stageFlags: ShaderStageFlags.ComputeBit
        );

    private Sampler CreateShadowSampler()
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            CompareEnable = true,
            CompareOp = CompareOp.GreaterOrEqual,
            MinLod = 0f,
            MaxLod = 0f,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "shadow comparison sampler");
        return sampler;
    }

    /// <summary>
    /// The layout of set 0, whose binding 10 holds <paramref name="linearClamp"/> and binding 13
    /// <paramref name="environment"/> as their immutable samplers. Bindings 12 and 14 are partially bound: they stay
    /// empty with the map off, as the bindless array's unused slots do (docs/plans/environment-map.md, decision 29).
    /// Binding 15, the tile views, is partially bound too: it stays empty without the local shadow atlas.
    /// </summary>
    private DescriptorSetLayout CreateFrameSetLayout(Sampler linearClamp, Sampler environment)
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        // Compute shaders read these bindings: the pyramid builds and the trace of the reflections read the frame
        // constants, the tables and the exposure, and the occlusion passes read the frame constants (binding 0).
        const ShaderStageFlags graphicsAndCompute = graphics | ShaderStageFlags.ComputeBit;
        const ShaderStageFlags shading = ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit;
        Span<DescriptorSetLayoutBinding> frame =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.UniformBuffer,
                descriptorCount: 1,
                stageFlags: graphicsAndCompute
            ),
            new(binding: 1, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 2, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 3, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 4, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 5, descriptorType: DescriptorType.Sampler, descriptorCount: 1, stageFlags: graphics),
            new(binding: 6, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 7, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            // The tables sit in the frame set because the impostor capture binds only sets 0 and 1.
            new(binding: 8, descriptorType: DescriptorType.SampledImage, descriptorCount: 1, stageFlags: shading),
            new(binding: 9, descriptorType: DescriptorType.SampledImage, descriptorCount: 1, stageFlags: shading),
            new(
                binding: 10,
                descriptorType: DescriptorType.Sampler,
                descriptorCount: 1,
                stageFlags: shading,
                pImmutableSamplers: &linearClamp
            ),
            new(
                binding: 11,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: graphicsAndCompute
            ),
            new(binding: 12, descriptorType: DescriptorType.SampledImage, descriptorCount: 1, stageFlags: shading),
            ImmutableSampler(binding: 13, stages: shading, sampler: &environment),
            // The lit puffs evaluate the irradiance's mean in the vertex stage.
            new(
                binding: 14,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: graphicsAndCompute
            ),
            // The lit puffs sample the tiles in the vertex stage. The reflections' compute shaders declare the views but
            // never use them.
            new(
                binding: ShadowAtlas.TileViewsBinding,
                descriptorType: DescriptorType.StorageBuffer,
                descriptorCount: 1,
                stageFlags: graphics
            ),
        ];
        Span<DescriptorBindingFlags> flags = stackalloc DescriptorBindingFlags[frame.Length];
        flags[12] = DescriptorBindingFlags.PartiallyBoundBit;
        flags[14] = DescriptorBindingFlags.PartiallyBoundBit;
        flags[(int)ShadowAtlas.TileViewsBinding] = DescriptorBindingFlags.PartiallyBoundBit;
        return CreateLayout(bindings: frame, name: "frame set", bindingFlags: flags);
    }

    /// <summary>Trilinear, clamped to the edge, over every level: the sampler of the impostor atlas and the environment map's cubes.</summary>
    private Sampler CreateTrilinearClampSampler(string name)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Linear,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0f,
            MaxLod = Vk.LodClampNone,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, name);
        return sampler;
    }

    /// <summary>
    /// The layout of the SMAA set (<see cref="SmaaSet"/>). Every binding has an immutable sampler: point for binding 0,
    /// which the header allows as an option and which gives the linear values at the texel centers that the edge pass
    /// reads, and linear for the others.
    /// </summary>
    private DescriptorSetLayout CreateSmaaSetLayout()
    {
        Sampler* samplers = stackalloc Sampler[(int)SmaaBindings];
        Span<DescriptorSetLayoutBinding> bindings = stackalloc DescriptorSetLayoutBinding[(int)SmaaBindings];
        for (uint binding = 0; binding < SmaaBindings; binding++)
        {
            samplers[binding] = binding == 0 ? PointSampler : LinearClampSampler;
            bindings[(int)binding] = new(
                binding: binding,
                descriptorType: DescriptorType.CombinedImageSampler,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit,
                pImmutableSamplers: &samplers[binding]
            );
        }

        return CreateLayout(bindings: bindings, name: "smaa set");
    }

    /// <summary>A sampler of level 0 only, clamped to the edge, with no anisotropy.</summary>
    private Sampler CreateLevelZeroSampler(Filter filter, string name)
    {
        var info = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = filter,
            MinFilter = filter,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0f,
            MaxLod = 0f,
        };
        device
            .Vk.CreateSampler(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pSampler: out Sampler sampler
            )
            .Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, name);
        return sampler;
    }

    /// <summary>A layout whose bindings carry <paramref name="bindingFlags"/>, one per binding, such as partially bound.</summary>
    private DescriptorSetLayout CreateLayout(
        Span<DescriptorSetLayoutBinding> bindings,
        string name,
        ReadOnlySpan<DescriptorBindingFlags> bindingFlags
    )
    {
        fixed (DescriptorBindingFlags* p = bindingFlags)
        {
            var info = new DescriptorSetLayoutBindingFlagsCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
                BindingCount = (uint)bindingFlags.Length,
                PBindingFlags = p,
            };
            return CreateLayout(bindings: bindings, name: name, next: &info);
        }
    }

    private DescriptorSetLayout CreateLayout(
        Span<DescriptorSetLayoutBinding> bindings,
        string name,
        void* next = null,
        DescriptorSetLayoutCreateFlags flags = 0
    )
    {
        fixed (DescriptorSetLayoutBinding* p = bindings)
        {
            var info = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                PNext = next,
                Flags = flags,
                BindingCount = (uint)bindings.Length,
                PBindings = p,
            };
            device
                .Vk.CreateDescriptorSetLayout(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pSetLayout: out DescriptorSetLayout layout
                )
                .Check("vkCreateDescriptorSetLayout");
            device.SetName(ObjectType.DescriptorSetLayout, layout.Handle, name);
            var storage = default(StorageDescriptors);
            foreach (DescriptorSetLayoutBinding binding in bindings)
                storage = storage.With(binding);
            setStorage[layout.Handle] = storage;
            return layout;
        }
    }

    /// <param name="push">The one push constant range, or null for a layout without push constants.</param>
    private PipelineLayout CreatePipelineLayout(
        ReadOnlySpan<DescriptorSetLayout> sets,
        PushConstantRange? push,
        string name
    )
    {
        PipelineLayout layout;
        PushConstantRange range = push ?? default;
        fixed (DescriptorSetLayout* setLayouts = sets)
        {
            var info = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = (uint)sets.Length,
                PSetLayouts = setLayouts,
                PushConstantRangeCount = push is null ? 0u : 1u,
                PPushConstantRanges = &range,
            };
            device
                .Vk.CreatePipelineLayout(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pPipelineLayout: out layout
                )
                .Check("vkCreatePipelineLayout");
        }
        device.SetName(ObjectType.PipelineLayout, layout.Handle, name);
        var storage = default(StorageDescriptors);
        foreach (DescriptorSetLayout set in sets)
            storage = storage.Plus(setStorage[set.Handle]);
        layoutStorage[layout.Handle] = storage;
        return layout;
    }

    private DescriptorPool CreatePool(
        Span<DescriptorPoolSize> sizes,
        uint maxSets,
        DescriptorPoolCreateFlags flags,
        string name
    )
    {
        fixed (DescriptorPoolSize* p = sizes)
        {
            var info = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                Flags = flags,
                MaxSets = maxSets,
                PoolSizeCount = (uint)sizes.Length,
                PPoolSizes = p,
            };
            device
                .Vk.CreateDescriptorPool(
                    device: device.Device,
                    pCreateInfo: &info,
                    pAllocator: null,
                    pDescriptorPool: out DescriptorPool created
                )
                .Check("vkCreateDescriptorPool");
            device.SetName(ObjectType.DescriptorPool, created.Handle, name);
            return created;
        }
    }

    private DescriptorSet Allocate(DescriptorPool from, DescriptorSetLayout layout, void* next)
    {
        var info = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            PNext = next,
            DescriptorPool = from,
            DescriptorSetCount = 1,
            PSetLayouts = &layout,
        };
        device.Vk.AllocateDescriptorSets(device.Device, &info, out DescriptorSet set).Check("vkAllocateDescriptorSets");
        return set;
    }

    public void Dispose()
    {
        Vk vk = device.Vk;
        // Destroying a pool frees its sets.
        vk.DestroyDescriptorPool(device.Device, pool, null);
        vk.DestroyDescriptorPool(device.Device, texturePool, null);
        vk.DestroyPipelineLayout(device.Device, SceneLayout, null);
        vk.DestroyPipelineLayout(device.Device, ImpostorLayout, null);
        vk.DestroyPipelineLayout(device.Device, PostLayout, null);
        vk.DestroyPipelineLayout(device.Device, OverlayLayout, null);
        vk.DestroyPipelineLayout(device.Device, BloomLayout, null);
        vk.DestroyPipelineLayout(device.Device, ExposureLayout, null);
        vk.DestroyPipelineLayout(device.Device, EnvironmentPrefilterLayout, null);
        vk.DestroyPipelineLayout(device.Device, OcclusionLayout, null);
        vk.DestroyPipelineLayout(device.Device, SmaaLayout, null);
        vk.DestroyPipelineLayout(device.Device, PyramidLayout, null);
        vk.DestroyPipelineLayout(device.Device, DepthTailLayout, null);
        vk.DestroyPipelineLayout(device.Device, ReflectionLayout, null);
        vk.DestroyPipelineLayout(device.Device, DeformLayout, null);
        vk.DestroyPipelineLayout(device.Device, ScatterLayout, null);
        vk.DestroyDescriptorSetLayout(device.Device, FrameSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, TextureSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, GBufferSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, PostSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, GlowSourceSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, AtlasSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, MeterSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, ExposureSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, EnvironmentPrefilterSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, EnvironmentLevelsSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, OcclusionSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, SmaaSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, PyramidLevelSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, DepthTailSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, ReflectionSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, DeformSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, ScatterSet, null);
        vk.DestroySampler(device.Device, ShadowSampler, null);
        vk.DestroySampler(device.Device, AtlasSampler, null);
        vk.DestroySampler(device.Device, LinearClampSampler, null);
        vk.DestroySampler(device.Device, EnvironmentSampler, null);
        vk.DestroySampler(device.Device, PointSampler, null);
    }
}

/// <summary>The storage image and storage buffer descriptors of a set layout or a pipeline layout.</summary>
internal readonly record struct StorageDescriptors(uint Images, uint Buffers)
{
    /// <summary>These descriptors and those of <paramref name="binding"/>.</summary>
    public StorageDescriptors With(in DescriptorSetLayoutBinding binding) =>
        binding.DescriptorType switch
        {
            DescriptorType.StorageImage => this with { Images = Images + binding.DescriptorCount },
            DescriptorType.StorageBuffer => this with { Buffers = Buffers + binding.DescriptorCount },
            _ => this,
        };

    /// <summary>These descriptors and <paramref name="other"/>.</summary>
    public StorageDescriptors Plus(StorageDescriptors other) => new(Images + other.Images, Buffers + other.Buffers);
}

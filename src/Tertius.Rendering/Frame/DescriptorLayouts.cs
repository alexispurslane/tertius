using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The descriptor set layouts, pipeline layouts and pools shared by all passes.
/// <list type="table">
/// <item><term>set 0, frame</term><description>frame constants, instances, materials, lights, occluders, material sampler,
/// frame outlines, puffs, the BRDF tables (bindings 8 and 9) with their sampler (binding 10, immutable), and the exposure
/// buffer (binding 11), which every frame's set shares. One per frame in flight.</description></item>
/// <item><term>set 1, textures</term>
/// <description>the bindless texture array: variable count, partially bound, update-after-bind. One per frame in flight.</description>
/// </item>
/// <item><term>set 2, G-buffer</term>
/// <description>the G-buffer targets for the lighting and transparent passes (rewritten on resize),
/// the sun shadow cascades (binding 5) and their comparison sampler (binding 6, immutable), which the vertex stage also reads
/// for the lit puffs. One.</description></item>
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
/// <item><term>SMAA</term><description>the inputs of the three SMAA passes, each with its immutable sampler. One when SMAA
/// is on, its screen-sized images rewritten on resize.</description></item>
/// </list>
/// Every pass that draws meshes or reads the G-buffer uses <see cref="SceneLayout"/>, so sets 0 and 1 stay bound
/// across pipeline changes. <see cref="ImpostorLayout"/> adds set 3 and is compatible with it for sets 0 to 2.
/// </summary>
internal sealed unsafe class DescriptorLayouts : IDisposable
{
    /// <summary>The bindings of <see cref="SmaaSet"/>.</summary>
    private const uint SmaaBindings = 6;

    private readonly VulkanDevice device;
    private readonly DescriptorPool pool;
    private readonly DescriptorPool texturePool;

    /// <param name="glowLevels">The bloom levels, 0 without bloom: the pool holds one glow source set more than that.</param>
    public DescriptorLayouts(VulkanDevice device, int framesInFlight, int glowLevels)
    {
        this.device = device;
        LinearClampSampler = CreateLevelZeroSampler(Filter.Linear, "linear clamp sampler");
        PointSampler = CreateLevelZeroSampler(Filter.Nearest, "point sampler");
        FrameSet = CreateFrameSetLayout(LinearClampSampler);
        TextureSet = CreateTextureSetLayout();
        ShadowSampler = CreateShadowSampler();
        GBufferSet = CreateGBufferSetLayout(ShadowSampler);
        PostSet = CreateImagesLayout(images: 2, sampler: LinearClampSampler, name: "post set");
        GlowSourceSet = CreateImagesLayout(images: 1, sampler: LinearClampSampler, name: "glow source set");
        AtlasSampler = CreateAtlasSampler();
        AtlasSet = CreateImagesLayout(images: 2, sampler: AtlasSampler, name: "impostor atlas set");
        (MeterSet, ExposureSet) = CreateExposureSetLayouts();
        SmaaSet = CreateSmaaSetLayout();

        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        var drawPush = new PushConstantRange(graphics, 0, (uint)Unsafe.SizeOf<DrawPushConstants>());
        DescriptorSetLayout* sceneSets = stackalloc[] { FrameSet, TextureSet, GBufferSet, AtlasSet };
        SceneLayout = CreatePipelineLayout(sets: sceneSets, count: 3, push: drawPush, name: "scene layout");
        ImpostorLayout = CreatePipelineLayout(sets: sceneSets, count: 4, push: drawPush, name: "impostor layout");
        OverlayLayout = CreatePipelineLayout(
            sets: sceneSets,
            count: 3,
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<OutlinePushConstants>()),
            name: "overlay layout"
        );
        DescriptorSetLayout* postSets = stackalloc[] { PostSet };
        PostLayout = CreatePipelineLayout(
            sets: postSets,
            count: 1,
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<ToneMapPushConstants>()),
            name: "post layout"
        );
        DescriptorSetLayout* glowSets = stackalloc[] { GlowSourceSet };
        BloomLayout = CreatePipelineLayout(
            sets: glowSets,
            count: 1,
            push: new(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<BloomPushConstants>()),
            name: "bloom layout"
        );
        DescriptorSetLayout* exposureSets = stackalloc[] { MeterSet, ExposureSet };
        ExposureLayout = CreatePipelineLayout(
            sets: exposureSets,
            count: 2,
            push: new(ShaderStageFlags.ComputeBit, 0, (uint)Unsafe.SizeOf<ExposureAdaptPushConstants>()),
            name: "exposure layout"
        );
        DescriptorSetLayout* smaaSets = stackalloc[] { SmaaSet };
        SmaaLayout = CreatePipelineLayout(
            sets: smaaSets,
            count: 1,
            push: new(graphics, 0, (uint)Unsafe.SizeOf<SmaaPushConstants>()),
            name: "smaa layout"
        );

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
    /// The shadow map sampler: hardware PCF (linear filtering of GREATER_OR_EQUAL comparisons, so a receiver at least
    /// as near the sun as the stored caster is lit), clamped to the edge.
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

    /// <summary>Trilinear filtering, clamped to the edge, for the impostor atlas.</summary>
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

    public DescriptorSet AllocateFrameSet() => Allocate(pool, FrameSet, null);

    public DescriptorSet AllocateGBufferSet() => Allocate(pool, GBufferSet, null);

    public DescriptorSet AllocatePostSet() => Allocate(pool, PostSet, null);

    public DescriptorSet AllocateGlowSourceSet() => Allocate(pool, GlowSourceSet, null);

    public DescriptorSet AllocateAtlasSet() => Allocate(pool, AtlasSet, null);

    public DescriptorSet AllocateMeterSet() => Allocate(pool, MeterSet, null);

    public DescriptorSet AllocateExposureSet() => Allocate(pool, ExposureSet, null);

    public DescriptorSet AllocateSmaaSet() => Allocate(pool, SmaaSet, null);

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

    public void WriteBuffer(DescriptorSet set, uint binding, DescriptorType type, GpuBuffer buffer)
    {
        var info = new DescriptorBufferInfo(buffer.Handle, 0, Vk.WholeSize);
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
    public void WriteImage(DescriptorSet set, uint binding, uint arrayElement, GpuImage image)
    {
        var info = new DescriptorImageInfo(default, image.View, ImageLayout.ShaderReadOnlyOptimal);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = arrayElement,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.SampledImage,
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

    /// <summary>Writes the view of a combined image sampler whose sampler is immutable, read in <see cref="ImageLayout.ShaderReadOnlyOptimal"/>.</summary>
    public void WriteSampledView(DescriptorSet set, uint binding, ImageView view)
    {
        var info = new DescriptorImageInfo(default, view, ImageLayout.ShaderReadOnlyOptimal);
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
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
    /// The layout of the G-buffer set: five G-buffer images, the shadow cascades, and <paramref name="shadowSampler"/>
    /// as an immutable sampler. It is immutable because portability implementations may not allow updating comparison
    /// sampler descriptors (VkPhysicalDevicePortabilitySubsetFeaturesKHR::mutableComparisonSamplers).
    /// </summary>
    private DescriptorSetLayout CreateGBufferSetLayout(Sampler shadowSampler)
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        Span<DescriptorSetLayoutBinding> gbuffer =
        [
            Image(binding: 0, stages: ShaderStageFlags.FragmentBit),
            Image(binding: 1, stages: ShaderStageFlags.FragmentBit),
            Image(binding: 2, stages: ShaderStageFlags.FragmentBit),
            Image(binding: 3, stages: ShaderStageFlags.FragmentBit),
            Image(binding: 4, stages: ShaderStageFlags.FragmentBit),
            // The vertex shader of the lit puffs samples the cascades once per puff, at its center.
            Image(binding: 5, stages: graphics),
            ImmutableSampler(binding: 6, stages: graphics, sampler: &shadowSampler),
        ];
        return CreateLayout(bindings: gbuffer, name: "g-buffer set");
    }

    /// <summary>The shared pool, which holds <paramref name="glowSourceSets"/> glow source sets, and the pool of the texture sets.</summary>
    private (DescriptorPool Shared, DescriptorPool Textures) CreatePools(uint framesInFlight, uint glowSourceSets)
    {
        uint frames = framesInFlight;
        Span<DescriptorPoolSize> sizes =
        [
            new(DescriptorType.UniformBuffer, frames),
            // Seven per frame set, and the two of the exposure set.
            new(DescriptorType.StorageBuffer, 7 * frames + 2),
            // Two per frame set (the material sampler and the immutable linear clamp sampler), the immutable samplers
            // of the G-buffer, atlas and post sets, and one per glow source set.
            new(DescriptorType.Sampler, 2 * frames + 3 + glowSourceSets),
            // 17 for the G-buffer, post and atlas sets, two per frame set for the tables, one per glow source set, and
            // two for the meter set.
            new(DescriptorType.SampledImage, 17 + 2 * frames + glowSourceSets + 2),
            // The SMAA set.
            new(DescriptorType.CombinedImageSampler, SmaaBindings),
        ];
        // The frame sets, the G-buffer, post, atlas, meter, exposure and SMAA sets, and the glow source sets.
        DescriptorPool shared = CreatePool(
            sizes: sizes,
            maxSets: frames + 6 + glowSourceSets,
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

    /// <summary>The layout of set 0, whose binding 10 holds <paramref name="linearClamp"/> as its immutable sampler.</summary>
    private DescriptorSetLayout CreateFrameSetLayout(Sampler linearClamp)
    {
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        Span<DescriptorSetLayoutBinding> frame =
        [
            new(binding: 0, descriptorType: DescriptorType.UniformBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 1, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 2, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 3, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 4, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 5, descriptorType: DescriptorType.Sampler, descriptorCount: 1, stageFlags: graphics),
            new(binding: 6, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            new(binding: 7, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
            // The tables sit in the frame set because the impostor capture binds only sets 0 and 1.
            new(
                binding: 8,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 9,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 10,
                descriptorType: DescriptorType.Sampler,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit,
                pImmutableSamplers: &linearClamp
            ),
            new(binding: 11, descriptorType: DescriptorType.StorageBuffer, descriptorCount: 1, stageFlags: graphics),
        ];
        return CreateLayout(bindings: frame, name: "frame set");
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

    private Sampler CreateAtlasSampler()
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
        device.SetName(ObjectType.Sampler, sampler.Handle, "impostor atlas sampler");
        return sampler;
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
            return layout;
        }
    }

    private PipelineLayout CreatePipelineLayout(
        DescriptorSetLayout* sets,
        uint count,
        PushConstantRange push,
        string name
    )
    {
        var info = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = count,
            PSetLayouts = sets,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };
        device
            .Vk.CreatePipelineLayout(
                device: device.Device,
                pCreateInfo: &info,
                pAllocator: null,
                pPipelineLayout: out PipelineLayout layout
            )
            .Check("vkCreatePipelineLayout");
        device.SetName(ObjectType.PipelineLayout, layout.Handle, name);
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
        vk.DestroyPipelineLayout(device.Device, SmaaLayout, null);
        vk.DestroyDescriptorSetLayout(device.Device, FrameSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, TextureSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, GBufferSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, PostSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, GlowSourceSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, AtlasSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, MeterSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, ExposureSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, SmaaSet, null);
        vk.DestroySampler(device.Device, ShadowSampler, null);
        vk.DestroySampler(device.Device, AtlasSampler, null);
        vk.DestroySampler(device.Device, LinearClampSampler, null);
        vk.DestroySampler(device.Device, PointSampler, null);
    }
}

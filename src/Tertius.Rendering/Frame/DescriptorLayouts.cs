using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The descriptor set layouts, pipeline layouts and pools shared by all passes.
/// <list type="table">
/// <item><term>set 0, frame</term><description>frame constants, instances, materials, lights, occluders, material sampler,
/// frame outlines, puffs, and the BRDF tables (bindings 8 and 9) with their sampler (binding 10, immutable). One per frame
/// in flight.</description></item>
/// <item><term>set 1, textures</term>
/// <description>the bindless texture array: variable count, partially bound, update-after-bind. One per frame in flight.</description>
/// </item>
/// <item><term>set 2, G-buffer</term>
/// <description>the G-buffer targets for the lighting and transparent passes (rewritten on resize),
/// the sun shadow cascades (binding 5) and their comparison sampler (binding 6, immutable), which the vertex stage also reads
/// for the lit puffs. One.</description></item>
/// <item><term>set 3, impostor atlas</term><description>the impostor atlas's color and depth-offset arrays and their
/// sampler (immutable), for the billboards in the G-buffer pass. One, written once.</description></item>
/// <item><term>post</term><description>the HDR image for tone mapping. One, rewritten on resize.</description></item>
/// </list>
/// Every pass that draws meshes or reads the G-buffer uses <see cref="SceneLayout"/>, so sets 0 and 1 stay bound
/// across pipeline changes. <see cref="ImpostorLayout"/> adds set 3 and is compatible with it for sets 0 to 2.
/// </summary>
internal sealed unsafe class DescriptorLayouts : IDisposable
{
    private readonly VulkanDevice device;
    private readonly DescriptorPool pool;
    private readonly DescriptorPool texturePool;

    public DescriptorLayouts(VulkanDevice device, int framesInFlight)
    {
        this.device = device;
        const ShaderStageFlags graphics = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;

        TableSampler = CreateTableSampler();
        FrameSet = CreateFrameSetLayout(TableSampler);

        // Textures live in their own set because only update-after-bind layouts may hold more than
        // maxPerStageDescriptorSampledImages (256 on MoltenVK) descriptors.
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
        TextureSet = CreateLayout(
            bindings: textures,
            name: "texture set",
            next: &bindingFlags,
            flags: DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit
        );

        ShadowSampler = CreateShadowSampler();
        // The comparison sampler is immutable: portability implementations may not allow updating comparison
        // sampler descriptors (VkPhysicalDevicePortabilitySubsetFeaturesKHR::mutableComparisonSamplers).
        Sampler shadowSampler = ShadowSampler;
        Span<DescriptorSetLayoutBinding> gbuffer =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 1,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 2,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 3,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 4,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            // The vertex shader of the lit puffs samples the cascades once per puff, at its center.
            new(binding: 5, descriptorType: DescriptorType.SampledImage, descriptorCount: 1, stageFlags: graphics),
            new(
                binding: 6,
                descriptorType: DescriptorType.Sampler,
                descriptorCount: 1,
                stageFlags: graphics,
                pImmutableSamplers: &shadowSampler
            ),
        ];
        GBufferSet = CreateLayout(bindings: gbuffer, name: "g-buffer set");

        Span<DescriptorSetLayoutBinding> post =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
        ];
        PostSet = CreateLayout(bindings: post, name: "post set");

        AtlasSampler = CreateAtlasSampler();
        Sampler atlasSampler = AtlasSampler;
        Span<DescriptorSetLayoutBinding> atlas =
        [
            new(
                binding: 0,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 1,
                descriptorType: DescriptorType.SampledImage,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit
            ),
            new(
                binding: 2,
                descriptorType: DescriptorType.Sampler,
                descriptorCount: 1,
                stageFlags: ShaderStageFlags.FragmentBit,
                pImmutableSamplers: &atlasSampler
            ),
        ];
        AtlasSet = CreateLayout(bindings: atlas, name: "impostor atlas set");

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

        uint frames = (uint)framesInFlight;
        Span<DescriptorPoolSize> sizes =
        [
            new(DescriptorType.UniformBuffer, frames),
            new(DescriptorType.StorageBuffer, 6 * frames),
            // Two per frame set (the material sampler and the immutable table sampler), and the immutable samplers of
            // the G-buffer and atlas sets.
            new(DescriptorType.Sampler, 2 * frames + 2),
            new(DescriptorType.SampledImage, 16 + 2 * frames),
        ];
        pool = CreatePool(sizes: sizes, maxSets: frames + 3, flags: 0, name: "descriptor pool");
        Span<DescriptorPoolSize> textureSizes = [new(DescriptorType.SampledImage, frames * TextureStore.Capacity)];
        texturePool = CreatePool(
            sizes: textureSizes,
            maxSets: frames,
            flags: DescriptorPoolCreateFlags.UpdateAfterBindBit,
            name: "texture descriptor pool"
        );
    }

    public DescriptorSetLayout FrameSet { get; }
    public DescriptorSetLayout TextureSet { get; }
    public DescriptorSetLayout GBufferSet { get; }
    public DescriptorSetLayout PostSet { get; }

    /// <summary>
    /// The shadow map sampler: hardware PCF (linear filtering of GREATER_OR_EQUAL comparisons, so a receiver at least
    /// as near the sun as the stored caster is lit), clamped to the edge.
    /// </summary>
    public Sampler ShadowSampler { get; }

    /// <summary>Linear filtering, clamped to the edge, one level and no anisotropy, for the BRDF tables.</summary>
    public Sampler TableSampler { get; }

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

    public DescriptorSet AllocateFrameSet() => Allocate(pool, FrameSet, null);

    public DescriptorSet AllocateGBufferSet() => Allocate(pool, GBufferSet, null);

    public DescriptorSet AllocatePostSet() => Allocate(pool, PostSet, null);

    public DescriptorSet AllocateAtlasSet() => Allocate(pool, AtlasSet, null);

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

    /// <summary>The layout of set 0, whose binding 10 holds <paramref name="tableSampler"/> as its immutable sampler.</summary>
    private DescriptorSetLayout CreateFrameSetLayout(Sampler tableSampler)
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
                pImmutableSamplers: &tableSampler
            ),
        ];
        return CreateLayout(bindings: frame, name: "frame set");
    }

    private Sampler CreateTableSampler()
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
        device.SetName(ObjectType.Sampler, sampler.Handle, "brdf table sampler");
        return sampler;
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
        vk.DestroyDescriptorSetLayout(device.Device, FrameSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, TextureSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, GBufferSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, PostSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, AtlasSet, null);
        vk.DestroySampler(device.Device, ShadowSampler, null);
        vk.DestroySampler(device.Device, AtlasSampler, null);
        vk.DestroySampler(device.Device, TableSampler, null);
    }
}

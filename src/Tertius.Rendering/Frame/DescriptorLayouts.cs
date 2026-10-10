using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// The descriptor set layouts, pipeline layouts and pools shared by all passes.
/// <list type="table">
/// <item><term>set 0, frame</term><description>frame constants, instances, materials, lights, occluders, material sampler. One per frame in flight.</description></item>
/// <item><term>set 1, textures</term><description>the bindless texture array: variable count, partially bound, update-after-bind. One per frame in flight.</description></item>
/// <item><term>set 2, G-buffer</term><description>the G-buffer targets for the lighting and transparent passes (rewritten on resize),
/// the sun shadow cascades (binding 5) and their comparison sampler (binding 6, immutable). One.</description></item>
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

        Span<DescriptorSetLayoutBinding> frame =
        [
            new(0, DescriptorType.UniformBuffer, 1, graphics),
            new(1, DescriptorType.StorageBuffer, 1, graphics),
            new(2, DescriptorType.StorageBuffer, 1, graphics),
            new(3, DescriptorType.StorageBuffer, 1, graphics),
            new(4, DescriptorType.StorageBuffer, 1, graphics),
            new(5, DescriptorType.Sampler, 1, graphics),
        ];
        FrameSet = CreateLayout(frame, "frame set");

        // Textures live in their own set because only update-after-bind layouts may hold more than
        // maxPerStageDescriptorSampledImages (256 on MoltenVK) descriptors.
        Span<DescriptorSetLayoutBinding> textures = [new(0, DescriptorType.SampledImage, (uint)TextureStore.Capacity, ShaderStageFlags.FragmentBit)];
        DescriptorBindingFlags textureFlags = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.VariableDescriptorCountBit |
                                              DescriptorBindingFlags.UpdateAfterBindBit;
        var bindingFlags = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &textureFlags,
        };
        TextureSet = CreateLayout(textures, "texture set", &bindingFlags, DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit);

        ShadowSampler = CreateShadowSampler();
        // The comparison sampler is immutable: portability implementations may not allow updating comparison
        // sampler descriptors (VkPhysicalDevicePortabilitySubsetFeaturesKHR::mutableComparisonSamplers).
        Sampler shadowSampler = ShadowSampler;
        Span<DescriptorSetLayoutBinding> gbuffer =
        [
            new(0, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(1, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(2, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(3, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(4, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(5, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(6, DescriptorType.Sampler, 1, ShaderStageFlags.FragmentBit, &shadowSampler),
        ];
        GBufferSet = CreateLayout(gbuffer, "g-buffer set");

        Span<DescriptorSetLayoutBinding> post = [new(0, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit)];
        PostSet = CreateLayout(post, "post set");

        AtlasSampler = CreateAtlasSampler();
        Sampler atlasSampler = AtlasSampler;
        Span<DescriptorSetLayoutBinding> atlas =
        [
            new(0, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(1, DescriptorType.SampledImage, 1, ShaderStageFlags.FragmentBit),
            new(2, DescriptorType.Sampler, 1, ShaderStageFlags.FragmentBit, &atlasSampler),
        ];
        AtlasSet = CreateLayout(atlas, "impostor atlas set");

        var drawPush = new PushConstantRange(graphics, 0, (uint)Unsafe.SizeOf<DrawPushConstants>());
        DescriptorSetLayout* sceneSets = stackalloc[] { FrameSet, TextureSet, GBufferSet, AtlasSet };
        SceneLayout = CreatePipelineLayout(sceneSets, 3, drawPush, "scene layout");
        ImpostorLayout = CreatePipelineLayout(sceneSets, 4, drawPush, "impostor layout");
        DescriptorSetLayout* postSets = stackalloc[] { PostSet };
        PostLayout = CreatePipelineLayout(postSets, 1, new PushConstantRange(ShaderStageFlags.FragmentBit, 0, (uint)Unsafe.SizeOf<ToneMapPushConstants>()), "post layout");

        uint frames = (uint)framesInFlight;
        Span<DescriptorPoolSize> sizes =
        [
            new(DescriptorType.UniformBuffer, frames),
            new(DescriptorType.StorageBuffer, 4 * frames),
            // One per frame set, and the immutable samplers of the G-buffer and atlas sets.
            new(DescriptorType.Sampler, frames + 2),
            new(DescriptorType.SampledImage, 16),
        ];
        pool = CreatePool(sizes, frames + 3, 0, "descriptor pool");
        Span<DescriptorPoolSize> textureSizes = [new(DescriptorType.SampledImage, frames * (uint)TextureStore.Capacity)];
        texturePool = CreatePool(textureSizes, frames, DescriptorPoolCreateFlags.UpdateAfterBindBit, "texture descriptor pool");
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
        device.Vk.UpdateDescriptorSets(device.Device, 1, &write, 0, null);
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
        device.Vk.UpdateDescriptorSets(device.Device, 1, &write, 0, null);
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
        device.Vk.UpdateDescriptorSets(device.Device, 1, &write, 0, null);
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
        device.Vk.CreateSampler(device.Device, &info, null, out Sampler sampler).Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "shadow comparison sampler");
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
        device.Vk.CreateSampler(device.Device, &info, null, out Sampler sampler).Check("vkCreateSampler");
        device.SetName(ObjectType.Sampler, sampler.Handle, "impostor atlas sampler");
        return sampler;
    }

    private DescriptorSetLayout CreateLayout(Span<DescriptorSetLayoutBinding> bindings, string name, void* next = null,
        DescriptorSetLayoutCreateFlags flags = 0)
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
            device.Vk.CreateDescriptorSetLayout(device.Device, &info, null, out DescriptorSetLayout layout).Check("vkCreateDescriptorSetLayout");
            device.SetName(ObjectType.DescriptorSetLayout, layout.Handle, name);
            return layout;
        }
    }

    private PipelineLayout CreatePipelineLayout(DescriptorSetLayout* sets, uint count, PushConstantRange push, string name)
    {
        var info = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = count,
            PSetLayouts = sets,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &push,
        };
        device.Vk.CreatePipelineLayout(device.Device, &info, null, out PipelineLayout layout).Check("vkCreatePipelineLayout");
        device.SetName(ObjectType.PipelineLayout, layout.Handle, name);
        return layout;
    }

    private DescriptorPool CreatePool(Span<DescriptorPoolSize> sizes, uint maxSets, DescriptorPoolCreateFlags flags, string name)
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
            device.Vk.CreateDescriptorPool(device.Device, &info, null, out DescriptorPool created).Check("vkCreateDescriptorPool");
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
        vk.DestroyDescriptorSetLayout(device.Device, FrameSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, TextureSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, GBufferSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, PostSet, null);
        vk.DestroyDescriptorSetLayout(device.Device, AtlasSet, null);
        vk.DestroySampler(device.Device, ShadowSampler, null);
        vk.DestroySampler(device.Device, AtlasSampler, null);
    }
}

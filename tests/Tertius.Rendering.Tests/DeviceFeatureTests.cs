using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The device features that the renderer requires (ARCHITECTURE.md, "Required device features"): a device that reports
/// every one passes, and a device without one is refused by its name. No GPU needed.
/// </summary>
public sealed class DeviceFeatureTests
{
    private static readonly PhysicalDeviceFeatures Core = new()
    {
        MultiDrawIndirect = true,
        DrawIndirectFirstInstance = true,
        DepthClamp = true,
        DualSrcBlend = true,
        IndependentBlend = true,
        SamplerAnisotropy = true,
        ShaderStorageImageExtendedFormats = true,
        ImageCubeArray = true,
    };

    private static readonly PhysicalDeviceVulkan12Features Vulkan12 = new()
    {
        SType = StructureType.PhysicalDeviceVulkan12Features,
        DescriptorIndexing = true,
        RuntimeDescriptorArray = true,
        DescriptorBindingPartiallyBound = true,
        DescriptorBindingVariableDescriptorCount = true,
        DescriptorBindingSampledImageUpdateAfterBind = true,
        ShaderSampledImageArrayNonUniformIndexing = true,
    };

    private static readonly PhysicalDeviceVulkan13Features Vulkan13 = new()
    {
        SType = StructureType.PhysicalDeviceVulkan13Features,
        DynamicRendering = true,
        Synchronization2 = true,
        ShaderDemoteToHelperInvocation = true,
    };

    private const ShaderStageFlags SubgroupStages = ShaderStageFlags.FragmentBit | ShaderStageFlags.ComputeBit;

    private const SubgroupFeatureFlags SubgroupOperations =
        SubgroupFeatureFlags.BasicBit | SubgroupFeatureFlags.BallotBit;

    [Fact]
    public void ADeviceWithEveryRequiredFeatureIsTaken() =>
        VulkanDevice.MissingFeature(Core, Vulkan12, Vulkan13).Should().BeNull();

    [Fact]
    public void ADeviceWithoutDescriptorIndexingIsRefusedByItsName()
    {
        // The five parts of descriptor indexing are not enough: only the feature itself guarantees the limits of 500,000
        // descriptors after bind, which the bindless array of 4,096 textures needs, and the device enables it.
        PhysicalDeviceVulkan12Features withoutIt = Vulkan12 with
        {
            DescriptorIndexing = false,
        };

        VulkanDevice.MissingFeature(Core, withoutIt, Vulkan13).Should().Be("descriptorIndexing");
    }

    [Fact]
    public void TheDeviceRequiresIndependentBlend()
    {
        // The ambient pass adds its share of the skin's diffuse light to the scatter image and writes the HDR image with
        // no blend: two blend states in one pipeline (docs/plans/skin-and-hair.md, decision 14).
        PhysicalDeviceFeatures withoutIt = Core with
        {
            IndependentBlend = false,
        };

        VulkanDevice.MissingFeature(withoutIt, Vulkan12, Vulkan13).Should().Be("independentBlend");
    }

    [Fact]
    public void ADeviceWithoutCubeArraysIsRefusedByItsName()
    {
        // The environment maps of the chain's frames are one cube array (docs/plans/environment-maps-per-frame.md, decision 18).
        PhysicalDeviceFeatures withoutIt = Core with
        {
            ImageCubeArray = false,
        };

        VulkanDevice.MissingFeature(withoutIt, Vulkan12, Vulkan13).Should().Be("imageCubeArray");
    }

    [Fact]
    public void ADeviceWithoutExtendedStorageImageFormatsIsRefusedByItsName()
    {
        // The occlusion passes write R8 and R16F storage images, which only this feature guarantees.
        PhysicalDeviceFeatures withoutIt = Core with
        {
            ShaderStorageImageExtendedFormats = false,
        };

        VulkanDevice.MissingFeature(withoutIt, Vulkan12, Vulkan13).Should().Be("shaderStorageImageExtendedFormats");
    }

    [Fact]
    public void ADeviceWithSubgroupBallotInFragmentAndComputeShadersIsTaken() =>
        VulkanDevice.MissingSubgroupSupport(SubgroupStages, SubgroupOperations).Should().BeNull();

    [Fact]
    public void ADeviceWithoutSubgroupBallotIsRefusedByItsName()
    {
        // The map's ambient light reads its coefficients at a map that subgroupBroadcastFirst makes uniform over the
        // subgroup (environment.glsl).
        const SubgroupFeatureFlags withoutIt = SubgroupFeatureFlags.BasicBit | SubgroupFeatureFlags.VoteBit;

        VulkanDevice
            .MissingSubgroupSupport(SubgroupStages, withoutIt)
            .Should()
            .Be("subgroupSupportedOperations BALLOT");
    }

    [Fact]
    public void ADeviceWithoutSubgroupOperationsInFragmentShadersIsRefusedByItsName() =>
        // The lighting and transparent passes read the map's ambient light in fragment shaders.
        VulkanDevice
            .MissingSubgroupSupport(ShaderStageFlags.ComputeBit, SubgroupOperations)
            .Should()
            .Be("subgroupSupportedStages FRAGMENT");

    [Fact]
    public void ADeviceWithoutSubgroupOperationsInComputeShadersIsRefusedByItsName() =>
        // The reflections' trace and upsample read the map's ambient light in compute shaders.
        VulkanDevice
            .MissingSubgroupSupport(ShaderStageFlags.FragmentBit, SubgroupOperations)
            .Should()
            .Be("subgroupSupportedStages COMPUTE");
}

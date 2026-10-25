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
        SamplerAnisotropy = true,
        ShaderStorageImageExtendedFormats = true,
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
}

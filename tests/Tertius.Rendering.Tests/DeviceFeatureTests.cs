using System.Reflection;
using Silk.NET.Core;
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
        FragmentStoresAndAtomics = true,
        SamplerAnisotropy = true,
        ShaderStorageImageExtendedFormats = true,
        ShaderStorageImageWriteWithoutFormat = true,
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
    public void TheDeviceRequiresStoresInTheFragmentStage()
    {
        // Under the skin scatter, the ambient pass adds D's ambient part to the diffuse image with a storage write at skin
        // pixels alone (docs/plans/skin-and-hair.md, decision 14).
        PhysicalDeviceFeatures withoutIt = Core with
        {
            FragmentStoresAndAtomics = false,
        };

        VulkanDevice.MissingFeature(withoutIt, Vulkan12, Vulkan13).Should().Be("fragmentStoresAndAtomics");
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
    public void ADeviceWithoutStorageWritesWithoutAFormatIsRefusedByItsName()
    {
        // AMD's accumulate pass of FSR 3.1 declares its output image with no format (docs/plans/temporal-renderer.md,
        // "FSR 3.1"), which only this feature lets a shader write.
        PhysicalDeviceFeatures withoutIt = Core with
        {
            ShaderStorageImageWriteWithoutFormat = false,
        };

        VulkanDevice.MissingFeature(withoutIt, Vulkan12, Vulkan13).Should().Be("shaderStorageImageWriteWithoutFormat");
    }

    [Fact]
    public void ADeviceThatReportsJustTheFeaturesThatTheRendererEnablesIsTaken() =>
        // The renderer enables every feature that it requires, so the shaders may use each one.
        VulkanDevice
            .MissingFeature(
                VulkanDevice.EnabledCoreFeatures(),
                VulkanDevice.EnabledVulkan12Features(),
                VulkanDevice.EnabledVulkan13Features()
            )
            .Should()
            .BeNull();

    [Fact]
    public void ADeviceWithoutAFeatureThatTheRendererEnablesIsRefusedByItsName()
    {
        // vkCreateDevice fails on a device without a feature that it enables, with no name. So the renderer enables only
        // the features that it requires, as independentBlend left both lists when the ambient pass stopped blending
        // (ter-pjza).
        PhysicalDeviceFeatures core = VulkanDevice.EnabledCoreFeatures();
        PhysicalDeviceVulkan12Features vulkan12 = VulkanDevice.EnabledVulkan12Features();
        PhysicalDeviceVulkan13Features vulkan13 = VulkanDevice.EnabledVulkan13Features();

        string[] unnamed =
        [
            .. EnabledFeaturesNotNamed(core, without => VulkanDevice.MissingFeature(without, vulkan12, vulkan13)),
            .. EnabledFeaturesNotNamed(vulkan12, without => VulkanDevice.MissingFeature(core, without, vulkan13)),
            .. EnabledFeaturesNotNamed(vulkan13, without => VulkanDevice.MissingFeature(core, vulkan12, without)),
        ];

        unnamed.Should().BeEmpty("the refusal of a device without one of these features must name it");
    }

    /// <summary>
    /// The Vulkan names of the features that <paramref name="enabled"/> turns on, for each of which
    /// <paramref name="missing"/> of a copy of <paramref name="enabled"/> without that feature alone gives another name.
    /// </summary>
    private static IEnumerable<string> EnabledFeaturesNotNamed<T>(T enabled, Func<T, string?> missing)
        where T : struct
    {
        foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.FieldType != typeof(Bool32) || !(Bool32)field.GetValue(enabled)!)
                continue;
            object without = enabled;
            field.SetValue(without, new Bool32(false));
            string vulkanName = char.ToLowerInvariant(field.Name[0]) + field.Name[1..];
            if (missing((T)without) != vulkanName)
                yield return vulkanName;
        }
    }
}

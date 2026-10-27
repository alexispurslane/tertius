using Silk.NET.Vulkan;

namespace Tertius.Rendering;

/// <summary>
/// One pipeline of a pass that shades, in its two variants: one that culls back faces, for the one-sided materials, and
/// one that culls none, for the two-sided ones, hair (docs/plans/skin-and-hair.md, "Both sides of a hair card").
/// </summary>
internal readonly record struct SidedPipeline(Pipeline OneSided, Pipeline TwoSided)
{
    /// <summary>The variant that draws the materials of <paramref name="twoSided"/>.</summary>
    public Pipeline For(bool twoSided) => twoSided ? TwoSided : OneSided;

    /// <summary>
    /// Creates both variants of <paramref name="desc"/>, whose own cull mode the one-sided variant keeps. Destroys the
    /// first when the second fails.
    /// </summary>
    public static unsafe SidedPipeline Create(VulkanDevice device, ShaderLibrary shaders, GraphicsPipelineDesc desc)
    {
        Pipeline oneSided = desc.Create(device, shaders);
        try
        {
            Pipeline twoSided = (desc with { Name = desc.Name + " two-sided", CullMode = CullModeFlags.None }).Create(
                device,
                shaders
            );
            return new(OneSided: oneSided, TwoSided: twoSided);
        }
        catch
        {
            device.Vk.DestroyPipeline(device.Device, oneSided, null);
            throw;
        }
    }

    /// <summary>Destroys both variants.</summary>
    public unsafe void Destroy(VulkanDevice device)
    {
        device.Vk.DestroyPipeline(device.Device, OneSided, null);
        device.Vk.DestroyPipeline(device.Device, TwoSided, null);
    }
}

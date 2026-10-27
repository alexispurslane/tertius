using Silk.NET.Vulkan;

namespace Tertius.Rendering.Tests;

/// <summary>
/// No pipeline blends into a color attachment of an integer format, which Vulkan cannot blend, so that the validation
/// log may leave out MoltenVK's false report of it (ValidationLog.IsFalseIntegerBlendReport).
/// </summary>
public sealed class IntegerTargetBlendTests
{
    [Fact]
    public void APipelineThatBlendsIntoAnIntegerTargetIsRefusedBeforeItIsBuilt()
    {
        using VulkanDevice device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var shaders = new ShaderLibrary(device);
        foreach (
            (BlendMode blend, Format integerFormat) in new[]
            {
                (BlendMode.Alpha, Format.R8G8B8A8Uint),
                (BlendMode.Additive, Format.R32Sint),
            }
        )
        {
            // No such shaders exist: the refusal comes before the pipeline loads any.
            var desc = new GraphicsPipelineDesc
            {
                Name = "blends into an integer target",
                VertexShader = "none.vert",
                FragmentShader = "none.frag",
                Layout = default,
                ColorAttachments = [new(Format.R16G16B16A16Sfloat, blend), new(integerFormat, blend)],
            };

            Action create = () => desc.Create(device, shaders);

            create.Should().Throw<ArgumentException>().WithMessage($"*{integerFormat}*", $"{blend} cannot blend");
        }
    }
}

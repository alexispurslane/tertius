namespace Tertius.Rendering;

/// <summary>
/// The shading word of a material, the C# twin of the shaders' reads of <c>GpuMaterial.shading</c>
/// (docs/plans/skin-and-hair.md, "The material on the GPU"): bits 0 and 1 hold the model's code, and bits 2 and 3 a hair's
/// strand direction.
/// </summary>
internal static class ShadingCode
{
    public const uint Standard = 0,
        Skin = 1,
        Hair = 2;

    /// <summary>The word of <paramref name="model"/>.</summary>
    public static uint Word(ShadingModel model) =>
        model switch
        {
            StandardShading => Standard,
            SkinShading => Skin,
            HairShading(var strands) => Hair | ((uint)strands << 2),
        };
}

namespace Tertius;

/// <summary>The direction in a hair card's texture along which its strands run from root to tip.</summary>
public enum StrandDirection : byte
{
    PlusU,
    MinusU,
    PlusV,
    MinusV,
}

/// <summary>Lambert diffuse and GGX with glTF's metallic (https://github.com/alexispurslane/tertius/wiki/pbr-brdf).</summary>
/// <param name="Metallic">From 0, a non-metal, to 1, a metal. The metallic-roughness map's B channel multiplies it.</param>
public readonly record struct StandardShading(float Metallic);

/// <summary>
/// The standard light of a non-metal, whose diffuse light scatters under the skin (https://github.com/alexispurslane/tertius/wiki/skin-and-hair).
/// </summary>
public readonly record struct SkinShading;

/// <summary>Fibers along the strands of the card's texture (https://github.com/alexispurslane/tertius/wiki/skin-and-hair, "The hair model").</summary>
/// <param name="Strands">The texture axis along which the strands run from root to tip.</param>
public readonly record struct HairShading(StrandDirection Strands);

/// <summary>
/// How a surface sends the light that reaches it: <see cref="StandardShading"/>, <see cref="SkinShading"/> or
/// <see cref="HairShading"/> (https://github.com/alexispurslane/tertius/wiki/skin-and-hair, decision 6). The default value is a standard non-metal.
/// </summary>
/// <remarks>The union generator writes its union members (https://github.com/alexispurslane/tertius/wiki/union-tooling).</remarks>
[GeneratedUnion(typeof(StandardShading), typeof(SkinShading), typeof(HairShading), ZeroCase = typeof(StandardShading))]
public readonly partial struct ShadingModel;

namespace Tertius;

/// <summary>The direction in a hair card's texture along which its strands run from root to tip.</summary>
public enum StrandDirection : byte
{
    PlusU,
    MinusU,
    PlusV,
    MinusV,
}

/// <summary>Lambert diffuse and GGX with glTF's metallic (docs/plans/pbr-brdf.md).</summary>
/// <param name="Metallic">From 0, a non-metal, to 1, a metal. The metallic-roughness map's B channel multiplies it.</param>
public readonly record struct StandardShading(float Metallic);

/// <summary>
/// The standard light of a non-metal, whose diffuse light scatters under the skin (docs/plans/skin-and-hair.md).
/// </summary>
public readonly record struct SkinShading;

/// <summary>Fibers along the strands of the card's texture (docs/plans/skin-and-hair.md, "The hair model").</summary>
/// <param name="Strands">The texture axis along which the strands run from root to tip.</param>
public readonly record struct HairShading(StrandDirection Strands);

/// <summary>
/// How a surface sends the light that reaches it: <see cref="StandardShading"/>, <see cref="SkinShading"/> or
/// <see cref="HairShading"/> (docs/plans/skin-and-hair.md, decision 6). The default value is a standard non-metal.
/// </summary>
/// <remarks>The union generator writes its union members (docs/plans/union-tooling.md).</remarks>
[GeneratedUnion(typeof(StandardShading), typeof(SkinShading), typeof(HairShading), ZeroCase = typeof(StandardShading))]
public readonly partial struct ShadingModel;

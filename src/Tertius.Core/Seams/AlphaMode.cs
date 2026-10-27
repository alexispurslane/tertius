namespace Tertius;

/// <summary>The alpha is not drawn: every texel is covered.</summary>
public readonly record struct OpaqueAlpha;

/// <summary>glTF's MASK: a texel whose alpha is below the cutoff is not there.</summary>
/// <param name="Cutoff">The alpha at and above which a texel is covered, above 0 and at most 1.</param>
public readonly record struct CutoutAlpha(float Cutoff);

/// <summary>A cutout core, and the texels below the cutoff blended over what lies behind by their coverage.</summary>
/// <param name="Cutoff">The alpha at and above which a texel is core, above 0 and at most 1.</param>
public readonly record struct FringedAlpha(float Cutoff);

/// <summary>A pane whose alpha is its opacity, which reflects in full (ARCHITECTURE.md, "Transparent objects").</summary>
public readonly record struct GlassAlpha;

/// <summary>
/// How a material's alpha draws: <see cref="OpaqueAlpha"/>, <see cref="CutoutAlpha"/>, <see cref="FringedAlpha"/> or
/// <see cref="GlassAlpha"/> (docs/plans/skin-and-hair.md, decision 7). The default value is opaque.
/// </summary>
/// <remarks>The union generator writes its union members (docs/plans/union-tooling.md).</remarks>
[GeneratedUnion(
    typeof(OpaqueAlpha),
    typeof(CutoutAlpha),
    typeof(FringedAlpha),
    typeof(GlassAlpha),
    ZeroCase = typeof(OpaqueAlpha)
)]
public readonly partial struct AlphaMode
{
    /// <summary>The cutoff of a cutout or fringed alpha, or null for an opaque or glass one, which tests none.</summary>
    public float? Cutoff =>
        this switch
        {
            CutoutAlpha(var cutoff) => cutoff,
            FringedAlpha(var cutoff) => cutoff,
            OpaqueAlpha or GlassAlpha => null,
        };
}

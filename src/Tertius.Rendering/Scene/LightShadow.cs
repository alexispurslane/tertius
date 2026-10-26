namespace Tertius.Rendering;

/// <summary>A run of the frame's occluder list: the far shadows of one light.</summary>
internal readonly record struct OccluderRange(int Start, int Count);

/// <summary>
/// A far light with cascade set <paramref name="Set"/> and far shadows. The first <paramref name="Mapped"/> occluders of
/// <paramref name="Occluders"/> are casters that the set also draws.
/// </summary>
internal readonly record struct CascadeSet(int Set, OccluderRange Occluders, int Mapped);

/// <summary>A far light with far shadows everywhere and no maps.</summary>
internal readonly record struct FarShadowsOnly(OccluderRange Occluders);

/// <summary>
/// A near light with the frame's tile views <paramref name="First"/> to First + Count − 1: one for a spot light, six for
/// a point light, in cube face order.
/// </summary>
internal readonly record struct AtlasTiles(int First, int Count);

/// <summary>
/// How a light casts shadows in a frame, its shadow slot (docs/plans/local-light-shadows.md, "Shadows of each light"). The
/// empty value is the all-zero default: it holds no case and means no shadows. This is a generated union.
/// </summary>
[GeneratedUnion(typeof(CascadeSet), typeof(FarShadowsOnly), typeof(AtlasTiles))]
internal readonly partial struct LightShadow;

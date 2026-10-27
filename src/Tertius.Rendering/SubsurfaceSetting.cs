namespace Tertius.Rendering;

/// <summary>
/// No subsurface scattering: the renderer makes no scatter images and no scatter passes, and skin shades exactly as the
/// standard model.
/// </summary>
public readonly record struct NoSubsurface;

/// <summary>
/// Skin scatters its diffuse light under the surface by the separable screen-space method: a pass along the rows and
/// one along the columns with the kernel of the human skin profile (docs/plans/skin-and-hair.md, "Skin").
/// </summary>
/// <param name="TapsPerSide">The taps on each side of the center in each pass, 1 to <see cref="MaxTapsPerSide"/>.</param>
public readonly record struct SeparableSubsurface(int TapsPerSide)
{
    /// <summary>The most taps on each side: 33 per pass, which brings back the outer taps' steps of an extreme close-up.</summary>
    public const int MaxTapsPerSide = 16;

    /// <summary>The renderer's default: 8 taps per side, 17 per pass, as Jimenez et al.'s high setting.</summary>
    public static SeparableSubsurface Default => new(TapsPerSide: 8);
}

/// <summary>
/// Whether skin scatters its diffuse light under the surface (docs/plans/skin-and-hair.md, decision 29):
/// <see cref="NoSubsurface"/> or <see cref="SeparableSubsurface"/>. The default value is NoSubsurface. It is a
/// generated union (see <see cref="GeneratedUnionAttribute"/>).
/// </summary>
[GeneratedUnion(typeof(NoSubsurface), typeof(SeparableSubsurface), ZeroCase = typeof(NoSubsurface))]
public readonly partial struct SubsurfaceSetting
{
    /// <summary>Why the renderer cannot use the setting, or null when it can.</summary>
    internal string? Problem =>
        this switch
        {
            NoSubsurface => null,
            SeparableSubsurface { TapsPerSide: < 1 or > SeparableSubsurface.MaxTapsPerSide } separable =>
                $"SeparableSubsurface.TapsPerSide must be 1 to {SeparableSubsurface.MaxTapsPerSide}, not {separable.TapsPerSide}.",
            SeparableSubsurface => null,
        };
}

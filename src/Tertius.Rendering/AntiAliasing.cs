namespace Tertius.Rendering;

/// <summary>How the renderer smooths the jagged edges of the tone-mapped image (ARCHITECTURE.md, "Anti-aliasing").</summary>
public enum AntiAliasing
{
    /// <summary>No anti-aliasing: the tone map writes the output.</summary>
    None,

    /// <summary>
    /// SMAA 1x with the high preset and luma edge detection: three full-screen passes between the tone map and the
    /// overlay.
    /// </summary>
    Smaa,
}

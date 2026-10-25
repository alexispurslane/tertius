namespace Tertius.Rendering;

/// <summary>The curve that maps the HDR image's linear light to the output's range (ARCHITECTURE.md, "Tone mapping").</summary>
public enum ToneCurve
{
    /// <summary>Stephen Hill's fit of the ACES RRT and ODT, which mixes the channels for the RRT's saturation.</summary>
    AcesFitted,

    /// <summary>John Hable's filmic curve from Uncharted 2, per channel, with his published constants and exposure bias of 2.</summary>
    Hable,
}

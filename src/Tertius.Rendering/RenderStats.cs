namespace Tertius.Rendering;

/// <summary>What the last <see cref="VulkanRenderer.Render"/> drew.</summary>
public readonly record struct RenderStats
{
    /// <summary>Instances in the snapshot.</summary>
    public int SubmittedInstances { get; init; }

    /// <summary>Instances left after the visibility flag, mesh check and frustum culling, whether drawn as geometry or as impostors.</summary>
    public int VisibleInstances { get; init; }

    /// <summary>
    /// Entries in the GPU instance buffer. An opaque object that spans two depth ranges counts twice; shadow casters
    /// count once per cascade, and impostor captures and billboards are included.
    /// </summary>
    public int GpuInstances { get; init; }

    /// <summary>Opaque indirect commands in the far range (2,000 km to infinity).</summary>
    public int FarCommands { get; init; }

    /// <summary>Opaque indirect commands in the middle range (2 km to 2,000 km).</summary>
    public int MiddleCommands { get; init; }

    /// <summary>Opaque indirect commands in the near range (5 cm to 2 km).</summary>
    public int NearCommands { get; init; }

    /// <summary>Transparent indirect commands over all ranges.</summary>
    public int TransparentCommands { get; init; }

    /// <summary>Shadow caster indirect commands over all cascades.</summary>
    public int ShadowCommands { get; init; }

    /// <summary>Sun shadow cascades drawn: 4, or 0 when there was no sun or no caster.</summary>
    public int ShadowCascades { get; init; }

    /// <summary>Instances selected as shadow casters: <see cref="RenderFlags.CastsShadow"/>, within 2 km and inside a cascade.</summary>
    public int ShadowCasters { get; init; }

    /// <summary>Caster instances over all cascades. A caster in two cascades counts twice.</summary>
    public int ShadowInstances { get; init; }

    /// <summary>Objects drawn as impostor billboards instead of geometry.</summary>
    public int Impostors { get; init; }

    /// <summary>Impostor captures rendered this frame: at most 4.</summary>
    public int ImpostorCaptures { get; init; }

    /// <summary>Objects below the impostor height that still drew as geometry because they wait for a first capture.</summary>
    public int ImpostorsWaiting { get; init; }

    /// <summary>Impostor atlas tiles that hold a capture.</summary>
    public int ImpostorTilesInUse { get; init; }

    /// <summary>
    /// vkCmdDrawIndexedIndirect calls. At most one per depth range per pass, one per shadow cascade and one per
    /// impostor capture.
    /// </summary>
    public int IndirectDrawCalls { get; init; }

    /// <summary>Point and spot lights after culling.</summary>
    public int Lights { get; init; }

    /// <summary>
    /// Frame outlines that the overlay pass drew: those of the snapshot whose outline sphere has a radius above 0 and is
    /// not outside the view frustum.
    /// </summary>
    public int Outlines { get; init; }

    /// <summary>
    /// Puffs that the transparent pass drew: those of the snapshot that are alive at the render age, have a look in the
    /// snapshot's list with a shading, have a finite place and a radius above 0, and are not outside the view frustum.
    /// </summary>
    public int Puffs { get; init; }

    /// <summary>
    /// The GPU time of each pass of the last frame whose GPU work finished, which is a few frames before this one, or
    /// null before one has, or on a device that writes no timestamps. A later frame rewrites the passes in place, so a
    /// reader copies what it keeps.
    /// </summary>
    public GpuTimings? GpuTimings { get; init; }

    /// <summary>
    /// The exposure that the passes of the last frame whose GPU work finished used, which is a few frames before this one,
    /// or null before one has. A frame that the output skips keeps the reading that its wait found.
    /// </summary>
    public ExposureReading? Exposure { get; init; }

    /// <summary>False when the frame was skipped, for example because the swapchain was out of date.</summary>
    public bool Rendered { get; init; }

    /// <summary>All indirect commands of the frame.</summary>
    public int IndirectCommands => FarCommands + MiddleCommands + NearCommands + TransparentCommands + ShadowCommands;
}

namespace Tertius.Rendering;

/// <summary>
/// The least compute limits that the Vulkan specification allows, which every device meets. A pass that dispatches more
/// than they hold spreads its work so that it stays within them (ARCHITECTURE.md, "Portable Vulkan").
/// </summary>
internal static class ComputeLimits
{
    /// <summary>
    /// The workgroups that a dispatch may count along each of X, Y and Z on every Vulkan device: the least
    /// <c>maxComputeWorkGroupCount</c> that the specification allows, the limit of ANV and lavapipe.
    /// </summary>
    public const int MaxGroupsPerDimension = 65_535;
}

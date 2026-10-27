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

    /// <summary>
    /// The grid of a dispatch of <paramref name="groups"/> workgroups, at least one: as few rows as hold them with at most
    /// <see cref="MaxGroupsPerDimension"/> per row, and rows as short as hold them. So the grid ends fewer than <c>Y</c>
    /// workgroups past the last, which the shader returns from at once. Both of its counts stay within the limit of every
    /// device when <paramref name="groups"/> is at most <c>MaxGroupsPerDimension</c> squared, 4,294,836,225, and the sums
    /// do not overflow for any count of <see cref="uint"/>.
    /// </summary>
    public static (uint X, uint Y) Grid(uint groups)
    {
        const uint perRow = MaxGroupsPerDimension;
        uint rows = groups / perRow + (groups % perRow == 0 ? 0u : 1u);
        return (groups / rows + (groups % rows == 0 ? 0u : 1u), rows);
    }
}

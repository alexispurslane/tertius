namespace Tertius.Rendering;

/// <summary>The exposure that the passes of one finished frame used.</summary>
/// <param name="Frame">The number of that frame, counted from 0 by <see cref="VulkanRenderer.Render"/>.</param>
/// <param name="Stops">The exposure in stops: the passes multiplied radiance by 2^Stops.</param>
public readonly record struct ExposureReading(long Frame, float Stops);

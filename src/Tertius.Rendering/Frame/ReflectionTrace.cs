namespace Tertius.Rendering;

/// <summary>
/// The image that <see cref="ReflectionTargets"/> holds for the reflections' trace, by its resolution
/// (docs/plans/screen-space-reflections.md, "The half-resolution trace"). A built-in C# 15 union of classes, like
/// <c>CaptureTarget</c>; its default value holds no case.
/// </summary>
internal union ReflectionTrace(FullTrace, HalfTrace);

/// <summary>The full-resolution trace, whose rays add their deltas to the HDR image in place, so it has no image of its own.</summary>
internal sealed record FullTrace;

/// <summary>
/// The half-resolution trace: <paramref name="Traced"/>, RGBA16F of ⌈W / 2⌉ × ⌈H / 2⌉, holds for each texel the ray of
/// its representative pixel. <see cref="ReflectionTargets"/> owns the image.
/// </summary>
internal sealed record HalfTrace(GpuImage Traced);

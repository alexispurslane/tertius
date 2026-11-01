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
/// The half-resolution trace: <paramref name="Traced"/>, RGBA16F of ⌈W / 2⌉ × ⌈H / 2⌉, holds for each texel the result
/// of its representative pixel's ray, and <paramref name="HitDepths"/>, R32F of the same size, the view depth of its hit.
/// <paramref name="OwnRays"/> is the list of the pixels whose texels' rays disagree, which the upsample writes and its
/// own-ray dispatch reads: the dispatch's indirect command and the count of the list in its first
/// <see cref="ReflectionTargets.OwnRayHeaderBytes"/> bytes, then one packed pixel per entry, room for every pixel of the
/// screen. <see cref="ReflectionTargets"/> owns the images and the buffer.
/// </summary>
internal sealed record HalfTrace(GpuImage Traced, GpuImage HitDepths, GpuBuffer OwnRays);

using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The far ray of one slice (docs/plans/screen-space-illumination.md, "The far ray of a slice"): from R along its
/// direction, through SSR's trace. occlusion_far_rays.comp computes the same. View space is XeGTAO's: x right, y up, z
/// forward, with the camera's unit axes.
/// </summary>
internal static class FarRay
{
    /// <summary>
    /// The origin of the trace, camera-relative: R along the direction from the center, off the geometric surface by
    /// SSR's origin offset at the start's view depth.
    /// </summary>
    /// <param name="centerView">c, the texel's center in view space.</param>
    /// <param name="directionView">ω, the ray's unit direction in view space.</param>
    /// <param name="geometricNormalView">n_g, the receiver's unit geometric normal in view space.</param>
    /// <param name="radius">R, where the near range ends, in m.</param>
    /// <param name="camera">The camera of the frame.</param>
    public static Vector3 Origin(
        Double3 centerView,
        Double3 directionView,
        Double3 geometricNormalView,
        double radius,
        in CameraView camera
    )
    {
        Double3 start = centerView + directionView * radius;
        float offset = ScreenRay.OriginOffset(viewDepth: (float)start.Z, pixelAngle: camera.PixelAngle);
        return WorldOf(start, camera) + WorldOf(geometricNormalView, camera) * offset;
    }

    /// <summary>
    /// What the far ray decides for its slice's open sectors, one case per way that the trace ends: a hit blocks by the
    /// edge fade there, with the hit's direct light when it faces back along the ray. A ray into its own surface is
    /// blocked without light. A start at or behind the near plane, and every trace without a hit, are open.
    /// </summary>
    /// <param name="centerView">c, the texel's center in view space.</param>
    /// <param name="directionView">ω, the ray's unit direction in view space.</param>
    /// <param name="geometricNormalView">n_g, the receiver's unit geometric normal in view space.</param>
    /// <param name="radius">R, where the near range ends, in m.</param>
    /// <param name="camera">The camera of the frame.</param>
    /// <param name="pyramid">The depth pyramid of the frame.</param>
    /// <param name="maxSteps">The most cells that the trace examines.</param>
    /// <param name="pixelAt">The view-space geometric normal and the direct light of a full-resolution pixel.</param>
    public static FarOutcome Trace(
        Double3 centerView,
        Double3 directionView,
        Double3 geometricNormalView,
        double radius,
        in CameraView camera,
        DepthPyramid pyramid,
        int maxSteps,
        Func<(int X, int Y), (Double3 GeometricNormalView, Double3 DirectLight)> pixelAt
    )
    {
        if (Double3.Dot(directionView, geometricNormalView) <= 0)
            return FarOutcome.IntoItsOwnSurface;
        double startDepth = centerView.Z + directionView.Z * radius;
        // screenRayOf divides by the start's view depth, so a start that the near plane hides takes no trace.
        if (startDepth <= DepthRanges.NearPlane)
            return FarOutcome.Open;
        Vector3 origin = Origin(
            centerView: centerView,
            directionView: directionView,
            geometricNormalView: geometricNormalView,
            radius: radius,
            camera: camera
        );
        ScreenRay ray = ScreenRay.Of(
            origin: origin,
            direction: WorldOf(directionView, camera),
            viewDepth: (float)startDepth,
            camera: camera
        );
        if (ray.Trace(pyramid, maxSteps) is not RayHit hit)
            return FarOutcome.Open;
        double edgeFade = camera.EdgeFade(hit.Position);
        (Double3 hitNormal, Double3 hitLight) = pixelAt(hit.Pixel);
        Double3 light = SectorSearch.Faces(hitNormal, -directionView) ? hitLight : Double3.Zero;
        return new(Blocked: edgeFade, Light: light * edgeFade);
    }

    /// <summary>A view-space vector in the camera-relative world axes.</summary>
    private static Vector3 WorldOf(Double3 view, in CameraView camera) =>
        (float)view.X * camera.Right + (float)view.Y * camera.Up + (float)view.Z * camera.Forward;
}

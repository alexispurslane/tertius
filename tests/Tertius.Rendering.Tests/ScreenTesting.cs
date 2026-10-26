using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// A flat rectangle of a synthetic scene: its center, and the two half-extent vectors along its sides, in the
/// camera-relative world axes.
/// </summary>
internal readonly record struct SceneRectangle(Vector3 Center, Vector3 HalfSide, Vector3 HalfOtherSide);

/// <summary>
/// Cameras built by hand and synthetic scenes of rectangles, for the CPU tests of the screen-space reflections: each
/// pixel's inverse view depth comes from an exact intersection of its view ray with the rectangles.
/// </summary>
internal static class ScreenTesting
{
    /// <summary>A camera at the origin that looks along −Z after a turn of <paramref name="yaw"/> about +Y and a tilt
    /// of <paramref name="pitchDown"/> below the horizon, both in radians, with a vertical field of view of
    /// <paramref name="fov"/> radians, <see cref="Gpu.Fov"/> when it is not given.</summary>
    public static CameraView Camera(int width, int height, float yaw = 0, float pitchDown = 0, float fov = Gpu.Fov)
    {
        Quaternion rotation =
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -pitchDown);
        float aspect = (float)width / height;
        float tanY = MathF.Tan(fov * 0.5f);
        return new()
        {
            Rotation = rotation,
            Right = Vector3.Transform(Vector3.UnitX, rotation),
            Up = Vector3.Transform(Vector3.UnitY, rotation),
            Forward = Vector3.Transform(-Vector3.UnitZ, rotation),
            VerticalFov = fov,
            Aspect = aspect,
            TanHalfY = tanY,
            TanHalfX = tanY * aspect,
            Width = width,
            Height = height,
        };
    }

    /// <summary>The six faces of a box with its sides along the axes.</summary>
    public static SceneRectangle[] Box(Vector3 center, Vector3 halfSize) =>
        [
            new(center + new Vector3(halfSize.X, 0, 0), new(0, halfSize.Y, 0), new(0, 0, halfSize.Z)),
            new(center - new Vector3(halfSize.X, 0, 0), new(0, halfSize.Y, 0), new(0, 0, halfSize.Z)),
            new(center + new Vector3(0, halfSize.Y, 0), new(halfSize.X, 0, 0), new(0, 0, halfSize.Z)),
            new(center - new Vector3(0, halfSize.Y, 0), new(halfSize.X, 0, 0), new(0, 0, halfSize.Z)),
            new(center + new Vector3(0, 0, halfSize.Z), new(halfSize.X, 0, 0), new(0, halfSize.Y, 0)),
            new(center - new Vector3(0, 0, halfSize.Z), new(halfSize.X, 0, 0), new(0, halfSize.Y, 0)),
        ];

    /// <summary>The view depth of the nearest rectangle along a view ray whose component along forward is 1, or null
    /// when the ray meets none beyond the near plane.</summary>
    public static double? DepthAlong(Vector3 viewRay, ReadOnlySpan<SceneRectangle> scene)
    {
        double? nearest = null;
        foreach (SceneRectangle rectangle in scene)
        {
            var normal = Vector3.Cross(rectangle.HalfSide, rectangle.HalfOtherSide);
            double along = Vector3.Dot(viewRay, normal);
            if (along == 0)
                continue;
            // The ray's forward component is 1, so its parameter at the plane is the view depth.
            double depth = Vector3.Dot(rectangle.Center, normal) / along;
            if (depth <= DepthRanges.NearPlane || depth >= nearest)
                continue;
            Vector3 offset = viewRay * (float)depth - rectangle.Center;
            bool inside =
                MathF.Abs(Vector3.Dot(offset, rectangle.HalfSide)) <= rectangle.HalfSide.LengthSquared()
                && MathF.Abs(Vector3.Dot(offset, rectangle.HalfOtherSide)) <= rectangle.HalfOtherSide.LengthSquared();
            if (inside)
                nearest = depth;
        }
        return nearest;
    }

    /// <summary>The inverse view depth of each pixel's center, row by row from the top, 0 for sky.</summary>
    public static float[] InverseDepths(in CameraView camera, ReadOnlySpan<SceneRectangle> scene)
    {
        float[] inverseDepths = new float[camera.Width * camera.Height];
        for (int y = 0; y < camera.Height; y++)
        for (int x = 0; x < camera.Width; x++)
        {
            Vector3 viewRay = camera.ViewRayThrough(new(x + 0.5f, y + 0.5f));
            if (DepthAlong(viewRay, scene) is { } depth)
                inverseDepths[y * camera.Width + x] = (float)(1 / depth);
        }
        return inverseDepths;
    }

    /// <summary>The depth pyramid of a synthetic scene seen by <paramref name="camera"/>.</summary>
    public static DepthPyramid Pyramid(in CameraView camera, ReadOnlySpan<SceneRectangle> scene) =>
        DepthPyramid.FromInverseDepths(InverseDepths(camera, scene), camera.Width, camera.Height);
}

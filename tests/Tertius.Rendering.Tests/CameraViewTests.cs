using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen's geometry of the camera that the reflection trace uses: view rays, the projection of points to pixels,
/// inverse depth and the edge fade (docs/plans/screen-space-reflections.md, "The ray's image on the screen").
/// </summary>
public sealed class CameraViewTests
{
    private const int Width = 160;
    private const int Height = 90;

    [Fact]
    public void TheViewRayThroughAPixelsCenterProjectsBackToThatCenter()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height, yaw: 0.5f, pitchDown: 0.3f);
        Vector2[] centers = [new(0.5f, 0.5f), new(159.5f, 89.5f), new(80.5f, 45.5f), new(13.5f, 70.5f)];

        foreach (Vector2 center in centers)
        {
            Vector2 projected = camera.PixelOf(camera.ViewRayThrough(center));
            projected.X.Should().BeApproximately(center.X, 1e-3f);
            projected.Y.Should().BeApproximately(center.Y, 1e-3f);
        }
    }

    [Fact]
    public void AViewRaysComponentAlongForwardIsOne()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height, yaw: 0.5f, pitchDown: 0.3f);
        Vector2[] pixels = [new(0, 0), new(160, 90), new(37.25f, 12.5f)];

        foreach (Vector2 pixel in pixels)
            Vector3.Dot(camera.ViewRayThrough(pixel), camera.Forward).Should().BeApproximately(1, 1e-6f);
    }

    [Fact]
    public void InverseDepthIsAffineAlongTheImageOfAStraightRay()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height, yaw: 0.5f, pitchDown: 0.3f);
        Vector3 origin = camera.ViewRayThrough(new(30, 60)) * 2;
        var direction = Vector3.Normalize(camera.Forward * 3 + camera.Right + camera.Up * 0.5f);
        Vector3[] points = [origin, origin + direction * 3, origin + direction * 10];

        Vector2[] pixels = [.. points.Select(camera.PixelOf)];
        float[] inverseDepths = [.. points.Select(camera.InverseDepthOf)];

        Vector2 toMiddle = pixels[1] - pixels[0];
        Vector2 toEnd = pixels[2] - pixels[0];
        float share = toMiddle.Length() / toEnd.Length();
        (toMiddle.X * toEnd.Y - toMiddle.Y * toEnd.X)
            .Should()
            .BeApproximately(0, 1e-3f * toEnd.LengthSquared(), "the image of a straight ray is straight");
        inverseDepths[1]
            .Should()
            .BeApproximately(inverseDepths[0] + share * (inverseDepths[2] - inverseDepths[0]), 1e-5f);
        inverseDepths[0].Should().BeApproximately(1 / Vector3.Dot(origin, camera.Forward), 1e-6f);
    }

    [Fact]
    public void TheEdgeFadeIsZeroOnTheEdgeAndOneABandInside()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // The band is a tenth of the shorter side: 9 px.

        camera.EdgeFade(new(0, 45)).Should().Be(0);
        camera.EdgeFade(new(160, 45)).Should().Be(0);
        camera.EdgeFade(new(80, 0)).Should().Be(0);
        camera.EdgeFade(new(80, 90)).Should().Be(0);
        camera.EdgeFade(new(9, 9)).Should().Be(1);
        camera.EdgeFade(new(151, 81)).Should().Be(1);
        camera.EdgeFade(new(80, 45)).Should().Be(1);
    }

    [Fact]
    public void TheEdgeFadeHasTheSameBandOnEveryEdge()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);

        // Halfway through a 9 px band, the smoothstep is one half on each of the four edges.
        camera.EdgeFade(new(4.5f, 45)).Should().BeApproximately(0.5f, 1e-6f);
        camera.EdgeFade(new(155.5f, 45)).Should().BeApproximately(0.5f, 1e-6f);
        camera.EdgeFade(new(80, 4.5f)).Should().BeApproximately(0.5f, 1e-6f);
        camera.EdgeFade(new(80, 85.5f)).Should().BeApproximately(0.5f, 1e-6f);
    }

    [Fact]
    public void TheEdgeFadeIsASmoothstepAcrossTheBand()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);

        // A quarter of the way into the 9 px band: 3 (1/4)² − 2 (1/4)³ = 5/32.
        camera.EdgeFade(new(2.25f, 45)).Should().BeApproximately(5 / 32f, 1e-6f);
    }
}

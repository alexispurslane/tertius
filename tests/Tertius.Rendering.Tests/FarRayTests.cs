using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The far ray of a slice on the CPU: where it starts, and the outcome of each way that SSR's trace ends, with the
/// facing test of its hit (docs/plans/screen-space-illumination.md, "The far ray of a slice"). The scenes are synthetic:
/// rectangles seen by a camera built by hand that looks along −Z, so view space is (x, y, −z) of the world axes.
/// </summary>
public sealed class FarRayTests
{
    private const int Width = 160;
    private const int Height = 120;
    private const int MaxSteps = 64;

    /// <summary>The floor's top face, in m below the eye.</summary>
    private const float FloorHeight = -1.5f;

    private static readonly CameraView Camera = ScreenTesting.Camera(width: Width, height: Height);

    /// <summary>A floor that reaches 100 m in every direction, facing up.</summary>
    private static readonly Surface Floor = new(
        new(new(0, FloorHeight, 0), new(100, 0, 0), new(0, 0, 100)),
        Normal: Vector3.UnitY,
        Light: new(0.5, 0.5, 0.5)
    );

    [Fact]
    public void AFarRayStartsAtTheRadiusAlongItsDirectionWithTheOriginOffset()
    {
        var center = new Double3(0.3, -0.5, 4);
        Double3 direction = new Double3(0.2, 0.6, 0.3).Normalized();
        var normal = new Double3(0, 1, 0);
        const double radius = 0.8;

        Vector3 origin = FarRay.Origin(
            centerView: center,
            directionView: direction,
            geometricNormalView: normal,
            radius: radius,
            camera: Camera
        );

        // Two pixel footprints at the start's view depth along the geometric normal: SSR's origin offset.
        Double3 start = center + direction * radius;
        Double3 expected = start + normal * (2 * start.Z * Camera.PixelAngle);
        (origin - WorldOf(expected))
            .Length()
            .Should()
            .BeLessThan(1e-5f, "the far range begins where the near range ends, R from the center");
    }

    [Fact]
    public void AFarRayFindsAWallBeyondTheRadiusThatTheNearStepsMissed()
    {
        // A red wall 3 m behind the floor point, far beyond a radius of 0.5 m, facing the eye.
        var wall = new Surface(
            new(new(0, 0, -8), new(30, 0, 0), new(0, 30, 0)),
            Normal: Vector3.UnitZ,
            Light: new(2, 0.1, 0.1)
        );
        var receiver = new Vector3(0, FloorHeight, -5);
        // It meets the wall 1.5 m above the floor, at the middle of the screen, where the edge fade is 1.
        Vector3 toWall = Vector3.Normalize(new Vector3(0, 0, -8) - receiver);

        FarOutcome outcome = TraceFrom(
            receiver: receiver,
            direction: toWall,
            normal: Vector3.UnitY,
            radius: 0.5,
            Floor,
            wall
        );

        outcome.Blocked.Should().BeApproximately(1, 1e-6, "the wall blocks the ray in the middle of the screen");
        outcome.Light.Should().Be(wall.Light, "the wall faces back along the ray, so its direct light reaches the floor");
    }

    [Fact]
    public void AFarRayThatEntersASlabFromBehindIsBlockedWithoutLight()
    {
        // A lit panel 5 m away, facing the eye, whose slab reaches 5.5 m, and a back wall 6 m away beside it.
        var panel = new Surface(
            new(new(0, 0, -5), new(1, 0, 0), new(0, 10, 0)),
            Normal: Vector3.UnitZ,
            Light: new(5, 5, 5)
        );
        var backWall = new Surface(
            new(new(0, 0, -6), new(30, 0, 0), new(0, 30, 0)),
            Normal: Vector3.UnitZ,
            Light: new(1, 1, 1)
        );
        // From the back wall beside the panel toward −x and the eye: it passes behind the panel and enters its slab
        // through the back, at a depth of 5.5 m near x = −0.17 m.
        var receiver = new Vector3(1.5f, 0.3f, -6);
        Vector3 direction = Vector3.Normalize(new(-1, 0, 0.3f));

        FarOutcome outcome = TraceFrom(
            receiver: receiver,
            direction: direction,
            normal: Vector3.UnitZ,
            radius: 0.3,
            panel,
            backWall
        );

        outcome
            .Blocked.Should()
            .BeApproximately(1, 1e-6, "a ray that meets a slab is blocked, whatever side it enters by");
        outcome.Light.Should().Be(Double3.Zero, "the ray sees the panel's back, whose light no pixel holds");
    }

    [Fact]
    public void AFarRayThatHitsIsBlockedByTheEdgeFadeAtTheHit()
    {
        var wall = new Surface(
            new(new(0, 0, -8), new(30, 0, 0), new(0, 30, 0)),
            Normal: Vector3.UnitZ,
            Light: new(0.2, 0.9, 0.3)
        );
        // The wall point at the middle row, 6 px from the right edge: halfway into the edge band of 12 px.
        Vector3 meeting = Camera.ViewRayThrough(new(Width - 6, Height / 2f)) * 8;
        float edgeFade = Camera.EdgeFade(Camera.PixelOf(meeting));
        edgeFade.Should().BeInRange(0.2f, 0.8f, "the test needs a hit inside the band where the fade is partial");
        var receiver = new Vector3(1, FloorHeight, -5);

        FarOutcome outcome = TraceFrom(
            receiver: receiver,
            direction: Vector3.Normalize(meeting - receiver),
            normal: Vector3.UnitY,
            radius: 0.5,
            Floor,
            wall
        );

        outcome.Blocked.Should().BeApproximately(edgeFade, 0.02, "the edge fade at the hit blocks the open share");
        (outcome.Light - wall.Light * edgeFade)
            .Length.Should()
            .BeLessThan(0.02, "the blocked share carries the hit's light, {0} not {1}", outcome.Light, wall.Light);
    }

    [Fact]
    public void AFarRayThatLeavesTheScreenIsOpen()
    {
        var receiver = new Vector3(0, FloorHeight, -5);

        FarOutcome outcome = TraceFrom(
            receiver: receiver,
            direction: Vector3.Normalize(new(0.3f, 0.8f, -0.5f)),
            normal: Vector3.UnitY,
            radius: 0.5,
            Floor
        );

        outcome.Should().Be(FarOutcome.Open, "nothing on the screen lies in its way, and off the screen it is open");
    }

    [Fact]
    public void AFarRayIntoItsOwnSurfaceIsBlockedWithoutLight()
    {
        var wall = new Surface(
            new(new(0, 0, -8), new(30, 0, 0), new(0, 30, 0)),
            Normal: Vector3.UnitZ,
            Light: new(1, 1, 1)
        );
        var receiver = new Vector3(0, FloorHeight, -5);

        // Below the floor's own plane, toward the wall's lower part.
        FarOutcome outcome = TraceFrom(
            receiver: receiver,
            direction: Vector3.Normalize(new(0.3f, -0.2f, -0.9f)),
            normal: Vector3.UnitY,
            radius: 0.5,
            Floor,
            wall
        );

        outcome.Should().Be(FarOutcome.IntoItsOwnSurface, "the receiver's own surface blocks a ray below it");
    }

    [Theory]
    [InlineData(0.45)]
    [InlineData(0.47)]
    [InlineData(0.5)]
    [InlineData(0.6)]
    public void AFarRayWhoseStartLiesAtOrBehindTheNearPlaneIsOpen(double radius)
    {
        // A wall half a meter ahead, facing the eye, and a ray from it straight toward the eye: R puts the start on
        // the near plane 5 cm away, between it and the eye, at the eye, where the projection divides by 0, or behind
        // it.
        var wall = new Surface(
            new(new(0, 0, -0.5f), new(30, 0, 0), new(0, 30, 0)),
            Normal: Vector3.UnitZ,
            Light: new(1, 1, 1)
        );

        FarOutcome outcome = TraceFrom(
            receiver: new(0, 0, -0.5f),
            direction: Vector3.UnitZ,
            normal: Vector3.UnitZ,
            radius: radius,
            wall
        );

        outcome.Should().Be(FarOutcome.Open, "the ray leaves the view through the near plane before it reaches R");
    }

    /// <summary>
    /// The outcome of the far ray of a receiver at a world point, along a world direction, with the receiver's world
    /// geometric normal, in the scene of <paramref name="surfaces"/> seen by <see cref="Camera"/>.
    /// </summary>
    private static FarOutcome TraceFrom(
        Vector3 receiver,
        Vector3 direction,
        Vector3 normal,
        double radius,
        params Surface[] surfaces
    )
    {
        SceneRectangle[] scene = [.. surfaces.Select(surface => surface.Rectangle)];
        return FarRay.Trace(
            centerView: ViewOf(receiver),
            directionView: ViewOf(direction),
            geometricNormalView: ViewOf(normal),
            radius: radius,
            camera: Camera,
            pyramid: ScreenTesting.Pyramid(Camera, scene),
            maxSteps: MaxSteps,
            pixelAt: pixel => SurfaceAt(pixel, surfaces)
        );
    }

    /// <summary>The view-space normal and the direct light of the surface nearest the eye at a pixel's center.</summary>
    private static (Double3 GeometricNormalView, Double3 DirectLight) SurfaceAt(
        (int X, int Y) pixel,
        Surface[] surfaces
    )
    {
        Vector3 viewRay = Camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f));
        Surface nearest = surfaces
            .Where(surface => ScreenTesting.DepthAlong(viewRay, [surface.Rectangle]) is not null)
            .MinBy(surface => ScreenTesting.DepthAlong(viewRay, [surface.Rectangle]))!;
        return (ViewOf(nearest.Normal), nearest.Light);
    }

    /// <summary>A world vector in view space: x right, y up, z forward.</summary>
    private static Double3 ViewOf(Vector3 world) =>
        new(Vector3.Dot(world, Camera.Right), Vector3.Dot(world, Camera.Up), Vector3.Dot(world, Camera.Forward));

    /// <summary>A view-space vector in the camera-relative world axes.</summary>
    private static Vector3 WorldOf(Double3 view) =>
        (float)view.X * Camera.Right + (float)view.Y * Camera.Up + (float)view.Z * Camera.Forward;

    /// <summary>A rectangle of the scene with the world normal of its visible face and its direct light.</summary>
    private readonly record struct Surface(SceneRectangle Rectangle, Vector3 Normal, Double3 Light);
}

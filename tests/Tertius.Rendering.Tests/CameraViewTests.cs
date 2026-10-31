using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The screen's geometry of the camera that the reflection trace uses: view rays, the projection of points to pixels,
/// inverse depth and the edge fade (docs/plans/screen-space-reflections.md, "The ray's image on the screen"). Also the
/// cull of a spot lamp's cone cut at its reach (ARCHITECTURE.md, "The reach from the exposure"), and the eye's distance to
/// the point of a lamp's axis in view nearest the eye, by which the shadow budgets rank a spot lamp (ARCHITECTURE.md,
/// "Local shadows", the rank).
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

    /// <summary>A spot lamp's cone along the unit <paramref name="axis"/> with the outer half-angle <paramref name="outerAngle"/>.</summary>
    private static Cone ConeAlong(Vector3 axis, float outerAngle) =>
        Cone.Of(Vector3.Normalize(axis), innerAngle: 0, outerAngle: outerAngle);

    [Fact]
    public void AConeAimedAwayFromTheViewIsCulledThoughTheSphereOfItsReachIsNot()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        var apex = new Vector3(0, 3, 30);

        camera.Culls(apex, ConeAlong(Vector3.UnitZ, 0.5f), reach: 35).Should().BeTrue();
        camera.Culls(apex, 35).Should().BeFalse("the sphere of the reach holds the eye");
    }

    [Fact]
    public void AConeAimedIntoTheViewFromOutsideItIsKept()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);

        camera.Culls(new(20, 0, -5), ConeAlong(-Vector3.UnitX, 0.5f), reach: 25).Should().BeFalse();
    }

    [Fact]
    public void AConeOfAWholeTurnCullsExactlyAsTheSphereOfItsReach()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height, yaw: 0.5f, pitchDown: 0.3f);
        Cone whole = ConeAlong(Vector3.UnitX, MathF.PI);

        for (int x = -6; x <= 6; x++)
        for (int y = -6; y <= 6; y++)
        for (int z = -6; z <= 6; z++)
        {
            var apex = new Vector3(x * 5f, y * 5f, z * 5f);
            camera.Culls(apex, whole, reach: 7).Should().Be(camera.Culls(apex, 7), $"at {apex}");
        }
    }

    [Fact]
    public void ANarrowConeThatOneSidePlaneCullsIsCulledThoughTheSphereOfItsReachIsNot()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // 20 m right of the view's axis and 5 m ahead, aimed further right: the right side plane lies 10.4 m from the lamp,
        // inside the reach of 12 m, but no direction of the cone goes toward that plane.
        var apex = new Vector3(20, 0, -5);

        camera.Culls(apex, ConeAlong(Vector3.UnitX, 0.1f), reach: 12).Should().BeTrue();
        camera.Culls(apex, 12).Should().BeFalse("the sphere of the reach crosses the right side plane");
    }

    [Fact]
    public void TheNearPlaneCullsAConeExactlyWhenItsEdgeStopsShortOfIt()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // 2 m behind the eye, opened 30° past a quarter turn: its edge comes reach · sin 30° toward the view, so it
        // crosses the near plane, 0.05 m ahead of the eye, from a reach of 4.1 m.
        var apex = new Vector3(0, 0, 2);
        Cone wide = ConeAlong(Vector3.UnitZ, MathF.PI * 2 / 3);

        camera.Culls(apex, wide, reach: 4.0f).Should().BeTrue();
        camera.Culls(apex, wide, reach: 4.2f).Should().BeFalse();
    }

    [Fact]
    public void NoConeWithAPointInsideTheViewIsCulled()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height, yaw: 0.5f, pitchDown: 0.3f);
        var random = new Random(19);

        for (int cone = 0; cone < 2000; cone++)
        {
            var apex = new Vector3(Uniform(random, 20), Uniform(random, 20), Uniform(random, 20));
            var axis = Vector3.Normalize(new(Uniform(random, 1), Uniform(random, 1), Uniform(random, 1)));
            float outerAngle = 0.05f + (float)random.NextDouble() * 3f;
            float reach = 1 + (float)random.NextDouble() * 30;
            Cone spread = ConeAlong(axis, outerAngle);
            bool seen = Enumerable.Range(0, 64).Any(_ => InView(camera, apex + PointOfCone(random, spread, reach)));

            if (seen)
                camera
                    .Culls(apex, spread, reach)
                    .Should()
                    .BeFalse($"a point of the cone at {apex} along {axis}, {outerAngle} rad, {reach} m, lies in view");
        }
    }

    /// <summary>A uniform random value in [−<paramref name="half"/>, <paramref name="half"/>).</summary>
    private static float Uniform(Random random, float half) => ((float)random.NextDouble() * 2 - 1) * half;

    /// <summary>A point of <paramref name="cone"/> cut at <paramref name="reach"/>, relative to its apex.</summary>
    private static Vector3 PointOfCone(Random random, Cone cone, float reach)
    {
        var side = Vector3.Normalize(
            Vector3.Cross(cone.Axis, MathF.Abs(cone.Axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY)
        );
        var other = Vector3.Cross(cone.Axis, side);
        float off = (float)random.NextDouble() * cone.OuterAngle * 0.999f;
        float around = (float)random.NextDouble() * 2 * MathF.PI;
        Vector3 direction =
            MathF.Cos(off) * cone.Axis + MathF.Sin(off) * (MathF.Cos(around) * side + MathF.Sin(around) * other);
        return direction * reach * (float)random.NextDouble();
    }

    /// <summary>Whether a camera-relative point lies past the near plane and inside the four side planes.</summary>
    private static bool InView(CameraView camera, Vector3 point)
    {
        float depth = Vector3.Dot(point, camera.Forward);
        return depth >= DepthRanges.NearPlane
            && MathF.Abs(Vector3.Dot(point, camera.Right)) <= depth * camera.TanHalfX
            && MathF.Abs(Vector3.Dot(point, camera.Up)) <= depth * camera.TanHalfY;
    }

    [Fact]
    public void TheEyesDistanceToAnAxisInViewIsItsDistanceToWhereTheAxisEntersTheView()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);
        // 10 m right of the eye, aimed straight ahead: the right side plane, x = depth · tan, meets it at depth 10 / tan.
        float entry = 10 / camera.TanHalfX;

        float? nearest = camera.EyeDistanceToAxisInView(new(10, 0, 0), -Vector3.UnitZ, reach: 30);

        nearest.Should().NotBeNull();
        nearest!.Value.Should().BeApproximately(MathF.Sqrt(10 * 10 + entry * entry), 1e-4f);
    }

    [Fact]
    public void TheEyesDistanceToAnAxisInViewIsItsDistanceToTheFootOfThePerpendicularWhenThatLiesInView()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);

        // 20 m ahead and 3 m up, aimed down: the foot of the perpendicular from the eye lies 3 m below the lamp.
        camera.EyeDistanceToAxisInView(new(0, 3, -20), -Vector3.UnitY, reach: 30).Should().BeApproximately(20, 1e-5f);
    }

    [Fact]
    public void AnAxisThatEndsBeforeTheViewHasNoDistanceInView()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);

        camera.EyeDistanceToAxisInView(new(10, 0, 0), -Vector3.UnitZ, reach: 5).Should().BeNull();
    }

    [Fact]
    public void AnAxisParallelToTheNearPlaneAndBehindItHasNoDistanceInView()
    {
        CameraView camera = ScreenTesting.Camera(width: Width, height: Height);

        // 1 cm ahead of the eye, behind the near plane at 5 cm, aimed right: the four side planes keep a piece of the axis
        // within 1 cm of the lamp, but the axis never crosses the near plane.
        camera.EyeDistanceToAxisInView(new(0, 0, -0.01f), Vector3.UnitX, reach: 30).Should().BeNull();
    }
}

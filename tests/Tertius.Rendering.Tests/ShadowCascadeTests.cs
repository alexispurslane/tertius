using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>The cascade math on the CPU: splits, sphere fits, stability and the light-space matrices. No GPU needed.</summary>
public sealed class ShadowCascadeTests
{
    private const int MapSize = 2048;

    private static CameraView Camera(Quaternion rotation, float verticalFov = MathF.PI / 3f, float aspect = 16f / 9f)
    {
        float tanY = MathF.Tan(verticalFov / 2);
        return new()
        {
            Rotation = rotation,
            Right = Vector3.Transform(Vector3.UnitX, rotation),
            Up = Vector3.Transform(Vector3.UnitY, rotation),
            Forward = Vector3.Transform(-Vector3.UnitZ, rotation),
            VerticalFov = verticalFov,
            Aspect = aspect,
            TanHalfX = tanY * aspect,
            TanHalfY = tanY,
            Width = 1280,
            Height = 720,
            View = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rotation))
        };
    }

    private static ShadowCascadeArray Fit(in CameraView camera, Vector3 sun, Double3 anchor = default)
    {
        var cascades = new ShadowCascadeArray();
        ShadowCascades.Fit(camera: camera, light: LightBasis.FromSunDirection(sun), mapSize: MapSize, depthRangeScale: 16f, anchor: anchor, cascades: ref cascades);
        return cascades;
    }

    /// <summary>The corners of the view frustum between two view depths, in camera-relative world space.</summary>
    private static IEnumerable<Vector3> SliceCorners(CameraView camera, float near, float far)
    {
        foreach (float depth in (float[])[near, far])
        foreach (float sx in (float[])[-1f, 1f])
        foreach (float sy in (float[])[-1f, 1f])
            yield return (camera.Forward + camera.Right * (sx * camera.TanHalfX) + camera.Up * (sy * camera.TanHalfY)) *
                         depth;
    }

    [Fact]
    public void SplitsFollowThePracticalSchemeAndEndAtTwoKilometres()
    {
        ShadowCascades.Split(0).Should().BeApproximately(50.6f, 0.5f);
        ShadowCascades.Split(1).Should().BeApproximately(109.0f, 0.5f);
        ShadowCascades.Split(2).Should().BeApproximately(277.3f, 0.5f);
        ShadowCascades.Split(3).Should().Be(ShadowCascades.Distance);
        float start = DepthRanges.NearPlane;
        for (int i = 0; i < ShadowCascades.Count; i++)
        {
            float split = ShadowCascades.Split(i);
            split.Should().BeGreaterThan(start);
            // Each cascade blends into the next over the last 10% of its own interval.
            ShadowCascades.BlendStart(i).Should().BeApproximately(split - 0.1f * (split - start), 1e-3f);
            start = split;
        }
    }

    [Theory]
    [InlineData(0.05f, 50f, 0.577f, 1.026f)] // 60 degrees at 16:9: centered on the far cap
    [InlineData(100f, 300f, 0.577f, 1.026f)]
    [InlineData(10f, 2_000f, 0.05f, 0.05f)] // a narrow zoom: centered between the planes
    [InlineData(500f, 600f, 0.2f, 0.1f)]
    public void TheBoundingSphereHoldsItsSliceAndTouchesIt(float near, float far, float tanX, float tanY)
    {
        (float centerDepth, float radius) = ShadowCascades.BoundingSphere(near: near, far: far, tanHalfX: tanX, tanHalfY: tanY);
        CameraView camera = Camera(Quaternion.Identity) with { TanHalfX = tanX, TanHalfY = tanY };
        var center = new Vector3(0, 0, -centerDepth);
        float farthest = SliceCorners(camera, near, far).Max(corner => Vector3.Distance(corner, center));
        farthest.Should().BeLessThanOrEqualTo(radius * (1 + 1e-5f), "the sphere holds every corner of the slice");
        // Smallest: some corner is on the sphere.
        farthest.Should()
            .BeGreaterThanOrEqualTo(radius * (1 - 1e-4f), "the smallest sphere touches the farthest corner");
    }

    [Fact]
    public void AZoomedCameraKeepsEachCascadeToItsOwnSliceSoTheShadowsStaySharp()
    {
        // With a 5 degree field of view the bounding sphere of a slice depends on where the slice starts. Each cascade
        // reaches back only over the previous cascade's blend band, not to the camera, which keeps its texels small.
        CameraView camera = Camera(Quaternion.Identity, verticalFov: 5f * MathF.PI / 180f);
        ShadowCascadeArray cascades = Fit(camera, new(0.3f, 0.8f, -0.4f));
        for (int i = 0; i < ShadowCascades.Count; i++)
        {
            float start = i == 0 ? DepthRanges.NearPlane : ShadowCascades.BlendStart(i - 1);
            (_, float own) =
                ShadowCascades.BoundingSphere(near: start, far: ShadowCascades.Split(i), tanHalfX: camera.TanHalfX, tanHalfY: camera.TanHalfY);
            (_, float fromCamera) = ShadowCascades.BoundingSphere(near: DepthRanges.NearPlane, far: ShadowCascades.Split(i),
                tanHalfX: camera.TanHalfX, tanHalfY: camera.TanHalfY);
            (cascades[i].NearDepth, cascades[i].FarDepth).Should().Be((start, ShadowCascades.Split(i)));
            cascades[i].Radius.Should().Be(own, "cascade {0} bounds its own slice", i);
            if (i > 0)
                cascades[i].TexelSize.Should()
                    .BeLessThan(0.9f * 2f * fromCamera / (MapSize - 2 * ShadowCascades.MarginTexels),
                        "a sphere back to the camera would be coarser");
        }
    }

    [Fact]
    public void SnappingMovesEachCascadeByAtMostHalfATexel()
    {
        // The margin keeps half a texel for snapping: the box moves to the nearest grid point, never further.
        var sun = new Vector3(-0.4f, 0.7f, 0.25f);
        LightBasis light = LightBasis.FromSunDirection(sun);
        CameraView camera = Camera(Quaternion.CreateFromYawPitchRoll(0.9f, -0.3f, 0f));
        var random = new Random(20261010);
        for (int trial = 0; trial < 200; trial++)
        {
            var anchor = new Double3(random.NextDouble() * 2_000 - 1_000, random.NextDouble() * 50,
                random.NextDouble() * 2_000 - 1_000);
            ShadowCascadeArray cascades = Fit(camera, sun, anchor);
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                (float centerDepth, _) = ShadowCascades.BoundingSphere(near: cascades[i].NearDepth, far: cascades[i].FarDepth,
                    tanHalfX: camera.TanHalfX, tanHalfY: camera.TanHalfY);
                Vector3 unsnapped = light.ToLight(camera.Forward * centerDepth);
                float texel = cascades[i].TexelSize;
                MathF.Abs(cascades[i].Center.X - unsnapped.X).Should().BeLessThanOrEqualTo(0.5001f * texel,
                    "trial {0} (seed 20261010), cascade {1}, x", trial, i);
                MathF.Abs(cascades[i].Center.Y - unsnapped.Y).Should().BeLessThanOrEqualTo(0.5001f * texel,
                    "trial {0} (seed 20261010), cascade {1}, y", trial, i);
            }
        }
    }

    [Fact]
    public void TurningTheCameraNeverChangesTheCascadeSizes()
    {
        var sun = new Vector3(0.3f, 0.8f, -0.4f);
        ShadowCascadeArray reference = Fit(Camera(Quaternion.Identity), sun);
        foreach (Quaternion rotation in (Quaternion[])
                 [
                     Quaternion.CreateFromYawPitchRoll(0.7f, 0f, 0f),
                     Quaternion.CreateFromYawPitchRoll(-2.1f, 0.6f, 0.2f),
                     Quaternion.CreateFromYawPitchRoll(3.0f, -1.2f, -0.4f)
                 ])
        {
            ShadowCascadeArray turned = Fit(Camera(rotation), sun);
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                turned[i].TexelSize.Should().Be(reference[i].TexelSize);
                turned[i].HalfExtent.Should().Be(reference[i].HalfExtent);
            }
        }
    }

    [Fact]
    public void EveryCascadeBoxHoldsItsSliceInsideTheMarginAndTheDepthRange()
    {
        var sun = new Vector3(-0.5f, 0.35f, 0.2f);
        foreach (Quaternion rotation in (Quaternion[])
                 [Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(1.3f, -0.5f, 0.1f)])
        {
            CameraView camera = Camera(rotation);
            ShadowCascadeArray cascades = Fit(camera, sun, new(123.456, -7.89, 1e6));
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                ShadowCascade cascade = cascades[i];
                cascade.TexelSize.Should()
                    .BeApproximately(cascade.HalfExtent * 2f / MapSize, cascade.TexelSize * 1e-5f);
                cascade.DepthHalfRange.Should().Be(16f * cascade.HalfExtent);
                // The snapped box keeps the margin minus half a texel of snapping free on every side.
                float limit = 1f - (ShadowCascades.MarginTexels - 0.5f) * 2f / MapSize;
                foreach (Vector3 corner in SliceCorners(camera, cascade.NearDepth, cascade.FarDepth))
                {
                    Vector4 clip = Vector4.Transform(new Vector4(corner, 1f), cascade.ViewProjection);
                    clip.X.Should().BeInRange(-limit, limit);
                    clip.Y.Should().BeInRange(-limit, limit);
                    clip.Z.Should().BeInRange(0.5f - 1f / 32f, 0.5f + 1f / 32f);
                    clip.W.Should().Be(1f);
                }
            }
        }
    }

    [Fact]
    public void ShadowDepthIsReversedAndMeasuredAlongTheSun()
    {
        Vector3 sun = Vector3.Normalize(new(0.2f, 1f, 0.3f));
        ShadowCascade cascade = Fit(Camera(Quaternion.Identity), sun)[0];
        LightBasis light = LightBasis.FromSunDirection(sun);
        Vector3 center = light.X * cascade.Center.X + light.Y * cascade.Center.Y + light.Z * cascade.Center.Z;
        float Depth(Vector3 p) => Vector4.Transform(new Vector4(p, 1f), cascade.ViewProjection).Z;
        Depth(center).Should().BeApproximately(0.5f, 1e-5f);
        Depth(center + sun * cascade.DepthHalfRange).Should().BeApproximately(1f, 1e-4f);
        Depth(center - sun * cascade.DepthHalfRange).Should().BeApproximately(0f, 1e-4f);
        // Moving across the light changes nothing.
        Depth(center + light.X * 7f + light.Y * 3f).Should().BeApproximately(Depth(center), 1e-5f);
    }

    [Fact]
    public void TheLightBasisIsOrthonormalRightHandedAndFollowsTheSun()
    {
        foreach (Vector3 sun in (Vector3[])
                 [new(0.3f, 0.8f, -0.4f), new(0f, 1f, 0f), new(0.001f, -1f, 0.002f), new(1f, 0f, 0f)])
        {
            LightBasis light = LightBasis.FromSunDirection(sun);
            light.X.Length().Should().BeApproximately(1f, 1e-5f);
            light.Y.Length().Should().BeApproximately(1f, 1e-5f);
            Vector3.Dot(light.X, light.Y).Should().BeApproximately(0f, 1e-5f);
            Vector3.Distance(Vector3.Normalize(sun), light.Z).Should().BeLessThan(1e-6f, "z points toward the sun");
            Vector3.Distance(Vector3.Cross(light.X, light.Y), light.Z).Should()
                .BeLessThan(1e-5f, "the basis is right-handed");
        }
    }

    [Fact]
    public void TheTexelGridIsFixedInTheWorldWhenTheOriginMoves()
    {
        var sun = new Vector3(0.4f, 0.8f, -0.3f);
        var world = new Vector3(17.3f, -2f, -41.9f);
        Vector2? fraction = null;
        // The same world point seen from cameras at different places: positions are camera-relative, and the anchor
        // is a fixed world point (here the world origin) seen from each camera.
        foreach (Vector3 eye in (Vector3[])[new(0f, 0f, 0f), new(0.31f, 0.07f, -1.13f), new(-251.5f, 3.2f, 977.25f)])
        {
            ShadowCascade cascade = Fit(Camera(Quaternion.CreateFromYawPitchRoll(eye.X * 0.01f, 0f, 0f)), sun,
                -(Double3)eye)[1];
            Vector4 clip = Vector4.Transform(new Vector4(world - eye, 1f), cascade.ViewProjection);
            Vector2 texel = new Vector2(clip.X * 0.5f + 0.5f, clip.Y * 0.5f + 0.5f) * MapSize;
            var current = new Vector2(texel.X - MathF.Floor(texel.X), texel.Y - MathF.Floor(texel.Y));
            fraction ??= current;
            float dx = MathF.Abs(current.X - fraction.Value.X), dy = MathF.Abs(current.Y - fraction.Value.Y);
            MathF.Min(dx, 1 - dx).Should().BeLessThan(0.01f,
                "the point keeps its place across texels as the eye moves to {0}", eye);
            MathF.Min(dy, 1 - dy).Should().BeLessThan(0.01f,
                "the point keeps its place across texels as the eye moves to {0}", eye);
        }
    }

    [Fact]
    public void TheAnchorIsTheFrameOriginSeenFromTheInterpolatedEye()
    {
        // The render origin is 6,400 km up in a planet frame, and the interpolated eye is a little behind it.
        var origin = new Double3(1_234.5, 6.4e6, -987.25);
        var offset = new Vector3(0.25f, -1.5f, 3f);
        Double3 anchor = ShadowCascades.Anchor(origin, offset);
        // Exact: in doubles, 6,400 km keeps every bit of a float offset.
        anchor.Should().Be(new Double3(-1_234.75, -6.4e6 + 1.5, 987.25 - 3),
            "the frame origin is at −origin − offset from the eye");

        // An origin that is not finite falls back to the render origin, so the cascades stay finite.
        ShadowCascades.Anchor(new(double.NaN, 0, 0), offset).Should().Be(-(Double3)offset);
        ShadowCascades.Anchor(new(0, double.PositiveInfinity, 0), Vector3.Zero).Should().Be(Double3.Zero);
    }
}

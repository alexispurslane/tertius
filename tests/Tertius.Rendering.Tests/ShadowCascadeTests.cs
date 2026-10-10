using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>The cascade math on the CPU: splits, sphere fits, stability and the light-space matrices. No GPU needed.</summary>
public sealed class ShadowCascadeTests
{
    private const int MapSize = 2048;

    private static CameraView Camera(Quaternion rotation, float verticalFov = MathF.PI / 3f, float aspect = 16f / 9f)
    {
        float tanY = MathF.Tan(verticalFov / 2);
        return new CameraView
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
            View = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rotation)),
        };
    }

    private static ShadowCascadeArray Fit(in CameraView camera, Vector3 sun, Double3 anchor = default)
    {
        var cascades = new ShadowCascadeArray();
        ShadowCascades.Fit(camera, LightBasis.FromSunDirection(sun), MapSize, 16f, anchor, ref cascades);
        return cascades;
    }

    /// <summary>The corners of the view frustum between two view depths, in camera-relative world space.</summary>
    private static IEnumerable<Vector3> SliceCorners(CameraView camera, float near, float far)
    {
        foreach (float depth in (float[])[near, far])
        {
            foreach (float sx in (float[])[-1f, 1f])
            {
                foreach (float sy in (float[])[-1f, 1f])
                    yield return (camera.Forward + camera.Right * (sx * camera.TanHalfX) + camera.Up * (sy * camera.TanHalfY)) * depth;
            }
        }
    }

    [Fact]
    public void SplitsFollowThePracticalSchemeAndEndAtTwoKilometres()
    {
        Assert.Equal(50.6f, ShadowCascades.Split(0), 0.5f);
        Assert.Equal(109.0f, ShadowCascades.Split(1), 0.5f);
        Assert.Equal(277.3f, ShadowCascades.Split(2), 0.5f);
        Assert.Equal(ShadowCascades.Distance, ShadowCascades.Split(3));
        float start = DepthRanges.NearPlane;
        for (int i = 0; i < ShadowCascades.Count; i++)
        {
            float split = ShadowCascades.Split(i);
            Assert.True(split > start);
            // Each cascade blends into the next over the last 10% of its own interval.
            Assert.Equal(split - 0.1f * (split - start), ShadowCascades.BlendStart(i), 3);
            start = split;
        }
    }

    [Theory]
    [InlineData(0.05f, 50f, 0.577f, 1.026f)]     // 60 degrees at 16:9: centered on the far cap
    [InlineData(100f, 300f, 0.577f, 1.026f)]
    [InlineData(10f, 2_000f, 0.05f, 0.05f)]       // a narrow zoom: centered between the planes
    [InlineData(500f, 600f, 0.2f, 0.1f)]
    public void TheBoundingSphereHoldsItsSliceAndTouchesIt(float near, float far, float tanX, float tanY)
    {
        (float centerDepth, float radius) = ShadowCascades.BoundingSphere(near, far, tanX, tanY);
        CameraView camera = Camera(Quaternion.Identity) with { TanHalfX = tanX, TanHalfY = tanY };
        var center = new Vector3(0, 0, -centerDepth);
        float farthest = SliceCorners(camera, near, far).Max(corner => Vector3.Distance(corner, center));
        Assert.True(farthest <= radius * (1 + 1e-5f), $"a corner is {farthest} from the center, radius {radius}");
        // Smallest: some corner is on the sphere.
        Assert.True(farthest >= radius * (1 - 1e-4f), $"the farthest corner is {farthest}, radius {radius}");
    }

    [Fact]
    public void TurningTheCameraNeverChangesTheCascadeSizes()
    {
        var sun = new Vector3(0.3f, 0.8f, -0.4f);
        ShadowCascadeArray reference = Fit(Camera(Quaternion.Identity), sun);
        foreach (Quaternion rotation in (Quaternion[])[
            Quaternion.CreateFromYawPitchRoll(0.7f, 0f, 0f),
            Quaternion.CreateFromYawPitchRoll(-2.1f, 0.6f, 0.2f),
            Quaternion.CreateFromYawPitchRoll(3.0f, -1.2f, -0.4f)])
        {
            ShadowCascadeArray turned = Fit(Camera(rotation), sun);
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                Assert.Equal(reference[i].TexelSize, turned[i].TexelSize);
                Assert.Equal(reference[i].HalfExtent, turned[i].HalfExtent);
            }
        }
    }

    [Fact]
    public void EveryCascadeBoxHoldsItsSliceInsideTheMarginAndTheDepthRange()
    {
        var sun = new Vector3(-0.5f, 0.35f, 0.2f);
        foreach (Quaternion rotation in (Quaternion[])[Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(1.3f, -0.5f, 0.1f)])
        {
            CameraView camera = Camera(rotation);
            ShadowCascadeArray cascades = Fit(camera, sun, new Double3(123.456, -7.89, 1e6));
            for (int i = 0; i < ShadowCascades.Count; i++)
            {
                ShadowCascade cascade = cascades[i];
                Assert.Equal(cascade.HalfExtent * 2f / MapSize, cascade.TexelSize, cascade.TexelSize * 1e-5f);
                Assert.Equal(16f * cascade.HalfExtent, cascade.DepthHalfRange);
                // The snapped box keeps the margin minus half a texel of snapping free on every side.
                float limit = 1f - (ShadowCascades.MarginTexels - 0.5f) * 2f / MapSize;
                foreach (Vector3 corner in SliceCorners(camera, cascade.NearDepth, cascade.FarDepth))
                {
                    Vector4 clip = Vector4.Transform(new Vector4(corner, 1f), cascade.ViewProjection);
                    Assert.InRange(clip.X, -limit, limit);
                    Assert.InRange(clip.Y, -limit, limit);
                    Assert.InRange(clip.Z, 0.5f - 1f / 32f, 0.5f + 1f / 32f);
                    Assert.Equal(1f, clip.W);
                }
            }
        }
    }

    [Fact]
    public void ShadowDepthIsReversedAndMeasuredAlongTheSun()
    {
        var sun = Vector3.Normalize(new Vector3(0.2f, 1f, 0.3f));
        ShadowCascade cascade = Fit(Camera(Quaternion.Identity), sun)[0];
        var light = LightBasis.FromSunDirection(sun);
        Vector3 center = light.X * cascade.Center.X + light.Y * cascade.Center.Y + light.Z * cascade.Center.Z;
        float Depth(Vector3 p) => Vector4.Transform(new Vector4(p, 1f), cascade.ViewProjection).Z;
        Assert.Equal(0.5f, Depth(center), 5);
        Assert.Equal(1f, Depth(center + sun * cascade.DepthHalfRange), 4);
        Assert.Equal(0f, Depth(center - sun * cascade.DepthHalfRange), 4);
        // Moving across the light changes nothing.
        Assert.Equal(Depth(center), Depth(center + light.X * 7f + light.Y * 3f), 5);
    }

    [Fact]
    public void TheLightBasisIsOrthonormalRightHandedAndFollowsTheSun()
    {
        foreach (Vector3 sun in (Vector3[])[new(0.3f, 0.8f, -0.4f), new(0f, 1f, 0f), new(0.001f, -1f, 0.002f), new(1f, 0f, 0f)])
        {
            var light = LightBasis.FromSunDirection(sun);
            Assert.Equal(1f, light.X.Length(), 5);
            Assert.Equal(1f, light.Y.Length(), 5);
            Assert.Equal(0f, Vector3.Dot(light.X, light.Y), 5);
            Assert.True(Vector3.Distance(Vector3.Normalize(sun), light.Z) < 1e-6f);
            Assert.True(Vector3.Distance(Vector3.Cross(light.X, light.Y), light.Z) < 1e-5f);
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
            ShadowCascade cascade = Fit(Camera(Quaternion.CreateFromYawPitchRoll(eye.X * 0.01f, 0f, 0f)), sun, -(Double3)eye)[1];
            Vector4 clip = Vector4.Transform(new Vector4(world - eye, 1f), cascade.ViewProjection);
            var texel = new Vector2(clip.X * 0.5f + 0.5f, clip.Y * 0.5f + 0.5f) * MapSize;
            var current = new Vector2(texel.X - MathF.Floor(texel.X), texel.Y - MathF.Floor(texel.Y));
            fraction ??= current;
            float dx = MathF.Abs(current.X - fraction.Value.X), dy = MathF.Abs(current.Y - fraction.Value.Y);
            Assert.True(MathF.Min(dx, 1 - dx) < 0.01f && MathF.Min(dy, 1 - dy) < 0.01f, $"texel fraction {current} vs {fraction}");
        }
    }
}

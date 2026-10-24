using System.Numerics;

namespace Tertius.Rendering.Tests;

public sealed class DepthRangeTests
{
    private const int Far = 1 << DepthRanges.Far;
    private const int Middle = 1 << DepthRanges.Middle;
    private const int Near = 1 << DepthRanges.Near;

    private static float Depth(Matrix4x4 projection, float viewDepth)
    {
        var clip = Vector4.Transform(new Vector4(x: 0, y: 0, z: -viewDepth, w: 1), projection);
        return clip.Z / clip.W;
    }

    [Fact]
    public void ProjectionIsReversedZ()
    {
        Matrix4x4 finite = DepthRanges.Projection(verticalFov: 1f, aspect: 1.5f, near: 0.05f, far: 2_000f);
        Depth(finite, 0.05f).Should().BeApproximately(1f, 1e-5f);
        Depth(finite, 2_000f).Should().BeApproximately(0f, 1e-5f);
        Depth(finite, 1f).Should().BeGreaterThan(Depth(finite, 10f), "nearer is greater");

        Matrix4x4 infinite = DepthRanges.Projection(
            verticalFov: 1f,
            aspect: 1.5f,
            near: 2e6f,
            far: float.PositiveInfinity
        );
        Depth(infinite, 2e6f).Should().BeApproximately(1f, 1e-5f);
        Depth(infinite, 1.5e11f).Should().BeInRange(1e-6f, 1e-4f);
        Depth(infinite, 1e7f).Should().BeGreaterThan(Depth(infinite, 1e8f), "nearer is greater");
    }

    [Fact]
    public void EachRangeIsCutOffAtItsFarBoundaryByTheDepthClearValue()
    {
        foreach (int range in (int[])[DepthRanges.Near, DepthRanges.Middle])
        {
            Matrix4x4 projection = DepthRanges.RangeProjection(range, 1f, 1.5f);
            float boundary = DepthRanges.FarOf(range);
            float clear = DepthRanges.ClearDepth(range);
            Depth(projection, boundary).Should().BeApproximately(clear, clear * 1e-5f);
            // Compare GREATER: just inside the boundary passes against the cleared depth, just beyond it fails.
            Depth(projection, boundary * 0.999f).Should().BeGreaterThan(clear);
            Depth(projection, boundary * 1.001f).Should().BeLessThan(clear);
            Depth(projection, DepthRanges.NearOf(range)).Should().BeApproximately(1f, 1e-5f);
        }

        DepthRanges.ClearDepth(DepthRanges.Far).Should().Be(0f);
    }

    [Fact]
    public void ProjectionFlipsYForVulkan()
    {
        Matrix4x4 projection = DepthRanges.Projection(verticalFov: MathF.PI / 2, aspect: 2f, near: 0.1f, far: 100f);
        // A point above the view axis lands in the upper half: negative y in Vulkan NDC.
        var up = Vector4.Transform(new Vector4(x: 0, y: 1, z: -2, w: 1), projection);
        var right = Vector4.Transform(new Vector4(x: 1, y: 0, z: -2, w: 1), projection);
        (up.Y / up.W).Should().BeNegative();
        (up.Y / up.W).Should().BeApproximately(-0.5f, 1e-5f);
        (right.X / right.W).Should().BeApproximately(0.25f, 1e-5f);
    }

    [Fact]
    public void RangesOverlapAtTheirBoundaries()
    {
        DepthRanges.FarOf(DepthRanges.Near).Should().BeGreaterThan(DepthRanges.NearOf(DepthRanges.Middle));
        DepthRanges.FarOf(DepthRanges.Middle).Should().BeGreaterThan(DepthRanges.NearOf(DepthRanges.Far));
        DepthRanges.NearOf(DepthRanges.Near).Should().Be(DepthRanges.NearPlane);
        DepthRanges.FarOf(DepthRanges.Far).Should().Be(float.PositiveInfinity);
    }

    [Theory]
    [InlineData(10f, 1f, Near)]
    [InlineData(1_995f, 4f, Near)] // fits in the near slab thanks to the overlap
    [InlineData(1_999f, 30f, Near | Middle)] // straddles 2 km
    [InlineData(50_000f, 600f, Middle)] // the station
    [InlineData(3.0e7f, 6.0e6f, Far)] // the planet
    [InlineData(1.0e6f, 1.5e6f, Near | Middle | Far)] // reaches from the camera to beyond 2,000 km
    [InlineData(2.0e6f, 1.0e4f, Middle)] // fits in the middle slab thanks to the overlap
    [InlineData(2.0e6f, 3.0e4f, Middle | Far)] // straddles 2,000 km
    [InlineData(1.5e11f, 7.0e8f, Far)] // the star
    [InlineData(0.02f, 0.5f, Near)] // touches the camera
    public void OpaqueObjectsGoToEveryRangeTheyReach(float depth, float radius, int expected) =>
        DepthRanges.OpaqueMask(depth, radius).Should().Be(expected);

    [Theory]
    [InlineData(1f, DepthRanges.Near)]
    [InlineData(1_999f, DepthRanges.Near)]
    [InlineData(2_000f, DepthRanges.Middle)]
    [InlineData(1_999_999f, DepthRanges.Middle)]
    [InlineData(2_000_000f, DepthRanges.Far)]
    public void TransparentObjectsUseTheRangeOfTheirCenter(float depth, int range) =>
        DepthRanges.Containing(depth).Should().Be(range);
}

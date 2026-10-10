using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

public sealed class DepthRangeTests
{
    private const int Far = 1 << DepthRanges.Far;
    private const int Middle = 1 << DepthRanges.Middle;
    private const int Near = 1 << DepthRanges.Near;

    private static float Depth(Matrix4x4 projection, float viewDepth)
    {
        Vector4 clip = Vector4.Transform(new Vector4(0, 0, -viewDepth, 1), projection);
        return clip.Z / clip.W;
    }

    [Fact]
    public void ProjectionIsReversedZ()
    {
        Matrix4x4 finite = DepthRanges.Projection(1f, 1.5f, 0.05f, 2_000f);
        Assert.Equal(1f, Depth(finite, 0.05f), 5);
        Assert.Equal(0f, Depth(finite, 2_000f), 5);
        Assert.True(Depth(finite, 1f) > Depth(finite, 10f));

        Matrix4x4 infinite = DepthRanges.Projection(1f, 1.5f, 2e6f, float.PositiveInfinity);
        Assert.Equal(1f, Depth(infinite, 2e6f), 5);
        Assert.InRange(Depth(infinite, 1.5e11f), 1e-6f, 1e-4f);
        Assert.True(Depth(infinite, 1e7f) > Depth(infinite, 1e8f));
    }

    [Fact]
    public void EachRangeIsCutOffAtItsFarBoundaryByTheDepthClearValue()
    {
        foreach (int range in (int[])[DepthRanges.Near, DepthRanges.Middle])
        {
            Matrix4x4 projection = DepthRanges.RangeProjection(range, 1f, 1.5f);
            float boundary = DepthRanges.FarOf(range);
            float clear = DepthRanges.ClearDepth(range);
            Assert.Equal(clear, Depth(projection, boundary), clear * 1e-5f);
            // Compare GREATER: just inside the boundary passes against the cleared depth, just beyond it fails.
            Assert.True(Depth(projection, boundary * 0.999f) > clear);
            Assert.True(Depth(projection, boundary * 1.001f) < clear);
            Assert.Equal(1f, Depth(projection, DepthRanges.NearOf(range)), 5);
        }
        Assert.Equal(0f, DepthRanges.ClearDepth(DepthRanges.Far));
    }

    [Fact]
    public void ProjectionFlipsYForVulkan()
    {
        Matrix4x4 projection = DepthRanges.Projection(MathF.PI / 2, 2f, 0.1f, 100f);
        // A point above the view axis lands in the upper half: negative y in Vulkan NDC.
        Vector4 up = Vector4.Transform(new Vector4(0, 1, -2, 1), projection);
        Vector4 right = Vector4.Transform(new Vector4(1, 0, -2, 1), projection);
        Assert.True(up.Y / up.W < 0);
        Assert.Equal(-0.5f, up.Y / up.W, 5);
        Assert.Equal(0.25f, right.X / right.W, 5);
    }

    [Fact]
    public void RangesOverlapAtTheirBoundaries()
    {
        Assert.True(DepthRanges.FarOf(DepthRanges.Near) > DepthRanges.NearOf(DepthRanges.Middle));
        Assert.True(DepthRanges.FarOf(DepthRanges.Middle) > DepthRanges.NearOf(DepthRanges.Far));
        Assert.Equal(DepthRanges.NearPlane, DepthRanges.NearOf(DepthRanges.Near));
        Assert.True(float.IsPositiveInfinity(DepthRanges.FarOf(DepthRanges.Far)));
    }

    [Theory]
    [InlineData(10f, 1f, Near)]
    [InlineData(1_995f, 4f, Near)]                   // fits in the near slab thanks to the overlap
    [InlineData(1_999f, 30f, Near | Middle)]           // straddles 2 km
    [InlineData(50_000f, 600f, Middle)]                // the station
    [InlineData(3.0e7f, 6.0e6f, Far)]                  // the planet
    [InlineData(1.0e6f, 1.5e6f, Near | Middle | Far)]  // reaches from the camera to beyond 2,000 km
    [InlineData(2.0e6f, 1.0e4f, Middle)]                // fits in the middle slab thanks to the overlap
    [InlineData(2.0e6f, 3.0e4f, Middle | Far)]         // straddles 2,000 km
    [InlineData(1.5e11f, 7.0e8f, Far)]                 // the star
    [InlineData(0.02f, 0.5f, Near)]                    // touches the camera
    public void OpaqueObjectsGoToEveryRangeTheyReach(float depth, float radius, int expected)
    {
        Assert.Equal(expected, DepthRanges.OpaqueMask(depth, radius));
    }

    [Theory]
    [InlineData(1f, DepthRanges.Near)]
    [InlineData(1_999f, DepthRanges.Near)]
    [InlineData(2_000f, DepthRanges.Middle)]
    [InlineData(1_999_999f, DepthRanges.Middle)]
    [InlineData(2_000_000f, DepthRanges.Far)]
    public void TransparentObjectsUseTheRangeOfTheirCenter(float depth, int range)
    {
        Assert.Equal(range, DepthRanges.Containing(depth));
    }
}

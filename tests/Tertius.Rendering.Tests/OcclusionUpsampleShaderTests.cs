using System.Numerics;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The occlusion upsample pass against its C# copy, <see cref="OcclusionUpsample"/>, at every pixel of an image whose
/// height is odd and whose half-resolution image has an even width: the last row of pixels has no row below it, and the
/// odd pixels of the last column read a texel beyond the half-resolution image, which gets no weight.
/// </summary>
public sealed class OcclusionUpsampleShaderTests(ITestOutputHelper output)
{
    private const int Width = 642;
    private const int Height = 361;

    /// <summary>The share of a weight's range within which the GPU's single floats and the copy's doubles can disagree.</summary>
    private const double WeightTolerance = 1e-3;

    [Fact]
    public void TheUpsampleWritesEveryPixelAsTheCSharpCopyDoes()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenSpaceLight: VisibilityBitmaskLight.Default,
            environmentMap: SceneEnvironmentMap.Default
        );
        try
        {
            // The faces draw the map in the first frame, so the open share S varies from texel to texel.
            renderer.Render(CornerWithABox(renderer), 1f);
            float[] estimate = renderer.ReadEstimate();
            float[] openShare = renderer.ReadEstimateOpenShare();
            (float[] term, int halfWidth, int halfHeight) = renderer.ReadOcclusionTerm();
            var input = new UpsampleInput(
                Distances: renderer.ReadDistance(),
                Normals: renderer.ReadNormals(),
                Term: term,
                TermOpenShare: renderer.ReadOcclusionTermOpenShare(),
                Depths: renderer.ReadOcclusionViewDepth().Depths,
                HalfSize: (halfWidth, halfHeight),
                Camera: renderer.LastScene.Camera
            );

            int compared = 0;
            int comparedOnTheEdges = 0;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (input.ExpectedAt(x, y) is not { } expected)
                    continue;
                int p = y * Width + x;
                ShouldHold(
                    expected: expected,
                    actual: [.. estimate.AsSpan(4 * p, 4), .. openShare.AsSpan(3 * p, 3)],
                    x: x,
                    y: y
                );
                compared++;
                if (x == Width - 1 || y == Height - 1)
                    comparedOnTheEdges++;
            }
            output.WriteLine($"compared {compared} pixels, {comparedOnTheEdges} in the last column or row");
            compared
                .Should()
                .BeGreaterThan(Width * Height / 2, "most pixels show the corner and lie off every edge of a weight");
            comparedOnTheEdges.Should().BeGreaterThan(Height / 2, "the last column and the last row show the corner");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Asserts that pixel (<paramref name="x"/>, <paramref name="y"/>) holds <paramref name="expected"/>:
    /// <paramref name="actual"/> holds the estimate target's G and V, then the pixel's S.
    /// </summary>
    private static void ShouldHold(AmbientEstimate expected, float[] actual, int x, int y)
    {
        double[] wanted =
        [
            expected.Bounce.X,
            expected.Bounce.Y,
            expected.Bounce.Z,
            expected.Occlusion,
            expected.OpenShare.X,
            expected.OpenShare.Y,
            expected.OpenShare.Z,
        ];
        for (int c = 0; c < wanted.Length; c++)
            ((double)actual[c])
                .Should()
                .BeApproximately(
                    wanted[c],
                    Math.Abs(wanted[c]) * 2e-3 + 1e-4,
                    "pixel ({0}, {1}) channel {2} must hold the C# copy's estimate",
                    x,
                    y,
                    c
                );
    }

    /// <summary>The matte corner under a white ambient light, with a box on the floor whose edges cut the weights.</summary>
    private static RenderSnapshot CornerWithABox(VulkanRenderer renderer)
    {
        MaterialId matte = Matte(renderer);
        RenderSnapshot scene = Corner(renderer, matte, UnderWhiteAmbientLight(CornerView));
        AddSlab(
            snapshot: scene,
            box: UnitBox(renderer),
            material: matte,
            center: new(0.5f, FloorHeight + 0.3f, -3),
            size: new(0.4f, 0.6f, 0.4f)
        );
        return scene;
    }

    /// <summary>
    /// The images that the upsample reads, read back from the GPU, and the frame's camera: term A's bounce and V in
    /// <paramref name="Term"/>, RGBA, and its S in <paramref name="TermOpenShare"/>, RGB.
    /// </summary>
    private sealed record UpsampleInput(
        float[] Distances,
        float[] Normals,
        float[] Term,
        float[] TermOpenShare,
        float[] Depths,
        (int Width, int Height) HalfSize,
        CameraView Camera
    )
    {
        /// <summary>
        /// The C# copy's estimate at pixel (<paramref name="x"/>, <paramref name="y"/>) from the GPU's inputs, or null for
        /// sky and for a pixel where the GPU's single floats and the copy's doubles can fall on either side of a weight's
        /// edge or of the choice of the nearest texel.
        /// </summary>
        public AmbientEstimate? ExpectedAt(int x, int y)
        {
            int p = y * Width + x;
            if (Distances[p] == 0)
                return null;
            Double3 position =
                (Double3)Vector3.Normalize(Camera.ViewRayThrough(new(x + 0.5f, y + 0.5f))) * Distances[p];
            Double3 normal = StoredGeometricNormal(Normals, p);
            double texelWidth = 4 * Camera.TanHalfX * Double3.Dot(position, Camera.Forward) / Width;
            double[] shares =
            [
                .. OcclusionUpsample
                    .BlockTexels(pixel: (x, y), halfSize: HalfSize)
                    .Select(texel =>
                        1 - Math.Abs(Double3.Dot(normal, TexelAt(texel.Coordinates).Position - position)) / texelWidth
                    ),
            ];
            if (shares.Any(share => Math.Abs(share) < WeightTolerance))
                return null;
            if (shares.Length > 1 && shares.All(share => share < 0) && NearestIsATie(shares))
                return null;
            return OcclusionUpsample.Estimate(
                pixel: (x, y),
                position: position,
                geometricNormal: normal,
                texelWidth: texelWidth,
                halfSize: HalfSize,
                texelAt: TexelAt
            );
        }

        /// <summary>The view position and the estimate of texel <paramref name="t"/>, relative to the eye in world axes.</summary>
        private UpsampleTexel TexelAt((int X, int Y) t)
        {
            int i = t.Y * HalfSize.Width + t.X;
            Vector3 ray = Camera.ViewRayThrough(new(2 * t.X + 0.5f, 2 * t.Y + 0.5f));
            return new(
                Position: (Double3)ray * Depths[i],
                Estimate: new(
                    Bounce: new(Term[4 * i], Term[4 * i + 1], Term[4 * i + 2]),
                    Occlusion: Term[4 * i + 3],
                    OpenShare: new(TermOpenShare[3 * i], TermOpenShare[3 * i + 1], TermOpenShare[3 * i + 2])
                )
            );
        }
    }

    /// <summary>Whether two of the texels lie within the tolerance of the same distance from the plane, when none has weight.</summary>
    private static bool NearestIsATie(double[] shares)
    {
        double[] ordered = [.. shares.OrderDescending()];
        return ordered[0] - ordered[1] < WeightTolerance;
    }

    /// <summary>The world geometric normal that the normals target stores at pixel <paramref name="p"/>: octDecode of its ba.</summary>
    private static Double3 StoredGeometricNormal(float[] normals, int p)
    {
        var e = new Double3(normals[4 * p + 2], normals[4 * p + 3], 0);
        Double3 n = e with { Z = 1 - Math.Abs(e.X) - Math.Abs(e.Y) };
        if (n.Z < 0)
            n = n with { X = (1 - Math.Abs(e.Y)) * (e.X >= 0 ? 1 : -1), Y = (1 - Math.Abs(e.X)) * (e.Y >= 0 ? 1 : -1) };
        return n.Normalized();
    }
}

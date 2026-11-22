using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The upscaled picture keeps the output's framing at every render size: two glowing markers, one near the centre and
/// one far from it, land at the same output pixels whatever the render size's shape (ter-qt72). Each draw is a headless
/// still through FSR 3.1 at a fixed render scale, the upscale path of a window under the dynamic render scale.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class OutputFramingRenderTests(ITestOutputHelper output)
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The marker near the centre and the one far from it, relative to the eye, in m.</summary>
    private static readonly Vector3 Near = new(0.2f, 0.1f, -10),
        Far = new(7, 3, -10);

    /// <summary>The render scale whose render size, 512 × 288, has the output's exact shape.</summary>
    private const float ExactScale = 0.8f;

    [Theory]
    [InlineData(0.9f)] // 576 × 320
    [InlineData(0.95f)] // 608 × 336
    [InlineData(2f / 3)] // 424 × 240
    public void TheMarkersLandAtTheSameOutputPixelsAsAtARenderSizeOfTheOutputsShape(float scale)
    {
        (Vector2 Near, Vector2 Far) exact = MarkerPlaces(ExactScale);

        (Vector2 Near, Vector2 Far) drawn = MarkerPlaces(scale);

        output.WriteLine(
            $"scale {scale}: near Δ = ({drawn.Near.X - exact.Near.X:F3}, {drawn.Near.Y - exact.Near.Y:F3}), "
                + $"far Δ = ({drawn.Far.X - exact.Far.X:F3}, {drawn.Far.Y - exact.Far.Y:F3}) output px"
        );
        // FSR rebuilds a marker's edges from another grid of samples at each render size, which scatters its centroid by
        // up to about 0.45 output px with no trend in the render size. A stretch moved the far marker by 1.3 to 3.8 px.
        Vector2.Distance(drawn.Near, exact.Near).Should().BeLessThan(0.6f, "the marker near the centre must not move sideways");
        Vector2.Distance(drawn.Far, exact.Far).Should().BeLessThan(0.6f, "the marker far from the centre must not stretch");
    }

    /// <summary>The centroids of the two markers on the output image of a still drawn at <paramref name="scale"/>.</summary>
    private static (Vector2 Near, Vector2 Far) MarkerPlaces(float scale)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(scale), 0),
            cascadedLights: 0
        );
        try
        {
            RenderSnapshot scene = ReflectionTesting.Dark();
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId glow = ReflectionTesting.Glow(renderer, new(0.6f));
            foreach (Vector3 marker in new[] { Near, Far })
                scene.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: glow,
                        position: marker,
                        scale: new(0.8f),
                        chainDepth: TestChains.CamerasFrame
                    )
                );
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: $"markers at scale {scale}");
            return (Centroid(image, Expected(Near)), Centroid(image, Expected(Far)));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The output pixel of a point relative to the eye, for the output's own projection.</summary>
    private static Vector2 Expected(Vector3 point)
    {
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * Width / Height;
        return new(
            (point.X / -point.Z / tanX + 1) * 0.5f * Width,
            (-point.Y / -point.Z / tanY + 1) * 0.5f * Height
        );
    }

    /// <summary>The brightness-weighted centroid of the red channel within 24 output pixels of <paramref name="around"/>.</summary>
    private static Vector2 Centroid(Capture image, Vector2 around)
    {
        double sum = 0,
            x = 0,
            y = 0;
        for (int row = (int)around.Y - 24; row <= (int)around.Y + 24; row++)
        for (int column = (int)around.X - 24; column <= (int)around.X + 24; column++)
        {
            double value = image.Rgba[4 * (row * image.Width + column)];
            sum += value;
            x += value * (column + 0.5);
            y += value * (row + 0.5);
        }
        sum.Should().BeGreaterThan(1000, "a marker must lie near {0}", around);
        return new((float)(x / sum), (float)(y / sum));
    }
}

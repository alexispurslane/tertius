using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The halo of a source follows the source's own light, also where the screen shows the same white core (ARCHITECTURE.md,
/// "Bloom", issue #3). Each test renders one square source twice under a fixed exposure of 0 stops, once dim and once
/// brighter by a known factor, and compares the light of the two halos outside the square. The sources are a lamp, which
/// emits, and two panels that reflect a star: a matte one and a glossy black one, whose light is only its specular
/// reflection, as a window's is.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BloomSourceLightTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The side of the square source, in output pixels.</summary>
    private const int Side = 24;

    /// <summary>The distance of the source and of the black wall behind it from the eye, in m.</summary>
    private const float Depth = 3;

    /// <summary>
    /// How many output pixels around the square the halo's light leaves out, so that no texel of level 0 that straddles
    /// the square's edge counts.
    /// </summary>
    private const int Margin = 2;

    /// <summary>The kinds of source.</summary>
    public enum Source
    {
        /// <summary>A black square that emits white light.</summary>
        Lamp,

        /// <summary>A rough white square with no specular reflection, lit by the star.</summary>
        MattePanel,

        /// <summary>A glossy black dielectric square, lit by the star: only its specular reflection shows.</summary>
        GlossyPanel,
    }

    [Theory]
    [InlineData(Source.Lamp, 10)]
    [InlineData(Source.MattePanel, 10)]
    [InlineData(Source.GlossyPanel, 10)]
    public void ABrighterSourceWithTheSameWhiteCoreGlowsByItsOwnLightAboveTheThreshold(Source source, float factor)
    {
        Bloom bloom = new(Threshold: 1, Knee: 0.5f, Strength: 0.25f, Levels: 6);
        VulkanRenderer renderer = Headless(bloom);
        try
        {
            (Capture dimImage, float[] dimHdr, float[] dimGlow) = Render(
                renderer: renderer,
                source: source,
                factor: 1,
                name: $"bloom-source-{source}-dim"
            );
            (Capture brightImage, _, float[] brightGlow) = Render(
                renderer: renderer,
                source: source,
                factor: factor,
                name: $"bloom-source-{source}-x{factor}"
            );

            float dimBrightness = CoreBrightness(dimHdr);
            dimBrightness.Should().BeInRange(20, 1000, "the dim source shows white and lies below every cap");
            dimImage.Luma(Width / 2, Height / 2).Should().BeGreaterThan(254 / 255f, "the dim source's core shows white");
            brightImage
                .Luma(Width / 2, Height / 2)
                .Should()
                .BeGreaterThan(254 / 255f, "the bright source's core shows the same white");

            double dim = BloomHalo.OutsideEnergy(
                level0: dimGlow,
                outputWidth: Width,
                outputHeight: Height,
                centerX: Width / 2,
                centerY: Height / 2,
                side: Side,
                margin: Margin
            );
            double bright = BloomHalo.OutsideEnergy(
                level0: brightGlow,
                outputWidth: Width,
                outputHeight: Height,
                centerX: Width / 2,
                centerY: Height / 2,
                side: Side,
                margin: Margin
            );

            (bright / dim)
                .Should()
                .BeApproximately(
                    ThresholdedRatio(dimHdr, bloom, factor),
                    0.03 * ThresholdedRatio(dimHdr, bloom, factor),
                    "the halo carries the light above the threshold of a source {0} times as bright",
                    factor
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A renderer under a fixed exposure of 0 stops, so the HDR image holds radiance as it is.</summary>
    private static VulkanRenderer Headless(BloomSetting bloom) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                Bloom = bloom,
                Exposure = new FixedExposure(0),
                Upscaling = new NoUpscaling(),
            }
        );

    /// <summary>
    /// The capture, the HDR image that the bloom read and the bloom's level 0 of one frame of <paramref name="source"/>,
    /// with its light <paramref name="factor"/> times the dim source's.
    /// </summary>
    private static (Capture Image, float[] Hdr, float[] Glow) Render(
        VulkanRenderer renderer,
        Source source,
        float factor,
        string name
    )
    {
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * Width / Height;
        float metersPerPixel = 2 * tanX * Depth / Width;
        MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        // The star stands behind the eye, so it lights the panels head-on and their specular peak faces the eye.
        RenderSnapshot snapshot = Gpu.Snapshot(
            Vector3.UnitZ,
            starIrradiance: source switch
            {
                Source.Lamp => 0,
                Source.MattePanel => 200 * factor,
                Source.GlossyPanel => 100 * factor,
            }
        );
        snapshot.Ambient.Intensity = 0;
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: Glow(renderer, Vector3.Zero),
                position: new(0, 0, -2 * Depth),
                scale: new(40),
                rotation: facing,
                radius: 30,
                chainDepth: TestChains.CamerasFrame
            )
        );
        MaterialId material = source switch
        {
            Source.Lamp => Glow(renderer, new(64 * factor)),
            Source.MattePanel => renderer.CreateMaterial(
                new(BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), Roughness: 1, Reflectance: 0)
            ),
            Source.GlossyPanel => renderer.CreateMaterial(
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 0.3f, Reflectance: 0.5f)
            ),
        };
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: material,
                position: new(0, 0, -Depth),
                scale: new(Side * metersPerPixel),
                rotation: facing,
                radius: Side * metersPerPixel,
                chainDepth: TestChains.CamerasFrame
            )
        );
        Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
        return (image, renderer.ReadPostHdr(), renderer.ReadGlowLevel(0));
    }

    /// <summary>The brightness, the largest channel, of the HDR image at the center of the source.</summary>
    private static float CoreBrightness(float[] hdr)
    {
        int i = 4 * (Height / 2 * Width + Width / 2);
        return MathF.Max(hdr[i], MathF.Max(hdr[i + 1], hdr[i + 2]));
    }

    /// <summary>
    /// How many times the thresholded light of the source's pixels grows when every pixel of <paramref name="dimHdr"/>
    /// grows by <paramref name="factor"/>: the ratio of the two sums of Rec. 709 luminance after the soft threshold
    /// (BloomChainModel). The filters of the bloom are linear, so the halos outside the source grow by the same ratio.
    /// </summary>
    private static double ThresholdedRatio(float[] dimHdr, Bloom bloom, float factor)
    {
        double dim = 0,
            bright = 0;
        for (int y = Height / 2 - Side / 2; y < Height / 2 + Side / 2; y++)
            for (int x = Width / 2 - Side / 2; x < Width / 2 + Side / 2; x++)
            {
                int i = 4 * (y * Width + x);
                var color = new Vector3(dimHdr[i], dimHdr[i + 1], dimHdr[i + 2]);
                dim += Luminance(BloomChainModel.Thresholded(color, bloom));
                bright += Luminance(BloomChainModel.Thresholded(factor * color, bloom));
            }

        return bright / dim;
    }

    private static double Luminance(Vector3 c) => 0.2126 * c.X + 0.7152 * c.Y + 0.0722 * c.Z;
}

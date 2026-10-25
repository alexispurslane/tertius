using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The bloom on the GPU (ARCHITECTURE.md, "Bloom"): emissive squares of an exact size in pixels, in front of a black wall
/// that no light reaches, so that every lit pixel off a square is glow.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BloomTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The distance of the squares from the eye, in m.</summary>
    private const float Depth = 3;

    /// <summary>The bloom of the defaults, written out, so that a test can change one value of it.</summary>
    private static Bloom Shaped(float strength = 0.25f, int levels = 6, float threshold = 1, float knee = 0.5f) =>
        new(threshold: threshold, knee: knee, strength: strength, levels: levels);

    private static VulkanRenderer Headless(BloomSetting bloom) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                Bloom = bloom,
            }
        );

    /// <summary>The image of <paramref name="squares"/> in front of a black wall.</summary>
    private static Capture Render(VulkanRenderer renderer, string name, params Square[] squares)
    {
        (int width, int height) = renderer.OutputSize;
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * width / height;
        float metersPerPixel = 2 * tanX * Depth / width;
        MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, sunIntensity: 0);
        snapshot.Ambient.Intensity = 0;
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: Glow(renderer, Vector3.Zero),
                position: new(0, 0, -2 * Depth),
                scale: new(40),
                rotation: facing,
                radius: 30
            )
        );
        foreach (Square square in squares)
        {
            var center = new Vector3(
                x: (2 * square.X / width - 1) * tanX * Depth,
                y: (1 - 2 * square.Y / height) * tanY * Depth,
                z: -Depth
            );
            MaterialId material =
                square.Opacity < 1
                    ? renderer.CreateMaterial(
                        new(
                            BaseColor: new(x: 0, y: 0, z: 0, w: square.Opacity),
                            Roughness: 0.5f,
                            Reflectance: 0,
                            Emissive: square.Radiance
                        )
                    )
                    : Glow(renderer, square.Radiance);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: plane,
                    material: material,
                    position: center,
                    scale: new(square.Side * metersPerPixel),
                    rotation: facing,
                    flags: square.Opacity < 1 ? RenderFlags.Transparent : RenderFlags.None,
                    radius: square.Side * metersPerPixel
                )
            );
        }

        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
    }

    /// <summary>A white square that emits <paramref name="radiance"/>, <paramref name="side"/> px across, centered on the image.</summary>
    private static Square Centered(int side, float radiance) =>
        new(X: Width / 2, Y: Height / 2, Side: side, Radiance: new(radiance));

    /// <summary>The luma of the pixel <paramref name="distance"/> px to the right of a centered square's right edge.</summary>
    private static float RightOf(Capture image, int side, int distance) =>
        image.Luma(Width / 2 + side / 2 + distance, Height / 2);

    [Fact]
    public void ByDefaultABrightSquareGlowsWithAGlowThatFadesWithDistance()
    {
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
            }
        );
        try
        {
            Capture image = Render(renderer, "bloom-default", Centered(side: 16, radiance: 20));

            float[] glow = [RightOf(image, 16, 2), RightOf(image, 16, 8), RightOf(image, 16, 24)];

            glow[0]
                .Should()
                .BeGreaterThan(0.1f, "the default bloom lights the black wall right beside a bright square");
            glow.Should().BeInDescendingOrder("the glow fades with the distance from the square");
            glow.Should().OnlyHaveUniqueItems("the glow fades with the distance from the square");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void LightBelowTheKneeAddsNoGlow()
    {
        Square[] dim =
        [
            new(X: 200, Y: 180, Side: 40, Radiance: new(0.45f)),
            new(X: 440, Y: 180, Side: 40, Radiance: new(0.3f, 0.45f, 0.1f)),
        ];
        VulkanRenderer withBloom = Headless(Shaped(strength: 4));
        VulkanRenderer without = Headless(new NoBloom());
        try
        {
            Capture bloomed = Render(withBloom, "bloom-below-knee", dim);
            Capture plain = Render(without, "bloom-below-knee-off", dim);

            bloomed
                .Rgba.Should()
                .Equal(plain.Rgba, "no channel reaches the knee, which starts at half the threshold, so nothing glows");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(withBloom);
            Gpu.DisposeAndAssertClean(without);
        }
    }

    [Fact]
    public void LightInTheKneeBelowTheThresholdGlowsALittle()
    {
        VulkanRenderer renderer = Headless(Shaped(strength: 2));
        try
        {
            Capture image = Render(renderer, "bloom-knee", Centered(side: 32, radiance: 0.9f));

            RightOf(image, 32, 3)
                .Should()
                .BePositive("light between the knee's start at 0.5 and the threshold of 1 glows, so the glow fades in");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheThresholdAppliesToTheLightAfterTheExposure()
    {
        VulkanRenderer renderer = Headless(Shaped());
        try
        {
            renderer.Exposure = 4;

            Capture image = Render(renderer, "bloom-exposure", Centered(side: 16, radiance: 0.5f));

            RightOf(image, 16, 3)
                .Should()
                .BePositive("a radiance of 0.5 under the exposure 4 shows as 2, twice the threshold, so it glows");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASourceOfAFewPixelsGlowsByItsFullBrightness()
    {
        // With no threshold and no knee, the glow is linear in the light, so two sources of one total energy glow alike
        // far from both. A weight of the bright pixels, such as Karis's, would dim the small and bright one.
        VulkanRenderer renderer = Headless(Shaped(strength: 8, threshold: 0, knee: 0));
        try
        {
            Capture small = Render(renderer, "bloom-small-source", Centered(side: 2, radiance: 64));
            Capture large = Render(renderer, "bloom-large-source", Centered(side: 8, radiance: 4));

            float smallGlow = small.Luma(Width / 2 + 40, Height / 2),
                largeGlow = large.Luma(Width / 2 + 40, Height / 2);

            largeGlow
                .Should()
                .BeGreaterThan(20 / 255f, "the glow 40 px from the sources must be large enough to compare");
            smallGlow
                .Should()
                .BeApproximately(
                    largeGlow,
                    3 / 255f,
                    "4 px of radiance 64 hold the energy of 64 px of radiance 4, and glow by all of it"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASourceOfOnePixelGlowsByItsOwnLightAboveTheThreshold()
    {
        // The threshold applies to each pixel before any averaging. One pixel of 4 holds as much light above the threshold
        // of 1 as four pixels of 1.75, so the two glow alike. A threshold on averages of four pixels would see the single
        // pixel as 1, in the knee, and its glow would nearly vanish.
        VulkanRenderer renderer = Headless(Shaped(strength: 40));
        try
        {
            Capture single = Render(
                renderer,
                "bloom-one-pixel",
                new Square(X: Width / 2 + 0.5f, Y: Height / 2 + 0.5f, Side: 1, Radiance: new(4))
            );
            Capture block = Render(renderer, "bloom-four-pixels", Centered(side: 2, radiance: 1.75f));

            float singleGlow = single.Luma(Width / 2 + 8, Height / 2),
                blockGlow = block.Luma(Width / 2 + 8, Height / 2);

            blockGlow
                .Should()
                .BeGreaterThan(20 / 255f, "the glow 8 px from the sources must be large enough to compare");
            singleGlow
                .Should()
                .BeApproximately(blockGlow, 4 / 255f, "both sources hold a light of 3 above the threshold");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithNoBloomABrightSquareEndsAtItsEdge()
    {
        VulkanRenderer renderer = Headless(new NoBloom());
        try
        {
            Capture image = Render(renderer, "bloom-off", Centered(side: 16, radiance: 20));

            image
                .Color(Width / 2 + 8, Height / 2)
                .Should()
                .Be(Vector3.Zero, "without bloom the black wall next to the square stays black");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void MoreLevelsSpreadTheGlowFarther()
    {
        VulkanRenderer two = Headless(Shaped(strength: 2, levels: 2));
        VulkanRenderer six = Headless(Shaped(strength: 2, levels: 6));
        try
        {
            float nearOfTwo = RightOf(Render(two, "bloom-two-levels", Centered(side: 16, radiance: 20)), 16, 60);
            float nearOfSix = RightOf(Render(six, "bloom-six-levels", Centered(side: 16, radiance: 20)), 16, 60);

            nearOfSix.Should().BeGreaterThan(nearOfTwo, "the coarser levels carry the glow farther from the square");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(two);
            Gpu.DisposeAndAssertClean(six);
        }
    }

    [Fact]
    public void AStrongerBloomGlowsBrighter()
    {
        VulkanRenderer weak = Headless(Shaped(strength: 0.25f));
        VulkanRenderer strong = Headless(Shaped(strength: 1));
        try
        {
            float weakGlow = RightOf(Render(weak, "bloom-weak", Centered(side: 16, radiance: 20)), 16, 6);
            float strongGlow = RightOf(Render(strong, "bloom-strong", Centered(side: 16, radiance: 20)), 16, 6);

            strongGlow.Should().BeGreaterThan(weakGlow, "the strength scales the glow");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(weak);
            Gpu.DisposeAndAssertClean(strong);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void ALargeSquareGlowsOntoItsMiddleByTheStrengthTimesItsLightWhateverTheLevels(int levels)
    {
        // With no threshold and no knee, all light glows. The middle of a square 200 px across lies farther inside it
        // than any level of 4 or fewer reaches, so each level holds the square's light there.
        VulkanRenderer bloomed = Headless(Shaped(strength: 1, levels: levels, threshold: 0, knee: 0));
        VulkanRenderer plain = Headless(new NoBloom());
        try
        {
            Vector3 middle = Render(bloomed, "bloom-large-middle", Centered(side: 200, radiance: 0.25f))
                .Color(Width / 2, Height / 2);
            Vector3 doubled = Render(plain, "bloom-large-doubled", Centered(side: 200, radiance: 0.5f))
                .Color(Width / 2, Height / 2);

            LargestChannelDifference(middle, doubled)
                .Should()
                .BeLessThanOrEqualTo(
                    1 / 255f,
                    "a strength of 1 adds the square's own light once, over all {0} levels",
                    levels
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(bloomed);
            Gpu.DisposeAndAssertClean(plain);
        }
    }

    [Fact]
    public void ABrightTransparentSquareGlows()
    {
        VulkanRenderer renderer = Headless(Shaped());
        try
        {
            var pane = new Square(X: Width / 2, Y: Height / 2, Side: 16, Radiance: new(40), Opacity: 0.5f);

            Capture image = Render(renderer, "bloom-transparent", pane);

            RightOf(image, 16, 3)
                .Should()
                .BePositive("the bloom reads the HDR image after the transparent pass, which draws the pane at 20");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AfterAResizeABrightSquareStillGlows()
    {
        VulkanRenderer renderer = Headless(Shaped());
        try
        {
            Render(renderer, "bloom-before-resize", Centered(side: 16, radiance: 20));
            renderer.Resize(Width / 2, Height / 2);
            // The next frame applies the new size, and the scene below is placed by the output's size.
            renderer.Render(Gpu.Snapshot(Vector3.UnitZ, sunIntensity: 0), 1f);

            Capture image = Render(
                renderer,
                "bloom-after-resize",
                new Square(X: Width / 4, Y: Height / 4, Side: 8, Radiance: new(20))
            );

            image
                .Luma(Width / 4 + 4 + 2, Height / 4)
                .Should()
                .BeGreaterThan(0.1f, "the bloom images follow the new size");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheGpuTimingsTimeTheBloomAsOnePassBetweenTheTransparentPassAndTheToneMap()
    {
        VulkanRenderer renderer = Headless(Shaped());
        try
        {
            if (!renderer.TimesPasses)
                Assert.Skip($"{renderer.DeviceName} writes no timestamps on its graphics queue.");
            for (int i = 0; i < 3; i++)
                Render(renderer, "bloom-timed", Centered(side: 16, radiance: 20));

            string[] passes = [.. renderer.LastFrameStats.GpuTimings!.Value.Passes.ToArray().Select(pass => pass.Pass)];

            int bloom = Array.IndexOf(passes, "bloom");
            bloom.Should().BePositive("the bloom is one timed pass");
            passes[bloom - 1].Should().Be("transparent", "the bloom reads the HDR image after its last writer");
            passes[bloom + 1].Should().Be("tone map", "the tone map adds the glow");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A square that emits <see cref="Radiance"/> and reflects nothing, <see cref="Side"/> px across, centered on the point
    /// (<see cref="X"/>, <see cref="Y"/>) of the image, in pixels. An <see cref="Opacity"/> below 1 makes it a transparent
    /// pane. A square of an even side centered on a pixel corner, or of an odd side on a pixel center, covers exactly its
    /// pixels.
    /// </summary>
    private readonly record struct Square(float X, float Y, int Side, Vector3 Radiance, float Opacity = 1);
}

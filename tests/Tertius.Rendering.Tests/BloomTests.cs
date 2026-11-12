using System.Numerics;
using Tertius.Testing;
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
        new(Threshold: threshold, Knee: knee, Strength: strength, Levels: levels);

    /// <summary>A renderer under <see cref="FixedExposure"/> at 0 stops, so the HDR image holds the light as it is.</summary>
    private static VulkanRenderer Headless(BloomSetting bloom, int width = Width, int height = Height) =>
        Headless(bloom: bloom, exposure: new FixedExposure(0), width: width, height: height);

    private static VulkanRenderer Headless(
        BloomSetting bloom,
        ExposureSetting exposure,
        int width = Width,
        int height = Height
    ) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = width,
                Height = height,
                Impostors = false,
                Bloom = bloom,
                Exposure = exposure,
                Upscaling = new NoUpscaling(),
            }
        );

    /// <summary>The image of <paramref name="squares"/> in front of a black wall.</summary>
    private static Capture Render(VulkanRenderer renderer, string name, params Square[] squares) =>
        Gpu.RenderAndCapture(renderer: renderer, snapshot: Scene(renderer, squares), name: name);

    /// <summary>The snapshot of <paramref name="squares"/> in front of a black wall, for a test that adds to it.</summary>
    private static RenderSnapshot Scene(VulkanRenderer renderer, params Square[] squares)
    {
        (int width, int height) = renderer.OutputSize;
        float tanY = MathF.Tan(Gpu.Fov / 2);
        float tanX = tanY * width / height;
        float metersPerPixel = 2 * tanX * Depth / width;
        MeshId plane = renderer.CreateMesh(Primitives.Plane(0.5f));
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
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
                            Emissive: square.Radiance,
                            Alpha: new GlassAlpha()
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
                    radius: square.Side * metersPerPixel,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        }

        return snapshot;
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
        // The default bloom at 0 stops: under the automatic exposure, the black wall would raise the exposure until
        // the whole image shows white.
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = Width,
                Height = Height,
                Impostors = false,
                Exposure = new FixedExposure(0),
                Upscaling = new NoUpscaling(),
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
    public void LightBelowTheKneeAfterTheExposureAddsNoGlow()
    {
        Square[] dim = [Centered(side: 40, radiance: 0.12f)];
        VulkanRenderer withBloom = Headless(bloom: Shaped(strength: 4), exposure: new FixedExposure(2));
        VulkanRenderer without = Headless(bloom: new NoBloom(), exposure: new FixedExposure(2));
        try
        {
            Capture bloomed = Render(withBloom, "bloom-exposed-below-knee", dim);
            Capture plain = Render(without, "bloom-exposed-below-knee-off", dim);

            bloomed
                .Rgba.Should()
                .Equal(
                    plain.Rgba,
                    "a radiance of 0.12 under the exposure 4 shows as 0.48, below the knee's start at 0.5"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(withBloom);
            Gpu.DisposeAndAssertClean(without);
        }
    }

    [Fact]
    public void LightAtTheThresholdGlowsByAQuarterOfTheKneesWidth()
    {
        // Unity's quadratic knee gives light at the threshold T the glow T × Knee / 4: here 0.25 × 0.5 / 4 = 0.03125,
        // which a strength of 4 adds as 0.125 onto the middle of a square too large for any of the 4 levels to reach past.
        VulkanRenderer bloomed = Headless(Shaped(strength: 4, levels: 4, threshold: 0.25f, knee: 0.5f));
        VulkanRenderer plain = Headless(new NoBloom());
        try
        {
            Vector3 middle = Render(bloomed, "bloom-knee-at-threshold", Centered(side: 200, radiance: 0.25f))
                .Color(Width / 2, Height / 2);
            Vector3 expected = Render(plain, "bloom-knee-at-threshold-off", Centered(side: 200, radiance: 0.375f))
                .Color(Width / 2, Height / 2);

            LargestChannelDifference(middle, expected)
                .Should()
                .BeLessThanOrEqualTo(1 / 255f, "the knee's glow at the threshold is a quarter of the knee's width");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(bloomed);
            Gpu.DisposeAndAssertClean(plain);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheThresholdAppliesToTheLightAfterTheExposure(bool automatic)
    {
        // A radiance of 0.5 lies at the knee's start, so it glows only when the exposure brightens it. Under the
        // automatic exposure, the black wall fills the view, so the exposure rises to its highest stops.
        ExposureSetting exposure = automatic ? AutoExposure.Default : new FixedExposure(2);
        VulkanRenderer renderer = Headless(bloom: Shaped(), exposure: exposure);
        try
        {
            Capture image = Render(renderer, "bloom-exposure", Centered(side: 16, radiance: 0.5f));

            RightOf(image, 16, 3)
                .Should()
                .BePositive(
                    "a radiance of 0.5 under an exposure of 2 stops or more shows as 2 or more, above the threshold"
                );
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

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void ALargeSquareAtTheCapGlowsByItsLightWithoutOverflowing(int levels)
    {
        // At 0 stops the HDR image holds the square's light at the surfaces' cap, 32,256, and so does each level inside
        // the square, so a sum of 3 or more levels as they are overflows to infinity. The glow is linear in the light,
        // so beside the square it equals the glow of a square 1,024 times dimmer under a bloom 1,024 times stronger. The
        // strength brings that glow into the range of the tone curve, as an exposure would before pre-exposure.
        const float dimmer = 1024;
        const int side = 200;
        float largest = ShaderInput.MaxRadiance,
            strength = 1f / 65536;
        VulkanRenderer bright = Headless(Shaped(strength: strength, levels: levels, threshold: 0, knee: 0));
        VulkanRenderer dim = Headless(Shaped(strength: strength * dimmer, levels: levels, threshold: 0, knee: 0));
        try
        {
            Capture brightImage = Render(bright, "bloom-largest", Centered(side: side, radiance: largest));
            Capture dimImage = Render(dim, "bloom-largest-dimmer", Centered(side: side, radiance: largest / dimmer));
            Vector3 glow = brightImage.Color(Width / 2 + side / 2 + 4, Height / 2);
            Vector3 expected = dimImage.Color(Width / 2 + side / 2 + 4, Height / 2);

            expected.X.Should().BeInRange(0.1f, 0.9f, "the glow beside the square lies inside the tone curve's range");
            LargestChannelDifference(glow, expected)
                .Should()
                .BeLessThanOrEqualTo(
                    1 / 255f,
                    "the {0} levels hold the glow of light at the cap without overflowing",
                    levels
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(bright);
            Gpu.DisposeAndAssertClean(dim);
        }
    }

    [Fact]
    public void AtAnOddSizeTheGlowIsCenteredOnItsSource()
    {
        // At 641x361 every level has an odd size. The square's center, (320, 192), is a pixel corner that halves to a
        // texel corner in each of the 6 levels, so the glow of a chain whose levels line up is the same on opposite sides.
        const int width = 641,
            height = 361,
            x = 320,
            y = 192,
            half = 8;
        VulkanRenderer renderer = Headless(Shaped(strength: 2), width: width, height: height);
        try
        {
            Capture image = Render(
                renderer,
                "bloom-odd-size",
                new Square(X: x, Y: y, Side: 2 * half, Radiance: new(20))
            );

            foreach (int distance in new[] { 3, 12, 30 })
            {
                float left = image.Luma(x - half - 1 - distance, y),
                    right = image.Luma(x + half + distance, y),
                    above = image.Luma(x, y - half - 1 - distance),
                    below = image.Luma(x, y + half + distance);

                right
                    .Should()
                    .BeGreaterThan(
                        2 / 255f,
                        "the glow {0} px from the square must be large enough to compare",
                        distance
                    );
                left.Should()
                    .BeApproximately(right, 1 / 255f, "the glow {0} px left and right of the square", distance);
                above.Should().BeApproximately(below, 1 / 255f, "the glow {0} px above and below the square", distance);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
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
            renderer.Render(Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0), 1f);

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

    [Theory]
    [InlineData(640, 360)]
    [InlineData(637, 355)]
    public void EveryBloomLevelHoldsTheDesignsChainOverTheHdrImage(int width, int height)
    {
        // One square touches the top edge, so its taps read past the edge, and the two squares differ in color. At
        // 637 x 355 the first three levels have an odd width, and five have an odd height.
        Bloom bloom = Shaped();
        VulkanRenderer renderer = Headless(bloom, width: width, height: height);
        try
        {
            Render(
                renderer,
                "bloom-chain",
                new Square(X: 9, Y: 7, Side: 14, Radiance: new(x: 30, y: 4, z: 1)),
                new Square(X: width / 2 + 40, Y: height / 2, Side: 24, Radiance: new(x: 2, y: 6, z: 12))
            );
            BloomChainModel.Level hdr = Rgb(renderer.ReadPostHdr(), width: width, height: height);

            BloomChainModel.Level[] expected = BloomChainModel.Levels(hdr, bloom);

            for (int i = 0; i < expected.Length; i++)
            {
                BloomChainModel.Level actual = Rgb(
                    renderer.ReadGlowLevel(i),
                    width: expected[i].Width,
                    height: expected[i].Height
                );
                float largest = expected[i].Pixels.Max(pixel => MathF.Max(pixel.X, MathF.Max(pixel.Y, pixel.Z)));
                float worst = expected[i]
                    .Pixels.Zip(actual.Pixels, (model, gpu) => Vector3.Abs(model - gpu))
                    .Max(error => MathF.Max(error.X, MathF.Max(error.Y, error.Z)));

                largest.Should().BeGreaterThan(0.01f, "level {0} must hold light to compare", i);
                worst
                    .Should()
                    .BeLessThan(ChainTolerance * largest, "level {0} holds the chain's light at every pixel", i);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The largest difference between a level of the GPU and of <see cref="BloomChainModel"/>, as a share of the level's
    /// brightest channel. The two differ by the order of their sums and by the rounding of each level's half floats.
    /// </summary>
    private const float ChainTolerance = 0.01f;

    /// <summary>The RGB of <paramref name="rgba"/>, an image of <paramref name="width"/> × <paramref name="height"/> pixels with four channels.</summary>
    private static BloomChainModel.Level Rgb(float[] rgba, int width, int height)
    {
        rgba.Should().HaveCount(4 * width * height);
        var pixels = new Vector3[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new(x: rgba[4 * i], y: rgba[4 * i + 1], z: rgba[4 * i + 2]);
        return new(Width: width, Height: height, Pixels: pixels);
    }

    /// <summary>
    /// A square that emits <see cref="Radiance"/> and reflects nothing, <see cref="Side"/> px across, centered on the point
    /// (<see cref="X"/>, <see cref="Y"/>) of the image, in pixels. An <see cref="Opacity"/> below 1 makes it a transparent
    /// pane. A square of an even side centered on a pixel corner, or of an odd side on a pixel center, covers exactly its
    /// pixels.
    /// </summary>
    private readonly record struct Square(float X, float Y, int Side, Vector3 Radiance, float Opacity = 1);
}

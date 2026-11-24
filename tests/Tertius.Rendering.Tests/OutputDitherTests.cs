using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The output's dither (ARCHITECTURE.md, "Tone mapping"): the tone map pass adds a triangular offset of at most one code
/// to each encoded color before the 8-bit target rounds it, so that a smooth dark gradient shows no contour steps and a
/// region's mean code is the unrounded value. Each test runs on the headless sRGB target, which takes the shader's
/// linear value and encodes it, and on a UNORM target, which takes the shader's own encoding. Bloom, the screen tracer,
/// the environment map and the ambient light are off, so a pixel holds only its emission or its lamp's direct light.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class OutputDitherTests
{
    private const int Width = 640;
    private const int Height = 360;

    /// <summary>
    /// The longest run of one code that the dither may leave along a row of the dark ramp. The triangular dither keeps a
    /// pixel at a whole code with a chance of 3/4, so runs of 20 to 30 pixels remain where the ramp sits on a whole code:
    /// 22 and 26 on the Mac's GPU, of a median of 1. Without the dither the ramp shows runs of about 300 pixels.
    /// </summary>
    private const int LongestDitheredRun = 40;

    /// <summary>The two kinds of 8-bit output: the sRGB format that encodes on its write, and UNORM, which the shader encodes for.</summary>
    public enum Target
    {
        Srgb,
        Unorm,
    }

    /// <summary>
    /// A renderer of <paramref name="target"/>. With the dither on, it keeps the renderer's default, so that these tests
    /// also pin that the default dithers.
    /// </summary>
    private static VulkanRenderer Renderer(Target target, bool dither = true)
    {
        var options = new RendererOptions
        {
            Validation = true,
            Width = Width,
            Height = Height,
            Impostors = false,
            Exposure = new FixedExposure(0),
            Bloom = new NoBloom(),
            Upscaling = new NoUpscaling(),
            EnvironmentMap = new NoEnvironmentMap(),
            ScreenTrace = default,
            LocalShadows = default,
            LightPairs = default,
            Subsurface = default,
        };
        if (!dither)
            options.OutputDither = false;
        return target == Target.Srgb
            ? VulkanRenderer.CreateHeadless(options)
            : VulkanRenderer.CreateOffscreen(
                options,
                (device, allocator, extent) => new UnormOutput(device, allocator, extent)
            );
    }

    /// <summary>A snapshot with no star and no ambient light: only emission and the lamps that a test adds light a pixel.</summary>
    private static RenderSnapshot DarkSnapshot()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>A wall 4 m ahead that fills the view, facing the camera.</summary>
    private static RenderInstance Wall(VulkanRenderer renderer, MaterialDesc material) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Plane(20f)),
            material: renderer.CreateMaterial(material),
            position: new(0, 0, -4),
            scale: Vector3.One,
            rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
            radius: 29f,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// A black wall that emits <paramref name="radiance"/> in each channel and fills the view: every pixel shows the same
    /// unrounded value.
    /// </summary>
    private static Capture FlatView(VulkanRenderer renderer, float radiance)
    {
        RenderSnapshot snapshot = DarkSnapshot();
        snapshot.Instances.Add(
            Wall(
                renderer,
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 1, Reflectance: 0, Emissive: new(radiance))
            )
        );
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "flat view");
    }

    /// <summary>
    /// A grey matte wall that fills the view, lit by one lamp 16 m in front of its center: the shown value falls by about
    /// 9 % of the radiance from the center to the left and right edges, a dark ramp of a few codes across 320 pixels.
    /// </summary>
    private static Capture DarkRamp(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = DarkSnapshot();
        snapshot.Instances.Add(
            Wall(renderer, new(BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), Roughness: 1, Reflectance: 0))
        );
        snapshot.Lights.Add(
            Gpu.Lamp(position: new(0, 0, 12), color: Vector3.One, intensity: 120, chainDepth: TestChains.CamerasFrame)
        );
        return Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "dark ramp");
    }

    /// <summary>The green code of a pixel, 0 to 255.</summary>
    private static int Code(Capture image, int x, int y) => image.Rgba[(y * image.Width + x) * 4 + 1];

    /// <summary>The longest run of one green code along row <paramref name="y"/>, from column <paramref name="from"/> to before <paramref name="to"/>.</summary>
    private static int LongestRun(Capture image, int y, int from, int to)
    {
        int best = 0,
            run = 0,
            last = -1;
        for (int x = from; x < to; x++)
        {
            int code = Code(image, x, y);
            run = code == last ? run + 1 : 1;
            last = code;
            best = Math.Max(best, run);
        }

        return best;
    }

    /// <summary>The mean green code of the pixels in a rectangle.</summary>
    private static double MeanCode(Capture image, int x0, int y0, int width, int height)
    {
        long sum = 0;
        for (int y = y0; y < y0 + height; y++)
        for (int x = x0; x < x0 + width; x++)
            sum += Code(image, x, y);
        return (double)sum / (width * height);
    }

    // A dark ramp without the dither shows one code over runs as long as 1 / slope, about 90 pixels here: a contour step.
    // The dither changes a pixel's code by its own offset, so runs of one code stay short wherever the ramp goes.
    [Theory]
    [InlineData(Target.Srgb)]
    [InlineData(Target.Unorm)]
    public void ADarkRampShowsNoLongRunOfOneCode(Target target)
    {
        VulkanRenderer renderer = Renderer(target);
        Capture image = DarkRamp(renderer);
        Gpu.DisposeAndAssertClean(renderer);

        double center = MeanCode(image: image, x0: Width / 2 - 16, y0: Height / 2 - 16, width: 32, height: 32);
        double edge = MeanCode(image: image, x0: 0, y0: Height / 2 - 16, width: 32, height: 32);
        center.Should().BeInRange(20, 60, "the ramp must be dark, where the sRGB steps are widest in light");
        (center - edge)
            .Should()
            .BeGreaterThan(2.5, "the ramp must cross several codes between the edge and the center");

        int longest = 0;
        for (int y = Height / 2 - 40; y < Height / 2 + 40; y += 4)
            longest = Math.Max(longest, LongestRun(image: image, y: y, from: 8, to: Width - 8));
        longest.Should().BeLessThanOrEqualTo(LongestDitheredRun, "a dithered ramp shows no band of one code");
    }

    // The bar of the contour step: across the ramp, the mean code of a window of 8 columns by 80 rows follows its two
    // neighbours to within 0.1 code. Without the dither, a window beside a step of one code sits up to half a code off the
    // line through its neighbours. With it, the mean code is the unrounded value, which the smooth light makes a line.
    [Theory]
    [InlineData(Target.Srgb)]
    [InlineData(Target.Unorm)]
    public void TheMeanCodeAcrossADarkRampHasNoStep(Target target)
    {
        VulkanRenderer renderer = Renderer(target);
        Capture image = DarkRamp(renderer);
        Gpu.DisposeAndAssertClean(renderer);

        const int window = 8;
        var means = new List<double>();
        for (int x = 8; x + window <= Width - 8; x += window)
            means.Add(MeanCode(image: image, x0: x, y0: Height / 2 - 40, width: window, height: 80));
        for (int i = 1; i < means.Count - 1; i++)
            (means[i] - (means[i - 1] + means[i + 1]) / 2)
                .Should()
                .BeInRange(-0.1, 0.1, "the window at column {0} lies on the ramp's line, with no step", 8 + i * window);
    }

    // An emission of 1/16, exact in half floats, shows 38.571 codes through ACES fitted and the sRGB curve. Without the
    // dither every pixel shows one code, 0.43 or 0.57 codes off. With it, the mean over 256 × 256 pixels is the unrounded
    // value, to within the target's own conversion: the UNORM target takes the shader's encoding, and the sRGB target
    // converts in hardware or the driver, whose linear-to-sRGB transfer approximation the Vulkan specification leaves
    // bounded only loosely (framebuffer.adoc, fundamentals.adoc). Apple M1 Max converts 38.571 codes about 0.07 code low
    // (dithered mean 38.511). Mesa 26.2.4 llvmpipe (lp_bld_format_srgb.c) uses a rational-polynomial approximation with up
    // to +/-0.17 code error across the curve (+0.112 at 38.571, dithered mean 38.6825), so sRGB allows 0.2 code while still
    // rejecting an undithered target (0.43 to 0.57 code off).
    [Theory]
    [InlineData(Target.Srgb)]
    [InlineData(Target.Unorm)]
    public void TheMeanCodeOfAFlatValueBetweenTwoCodesIsTheUnroundedValue(Target target)
    {
        const float radiance = 1 / 16f;
        double expected = 255 * ToneCurveTests.Encoded(ToneCurveTests.AcesFitted(new(radiance)).Y);
        VulkanRenderer renderer = Renderer(target);
        Capture image = FlatView(renderer, radiance);
        Gpu.DisposeAndAssertClean(renderer);

        double mean = MeanCode(image: image, x0: Width / 2 - 128, y0: Height / 2 - 128, width: 256, height: 256);
        mean.Should()
            .BeApproximately(
                expected,
                target == Target.Unorm ? 0.03 : 0.2,
                "the dither's offsets average to 0, so the rounding loses nothing on average"
            );
    }

    // The dither moves a value by at most one code before the rounding: a flat value of 38.571 codes shows 38, 39 or 40
    // at every pixel. The UNORM target takes the shader's own encoding, so its codes hold no conversion error of the
    // sRGB target's hardware.
    [Fact]
    public void ADitheredFlatValueStaysWithinOneCodeOfItsUnroundedValue()
    {
        VulkanRenderer renderer = Renderer(Target.Unorm);
        Capture image = FlatView(renderer, radiance: 1 / 16f);
        Gpu.DisposeAndAssertClean(renderer);

        for (int i = 1; i < image.Rgba.Length; i += 4)
            ((int)image.Rgba[i]).Should().BeInRange(38, 40, "a value of 38.571 codes moves by at most one code");
    }

    // The dither's size falls to 0 at an encoded value of 0, so black stays black: no pixel of an unlit view rises to code 1.
    [Theory]
    [InlineData(Target.Srgb)]
    [InlineData(Target.Unorm)]
    public void AnUnlitViewStaysAtCodeZero(Target target)
    {
        VulkanRenderer renderer = Renderer(target);
        RenderSnapshot snapshot = DarkSnapshot();
        snapshot.Instances.Add(Wall(renderer, new(BaseColor: new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), Roughness: 1)));
        Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "unlit view");
        Gpu.DisposeAndAssertClean(renderer);

        for (int i = 0; i < image.Rgba.Length; i += 4)
            (image.Rgba[i] | image.Rgba[i + 1] | image.Rgba[i + 2])
                .Should()
                .Be(0, "pixel {0} has no light, and the dither adds nothing to an encoded 0", i / 4);
    }

    // The dither's size falls to 0 at an encoded value of 1, so white stays white: no pixel of a view past the curve's
    // white falls to code 254.
    [Theory]
    [InlineData(Target.Srgb)]
    [InlineData(Target.Unorm)]
    public void AViewPastWhiteStaysAtCode255(Target target)
    {
        VulkanRenderer renderer = Renderer(target);
        Capture image = FlatView(renderer, radiance: 1000);
        Gpu.DisposeAndAssertClean(renderer);

        for (int i = 0; i < image.Rgba.Length; i += 4)
            (image.Rgba[i] & image.Rgba[i + 1] & image.Rgba[i + 2])
                .Should()
                .Be(255, "pixel {0} is past white, and the dither takes nothing from an encoded 1", i / 4);
    }

    // The dither is a pure function of the pixel and the draw index: two renderers that draw the same snapshot as their
    // first draw show the same codes.
    [Fact]
    public void TheSameDrawOfTheSameViewShowsTheSameCodes()
    {
        VulkanRenderer first = Renderer(Target.Srgb);
        Capture a = FlatView(first, radiance: 1 / 16f);
        Gpu.DisposeAndAssertClean(first);
        VulkanRenderer second = Renderer(Target.Srgb);
        Capture b = FlatView(second, radiance: 1 / 16f);
        Gpu.DisposeAndAssertClean(second);

        b.Rgba.Should().Equal(a.Rgba, "the dither keeps no state and draws from no clock");
    }

    // The dither's pattern changes with the draw index, so that the eye averages the offsets over time instead of seeing
    // one fixed grain: two consecutive draws of a still view differ at many pixels.
    [Fact]
    public void TwoConsecutiveDrawsOfAStillViewShowDifferentPatterns()
    {
        VulkanRenderer renderer = Renderer(Target.Srgb);
        RenderSnapshot snapshot = DarkSnapshot();
        snapshot.Instances.Add(
            Wall(
                renderer,
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 1, Reflectance: 0, Emissive: new(1 / 16f))
            )
        );
        Capture first = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "first draw");
        snapshot.Tick++;
        snapshot.Time += 1.0 / 60;
        Capture second = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "second draw");
        Gpu.DisposeAndAssertClean(renderer);

        int differing = 0;
        for (int i = 1; i < first.Rgba.Length; i += 4)
            if (first.Rgba[i] != second.Rgba[i])
                differing++;
        differing.Should().BeGreaterThan(Width * Height / 4, "each draw reads its own frame of the blue-noise mask");
    }

    // A test that reads exact codes turns the dither off: a flat value then shows one code at every pixel, the target's
    // conversion of the unrounded value. The sRGB target of this Mac converts 38.571 codes to 38, not to the nearest 39.
    [Theory]
    [InlineData(Target.Srgb)]
    [InlineData(Target.Unorm)]
    public void WithoutTheDitherAFlatValueShowsOneCodeEverywhere(Target target)
    {
        VulkanRenderer renderer = Renderer(target, dither: false);
        Capture image = FlatView(renderer, radiance: 1 / 16f);
        Gpu.DisposeAndAssertClean(renderer);

        byte first = image.Rgba[1];
        ((int)first).Should().BeInRange(38, 39, "the unrounded value is 38.571 codes");
        for (int i = 1; i < image.Rgba.Length; i += 4)
            image.Rgba[i].Should().Be(first, "no dither moves pixel {0}", i / 4);
    }

    [Theory]
    [InlineData(Format.R8G8B8A8Srgb)]
    [InlineData(Format.B8G8R8A8Srgb)]
    [InlineData(Format.R8G8B8A8Unorm)]
    [InlineData(Format.B8G8R8A8Unorm)]
    public void EachEightBitOutputFormatGetsADitherCodeOfOneOver255(Format format) =>
        ToneMapPass
            .DitherCodeOf(format)
            .Should()
            .Be(1 / 255f, "an 8-bit output format stores 256 codes from 0 to 1, one code per 1/255");

    [Theory]
    [InlineData(Format.A2B10G10R10UnormPack32)]
    [InlineData(Format.R16G16B16A16Sfloat)]
    public void AFormatWiderThanEightBitsGetsNoOutputDither(Format format) =>
        ToneMapPass.DitherCodeOf(format).Should().Be(0f, "only the 8-bit output formats take the 1/255 output dither");
}

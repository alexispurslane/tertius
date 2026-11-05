using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The tone curves of <see cref="RendererOptions.ToneCurve"/> on the GPU: an emissive square that no light reaches shows
/// its emission through the curve at the exposure 1, and each shown color must match the published formula, which the
/// test computes, followed by the sRGB encoding. Bloom is off, so that no glow adds to a square.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ToneCurveTests
{
    /// <summary>One step of an 8-bit channel and a half step more, for the rounding of the target and of the half floats.</summary>
    private const float Tolerance = 1.5f / 255;

    private static VulkanRenderer Headless(ToneCurve curve) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = 640,
                Height = 360,
                Impostors = false,
                ToneCurve = curve,
                Exposure = new FixedExposure(0),
                Bloom = new NoBloom(),
                Upscaling = new NoUpscaling(),
                EnvironmentMap = new NoEnvironmentMap(),
            }
        );

    /// <summary>
    /// The linear color that Stephen Hill's ACES fitted curve gives a linear HDR color: into ACES AP1 with the RRT's
    /// saturation, the rational fit of the RRT and ODT, and back to linear sRGB, clamped to [0, 1].
    /// </summary>
    private static Vector3 AcesFitted(Vector3 color)
    {
        // Row-major, as Hill's HLSL writes them.
        double[,] input =
        {
            { 0.59719, 0.35458, 0.04823 },
            { 0.07600, 0.90834, 0.01566 },
            { 0.02840, 0.13383, 0.83777 },
        };
        double[,] output =
        {
            { 1.60475, -0.53108, -0.07367 },
            { -0.10208, 1.10813, -0.00605 },
            { -0.00327, -0.07276, 1.07602 },
        };
        double[] v = [color.X, color.Y, color.Z];
        double[] fitted = new double[3];
        for (int row = 0; row < 3; row++)
        {
            double ap1 = input[row, 0] * v[0] + input[row, 1] * v[1] + input[row, 2] * v[2];
            double a = ap1 * (ap1 + 0.0245786) - 0.000090537;
            double b = ap1 * (0.983729 * ap1 + 0.4329510) + 0.238081;
            fitted[row] = a / b;
        }

        double[] shown = new double[3];
        for (int row = 0; row < 3; row++)
            shown[row] = Math.Clamp(
                output[row, 0] * fitted[0] + output[row, 1] * fitted[1] + output[row, 2] * fitted[2],
                0,
                1
            );
        return new((float)shown[0], (float)shown[1], (float)shown[2]);
    }

    /// <summary>The sRGB encoding of a linear channel in [0, 1].</summary>
    private static float Encoded(double linear) =>
        (float)(linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055);

    private static Vector3 Encoded(Vector3 linear) => new(Encoded(linear.X), Encoded(linear.Y), Encoded(linear.Z));

    /// <summary>What Hable's curve shows for an emission, per channel, after the sRGB encoding.</summary>
    private static Vector3 HableShown(Vector3 emission) =>
        new(
            Encoded(HableCurve.Shown(emission.X)),
            Encoded(HableCurve.Shown(emission.Y)),
            Encoded(HableCurve.Shown(emission.Z))
        );

    /// <summary>Requires each square to show what <paramref name="expected"/> gives for its emission.</summary>
    private static void ShouldShow(
        IReadOnlyList<Vector3> emissions,
        IReadOnlyList<Vector3> shown,
        Func<Vector3, Vector3> expected,
        string curve
    )
    {
        for (int i = 0; i < emissions.Count; i++)
            LargestChannelDifference(shown[i], expected(emissions[i]))
                .Should()
                .BeLessThan(
                    Tolerance,
                    "{0} must show the emission {1} as {2}, not {3}",
                    curve,
                    emissions[i],
                    expected(emissions[i]),
                    shown[i]
                );
    }

    [Fact]
    public void TheDefaultCurveIsAcesFittedAndShowsEachEmissionAsHillsFitGives()
    {
        Vector3[] emissions = [new(0.02f, 0.05f, 0.1f), new(0.18f, 0.18f, 0.18f), new(1.5f, 0.6f, 0.2f), new(5.6f)];
        var renderer = VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = 640,
                Height = 360,
                Impostors = false,
                Exposure = new FixedExposure(0),
                Bloom = new NoBloom(),
                Upscaling = new NoUpscaling(),
                EnvironmentMap = new NoEnvironmentMap(),
            }
        );
        try
        {
            Vector3[] shown = Displayed(renderer, emissions, "tone-curve-default");

            ShouldShow(
                emissions: emissions,
                shown: shown,
                expected: e => Encoded(AcesFitted(e)),
                curve: "the default ACES fitted curve"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void HablesCurveShowsEachChannelOfTheToeAsHisFormulaGives()
    {
        Vector3[] emissions = [new(0.01f, 0.03f, 0.08f), new(0.18f, 0.12f, 0.05f)];
        VulkanRenderer renderer = Headless(ToneCurve.Hable);
        try
        {
            Vector3[] shown = Displayed(renderer, emissions, "tone-curve-hable-toe");

            ShouldShow(emissions: emissions, shown: shown, expected: HableShown, curve: "Hable's curve");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void HablesCurveShowsEachChannelOfTheShoulderAsHisFormulaGives()
    {
        Vector3[] emissions = [new(0.5f, 1.5f, 3f), new(4.5f, 1f, 0.3f)];
        VulkanRenderer renderer = Headless(ToneCurve.Hable);
        try
        {
            Vector3[] shown = Displayed(renderer, emissions, "tone-curve-hable-shoulder");

            ShouldShow(emissions: emissions, shown: shown, expected: HableShown, curve: "Hable's curve");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void HablesCurveShowsHalfItsWhitePointAsWhite()
    {
        // His exposure bias of 2 doubles the emission before the curve, so W / 2 reaches the white point W.
        var emission = new Vector3((float)HableCurve.Brightest);
        VulkanRenderer renderer = Headless(ToneCurve.Hable);
        try
        {
            Vector3 shown = Displayed(renderer, [emission], "tone-curve-hable-white")[0];

            shown.Should().Be(Vector3.One, "an emission of W / 2 = 5.6 shows white under Hable's curve");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AToneCurveOutsideTheEnumIsRefused() =>
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        ToneCurve = (ToneCurve)2,
                        Upscaling = new NoUpscaling(),
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
}

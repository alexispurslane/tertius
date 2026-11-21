using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// How each path that stores a 32-bit float into a 16-bit float rounds on this driver (ticket t8-69v7, ARCHITECTURE.md,
/// "Portable Vulkan"). The Vulkan spec lets a conversion to a half float round toward zero or to the nearest. The probe
/// stores values that lie between two half floats through four paths: a compute shader's imageStore, a fragment
/// shader's write into a color attachment without a blend, an additive blend into one, and packHalf2x16. The test reports
/// the rounding of each path into the file that TERTIUS_STORE_ROUNDING_REPORT names, because the test runner shows no
/// output of a passing test. It asserts nothing about the driver, so it is a measurement, not a test of the engine: it
/// skips unless the variable is set, as the GPU calibration does (ARCHITECTURE.md, "Portable Vulkan", holds its table).
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class StoreRoundingTests
{
    private const int Width = 144,
        Height = 16;

    /// <summary>The number of cases in Shaders/store_rounding.glsl.</summary>
    private const int Cases = 36;

    // The table of Shaders/store_rounding.glsl: the bases, the half float spacing above each, and the shares of it.
    private static readonly float[] Bases = [1f, 1.0009765625f, 0.296875f, 100f, 100.0625f, 3000f];
    private static readonly float[] Spacings = [1f / 1024, 1f / 1024, 1f / 4096, 1f / 16, 1f / 16, 2f];
    private static readonly float[] Shares = [0.25f, 0.5f, 0.75f];

    /// <summary>The variable that names the report's file and turns the measurement on.</summary>
    private const string ReportVariable = "TERTIUS_STORE_ROUNDING_REPORT";

    [Fact]
    public void EachStorePathIntoAHalfFloatReportsItsRounding()
    {
        string? file = Environment.GetEnvironmentVariable(ReportVariable);
        Assert.SkipWhen(string.IsNullOrEmpty(file), $"Set {ReportVariable} to a file to measure the store paths.");
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            upscaling: new NoUpscaling()
        );
        try
        {
            var compute = ScreenTraceProbe.On(renderer, "store_rounding_probe.comp");
            var fragment = StoreRoundingProbe.On(renderer);
            renderer.Render(Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0), 1f);

            float[] stored = compute.Read();
            (string Path, float[] Image, int Channel)[] paths =
            [
                ("compute imageStore", stored, 0),
                ("fragment write without a blend", fragment.ReadWritten(), 0),
                ("additive blend", fragment.ReadBlended(), 0),
                ("packHalf2x16", stored, 1),
            ];
            string report = string.Join(
                "\n",
                paths.Select(path => $"{path.Path}: {Rounding(path.Image, path.Channel)}")
            );
            output.WriteLine(report);
            File.WriteAllText(file!, report + "\n");
            string.Join("\n", renderer.ValidationMessages).Should().BeEmpty();
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>
    /// The rounding that the cases in channel <paramref name="channel"/> of the first row of <paramref name="image"/> show:
    /// to the nearest with ties to even, to the nearest with ties away from zero, toward zero, or other, with the cases
    /// that fit none of them.
    /// </summary>
    private static string Rounding(float[] image, int channel)
    {
        bool nearestEven = true,
            nearestAway = true,
            towardZero = true;
        List<string> cases = [];
        for (int c = 0; c < Cases; c++)
        {
            float sign = c >= Cases / 2 ? -1 : 1;
            float lower = sign * Bases[c % 6];
            float upper = sign * (Bases[c % 6] + Spacings[c % 6]);
            float share = Shares[c / 6 % 3];
            float value = sign * (Bases[c % 6] + share * Spacings[c % 6]);
            float actual = image[4 * c + channel];
            float nearest = (float)(Half)value;
            nearestEven &= actual == nearest;
            nearestAway &= actual == (share < 0.5f ? lower : upper);
            towardZero &= actual == lower;
            cases.Add($"{value:R} -> {actual:R}");
        }
        if (nearestEven)
            return "to nearest, ties to even";
        if (nearestAway)
            return "to nearest, ties away from zero";
        if (towardZero)
            return "toward zero";
        return "other: " + string.Join(", ", cases);
    }
}

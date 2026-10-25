using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The cap at MaxRadiance across the split of the lighting pass: the direct pass caps the direct light, the ambient pass
/// caps the sum, and together they give the one pass's light below the cap and keep the surfaces' share at it
/// (docs/plans/screen-space-illumination.md, "The cap at MaxRadiance"). Single floats, as the shaders compute.
/// </summary>
public sealed class LightingSplitTests
{
    private static readonly Vector3 Cap = new(ShaderInput.MaxRadiance);

    /// <summary>The lighting pass before the split: min(exposed(ambient + direct, k) + emission, c).</summary>
    private static Vector3 OnePass(Vector3 direct, Vector3 ambient, Vector3 emission, float exposure) =>
        Vector3.Min(ShaderInput.Exposed(direct + ambient, exposure) + emission, Cap);

    /// <summary>The direct pass, rounded to the half floats of the direct image: min(exposed(direct, k) + emission, c).</summary>
    private static Vector3 DirectImage(Vector3 direct, Vector3 emission, float exposure)
    {
        var light = Vector3.Min(ShaderInput.Exposed(direct, exposure) + emission, Cap);
        return new((float)(Half)light.X, (float)(Half)light.Y, (float)(Half)light.Z);
    }

    /// <summary>The ambient pass: min(direct image + exposed(ambient, k), c).</summary>
    private static Vector3 AmbientPass(Vector3 directImage, Vector3 ambient, float exposure) =>
        Vector3.Min(directImage + ShaderInput.Exposed(ambient, exposure), Cap);

    private static Vector3 RandomBelow(Random random, float most) =>
        new(x: random.NextSingle() * most, y: random.NextSingle() * most, z: random.NextSingle() * most);

    [Fact]
    public void ExposedLightIsTheRadianceTimesTheExposureBelowTheCap() =>
        ShaderInput.Exposed(new(1, 2, 3), 4).Should().Be(new Vector3(4, 8, 12));

    [Fact]
    public void ExposedLightStopsAtTheCapBeforeTheProductLeavesTheFloatRange() =>
        ShaderInput
            .Exposed(new(ShaderInput.RadianceLimit, 1, ShaderInput.RadianceLimit), MathF.Pow(2, 64))
            .Should()
            .Be(
                new Vector3(ShaderInput.MaxRadiance, ShaderInput.MaxRadiance, ShaderInput.MaxRadiance),
                "2⁶⁴ times 2⁶⁴ is past the largest single float, so the cap applies before the product"
            );

    [Fact]
    public void TheDirectAndTheAmbientPassesSumToTheOnePassLightBelowTheCap()
    {
        var random = new Random(3);
        for (int trial = 0; trial < 1000; trial++)
        {
            // Pre-exposed parts of a third of the cap each, so that no cap binds.
            float exposure = MathF.Pow(2, -20 + 40 * random.NextSingle());
            Vector3 direct = RandomBelow(random, ShaderInput.MaxRadiance / 3) / exposure;
            Vector3 ambient = RandomBelow(random, ShaderInput.MaxRadiance / 3) / exposure;
            Vector3 emission = RandomBelow(random, ShaderInput.MaxRadiance / 3);

            Vector3 split = AmbientPass(DirectImage(direct, emission, exposure), ambient, exposure);
            Vector3 one = OnePass(direct: direct, ambient: ambient, emission: emission, exposure: exposure);

            // The direct image rounds once more, to within half a step of the half float, at most 2⁻¹¹ of its value.
            Vector3 tolerance = one * MathF.Pow(2, -11) + new Vector3(1e-6f);
            Vector3.Abs(split - one).X.Should().BeLessThanOrEqualTo(tolerance.X, $"trial {trial}");
            Vector3.Abs(split - one).Y.Should().BeLessThanOrEqualTo(tolerance.Y, $"trial {trial}");
            Vector3.Abs(split - one).Z.Should().BeLessThanOrEqualTo(tolerance.Z, $"trial {trial}");
        }
    }

    [Theory]
    [InlineData(-64)]
    [InlineData(0)]
    [InlineData(64)]
    public void TheTwoPassesKeepAPixelWithinTheSurfacesShare(int stops)
    {
        // Both parts at the brightest radiance that the renderer reads, and emission at the cap.
        float exposure = MathF.Pow(2, stops);
        var brightest = new Vector3(ShaderInput.RadianceLimit);

        Vector3 pixel = AmbientPass(DirectImage(brightest, Cap, exposure), brightest, exposure);

        pixel.Should().Be(Cap, "each pass caps its sum, so the pixel keeps to the surfaces' share of the HDR image");
    }
}

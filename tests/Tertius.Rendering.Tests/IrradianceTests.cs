using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The CPU's reading of a map's diffuse light: the last level of its irradiance cube, its mean radiance and the halves it
/// reads back (docs/plans/environment-map.md, "The irradiance").
/// </summary>
public sealed class IrradianceTests
{
    [Fact]
    public void TheMeanRadianceIsTheMeanOfTheTexelsBecauseEachSpansTheSameSolidAngle()
    {
        var texels = new Vector3[Irradiance.Texels];
        texels[0] = new(24, 48, 0);
        texels[23] = new(0, 0, 72);

        new Irradiance(texels).MeanRadiance.Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void AUniformCubeHasItsOwnLightAsTheMeanRadiance()
    {
        var texels = new Vector3[Irradiance.Texels];
        Array.Fill(texels, new Vector3(2, 3, 4));

        Vector3.Distance(new Irradiance(texels).MeanRadiance, new(2, 3, 4)).Should().BeLessThan(1e-6f);
    }

    [Fact]
    public void TheReadbackIsReadAsRgbaTexelsInOrderWithTheAlphaLeftOut()
    {
        var halves = new Half[Irradiance.Halves];
        for (int i = 0; i < halves.Length; i++)
            halves[i] = (Half)(i % 4 == 3 ? 100 : i);

        var irradiance = Irradiance.Of(halves);

        irradiance[0].Should().Be(new Vector3(0, 1, 2));
        irradiance[Irradiance.Texels - 1].Should().Be(new Vector3(92, 93, 94));
    }

    [Fact]
    public void AnIrradianceOfAnotherCountOfTexelsIsRefused()
    {
        Action fewer = () => _ = new Irradiance(new Vector3[Irradiance.Texels - 1]);

        fewer.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AReadbackOfAnotherCountOfHalvesIsRefused()
    {
        Action fewer = () => Irradiance.Of(new Half[Irradiance.Halves - 4]);

        fewer.Should().Throw<ArgumentException>();
    }
}

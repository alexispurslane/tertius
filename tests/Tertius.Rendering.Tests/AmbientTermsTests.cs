namespace Tertius.Rendering.Tests;

/// <summary>
/// The one function that applies the estimate, the occlusion V, the open share S and the bounce G, to the two ambient
/// terms, whatever source gave them (docs/plans/screen-space-illumination.md, "The ambient pass"): the diffuse term is
/// the source's diffuse term times S plus G under the diffuse color, and the specular term takes the specular occlusion
/// of V. The terms have a different color in each channel of each term, which no hemisphere gives, so the tests hold for
/// any source.
/// </summary>
public sealed class AmbientTermsTests
{
    private static readonly AmbientTerms Terms = new(Diffuse: new(0.31, 0.57, 0.83), Specular: new(0.05, 0.11, 0.23));
    private static readonly Double3 DiffuseColor = new(0.7, 0.4, 0.2);

    [Fact]
    public void TheOccludedDiffuseTermIsTheDiffuseTermTimesTheOpenShareInEachChannel()
    {
        Double3 openShare = new(0.6, 0.3, 0.9);

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            openShare: openShare,
            bounce: Double3.Zero,
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: new GgxLobe(0.4)
        );

        occluded.Diffuse.Should().Be(Terms.Diffuse * openShare);
    }

    [Fact]
    public void TheOccludedDiffuseTermAddsTheBounceUnderTheDiffuseColor()
    {
        Double3 openShare = new(0.6, 0.3, 0.9);
        Double3 bounce = new(1.5, 0.5, 3);

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            openShare: openShare,
            bounce: bounce,
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: new GgxLobe(0.4)
        );

        occluded.Diffuse.Should().Be(Terms.Diffuse * openShare + DiffuseColor * bounce);
    }

    [Fact]
    public void TheDiffuseTermNeverFallsBelowZeroAtAnyOpenShareAndBounceOfTheEstimate()
    {
        // The estimate's S lies in [0, 1] and G in [0, c], so no term goes below 0 and nothing clamps it.
        var random = new Random(11);
        for (int trial = 0; trial < 1000; trial++)
        {
            Double3 openShare = new(random.NextDouble(), random.NextDouble(), random.NextDouble());
            Double3 bounce = new Double3(random.NextDouble(), random.NextDouble(), random.NextDouble()) * 3;

            AmbientTerms occluded = Terms.Occluded(
                occlusion: random.NextDouble(),
                openShare: openShare,
                bounce: bounce,
                diffuseColor: DiffuseColor,
                nDotV: 0.7,
                lobe: new GgxLobe(0.4)
            );

            occluded
                .Diffuse.Should()
                .Be(Terms.Diffuse * openShare + DiffuseColor * bounce, "trial {0}: the form has no clamp", trial);
            Math.Min(occluded.Diffuse.X, Math.Min(occluded.Diffuse.Y, occluded.Diffuse.Z))
                .Should()
                .BeGreaterThanOrEqualTo(0, "trial {0}: a surface sends no negative light", trial);
        }
    }

    [Fact]
    public void TheSpecularTermTakesTheSpecularOcclusionOfVAndNotTheOpenShareOrTheBounce()
    {
        var lobe = new GgxLobe(0.4);

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            openShare: new(0.2, 0.9, 0.5),
            bounce: new(1.5, 0.5, 3),
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: lobe
        );

        occluded
            .Specular.Should()
            .Be(
                Terms.Specular * lobe.SpecularOcclusion(nDotV: 0.7, occlusion: 0.6),
                "the bounce is diffuse light only, and light that the screen reflects is the reflections'"
            );
    }

    [Fact]
    public void WithAnOpenShareOfVInEachChannelAndNoBounceTheOccludedTermsAreThoseOfTheHemisphereModel()
    {
        var lobe = new GgxLobe(0.4);
        var hemisphere = new AmbientTerms(
            Diffuse: Terms.Diffuse * 0.6,
            Specular: Terms.Specular * lobe.SpecularOcclusion(nDotV: 0.7, occlusion: 0.6)
        );

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            openShare: new(0.6, 0.6, 0.6),
            bounce: Double3.Zero,
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: lobe
        );

        occluded.Should().Be(hemisphere, "an open direction takes the source's mean light where it holds no direction");
    }

    [Fact]
    public void AnOpenEstimateLeavesTheTermsAsTheyAre()
    {
        AmbientTerms occluded = Terms.Occluded(
            occlusion: 1,
            openShare: new(1, 1, 1),
            bounce: Double3.Zero,
            diffuseColor: DiffuseColor,
            nDotV: 0.3,
            lobe: new GgxLobe(0.1)
        );

        occluded.Should().Be(Terms, "a point that nothing hides keeps the same bits in both terms");
    }
}

namespace Tertius.Rendering.Tests;

/// <summary>
/// The one function that applies the estimate, the occlusion and the bounce, to the two ambient terms, whatever source
/// gave them (docs/plans/screen-space-illumination.md, "The ambient pass"). The terms have a different color in each
/// channel of each term, which no hemisphere gives, so the tests hold for any source.
/// </summary>
public sealed class AmbientTermsTests
{
    private static readonly AmbientTerms Terms = new(Diffuse: new(0.31, 0.57, 0.83), Specular: new(0.05, 0.11, 0.23));
    private static readonly Double3 DiffuseColor = new(0.7, 0.4, 0.2);

    [Fact]
    public void TheOccludedDiffuseTermIsTheDiffuseTermTimesTheOcclusion()
    {
        var lobe = new GgxLobe(0.4);

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            bounce: Double3.Zero,
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: lobe
        );

        occluded.Diffuse.Should().Be(Terms.Diffuse * 0.6);
    }

    [Fact]
    public void TheOccludedDiffuseTermAddsTheBounceUnderTheDiffuseColor()
    {
        var lobe = new GgxLobe(0.4);
        Double3 bounce = new(1.5, 0.5, 3);

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            bounce: bounce,
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: lobe
        );

        occluded.Diffuse.Should().Be(Terms.Diffuse * 0.6 + DiffuseColor * bounce);
    }

    [Fact]
    public void TheBounceLeavesTheSpecularTerm()
    {
        var lobe = new GgxLobe(0.4);

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
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
    public void WithNoBounceTheOccludedTermsAreThoseOfBefore()
    {
        var lobe = new GgxLobe(0.4);
        var before = new AmbientTerms(
            Diffuse: Terms.Diffuse * 0.6,
            Specular: Terms.Specular * lobe.SpecularOcclusion(nDotV: 0.7, occlusion: 0.6)
        );

        AmbientTerms occluded = Terms.Occluded(
            occlusion: 0.6,
            bounce: Double3.Zero,
            diffuseColor: DiffuseColor,
            nDotV: 0.7,
            lobe: lobe
        );

        occluded
            .Should()
            .Be(before, "a texel with no blocker that gives light keeps the same bits as before the bounce");
    }

    [Fact]
    public void AtFullVisibilityAndNoBounceTheOccludedTermsAreTheTerms()
    {
        AmbientTerms occluded = Terms.Occluded(
            occlusion: 1,
            bounce: Double3.Zero,
            diffuseColor: DiffuseColor,
            nDotV: 0.3,
            lobe: new GgxLobe(0.1)
        );

        occluded.Should().Be(Terms, "a point that nothing hides keeps the same bits in both terms");
    }
}

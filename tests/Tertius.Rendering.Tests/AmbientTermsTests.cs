namespace Tertius.Rendering.Tests;

/// <summary>
/// The one function that applies ambient occlusion to the two ambient terms, whatever source gave them
/// (docs/plans/ambient-occlusion.md, "The lighting pass"). The terms have a different color in each channel of each
/// term, which no hemisphere gives, so the tests hold for any source.
/// </summary>
public sealed class AmbientTermsTests
{
    private static readonly AmbientTerms Terms = new(Diffuse: new(0.31, 0.57, 0.83), Specular: new(0.05, 0.11, 0.23));

    [Fact]
    public void TheOccludedDiffuseTermIsTheDiffuseTermTimesTheOcclusion()
    {
        var lobe = new GgxLobe(0.4);

        AmbientTerms occluded = Terms.Occluded(occlusion: 0.6, nDotV: 0.7, lobe: lobe);

        occluded.Diffuse.Should().Be(Terms.Diffuse * 0.6);
    }

    [Fact]
    public void TheOccludedSpecularTermIsTheSpecularTermTimesItsSpecularOcclusion()
    {
        var lobe = new GgxLobe(0.4);

        AmbientTerms occluded = Terms.Occluded(occlusion: 0.6, nDotV: 0.7, lobe: lobe);

        occluded.Specular.Should().Be(Terms.Specular * lobe.SpecularOcclusion(nDotV: 0.7, occlusion: 0.6));
    }

    [Fact]
    public void AtFullVisibilityTheOccludedTermsAreTheTerms()
    {
        AmbientTerms occluded = Terms.Occluded(occlusion: 1, nDotV: 0.3, lobe: new GgxLobe(0.1));

        occluded.Should().Be(Terms, "a point that nothing hides keeps the same bits in both terms");
    }
}

namespace Tertius.Rendering.Tests;

/// <summary>
/// The model's code in bits 0 and 1 of the shading word, which the shading target's alpha channel holds
/// (docs/plans/skin-and-hair.md, "The shading target").
/// </summary>
public sealed class ShadingCodeTests
{
    private const uint ModelBits = 0b11;

    [Fact]
    public void EachModelHasItsOwnCode()
    {
        uint[] codes =
        [
            ShadingCode.Word(new StandardShading(0.5f)) & ModelBits,
            ShadingCode.Word(new SkinShading()) & ModelBits,
            ShadingCode.Word(new HairShading(StrandDirection.MinusV)) & ModelBits,
        ];

        codes.Should().OnlyHaveUniqueItems("the lighting passes choose a pixel's response by its code alone");
    }

    [Fact]
    public void TheStandardCodeIsZeroAsInAClearedTarget()
    {
        (ShadingCode.Word(new StandardShading(1)) & ModelBits)
            .Should()
            .Be(
                0,
                "the shading target is cleared to 0, so the sky and every pixel that nothing draws read as standard"
            );
        ShadingCode.Word(default).Should().Be(0, "a default material is a standard non-metal");
    }
}

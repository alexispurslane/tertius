namespace Tertius.Rendering.Tests;

/// <summary>
/// The card code of a hair pixel in the shading target's 13 spare bits of B and A, beside the light mark and the model's
/// code (docs/plans/skin-and-hair.md, "The shading target").
/// </summary>
public sealed class CardCodeTests
{
    [Fact]
    public void TheCardsOfOneInstanceHaveDistinctCodesUpToEightThousandOneHundredNinetyTwoCards()
    {
        uint[] codes = [.. Enumerable.Range(0, 8192).Select(card => CardCode.Of(card: (uint)card, instance: 7))];

        codes.Should().OnlyHaveUniqueItems("two cards of one instance never take each other's steps as their own");
    }

    [Fact]
    public void OneCardOfTwoInstancesHasTwoCodes() =>
        CardCode
            .Of(card: 0, instance: 1)
            .Should()
            .NotBe(CardCode.Of(card: 0, instance: 0), "two instances of one hair mesh shade each other");

    [Fact]
    public void TheCardsOfNeighborInstancesLieFarApartInTheCodes()
    {
        // The same card of instances 0 and 1: their codes differ by more than the cards of a large hair mesh.
        uint step = (CardCode.Of(card: 0, instance: 1) - CardCode.Of(card: 0, instance: 0)) % 8192;

        Math.Min(step, 8192 - step).Should().BeGreaterThan(2000, "neighbor instances share no code below 2,000 cards");
    }

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u, true)]
    [InlineData(127u, false)]
    [InlineData(128u, true)]
    [InlineData(8191u, true)]
    public void AHairPixelsChannelsGiveBackItsCodeItsLightMarkAndItsModel(uint code, bool holdsLight)
    {
        (byte b, byte a) = CardCode.Channels(code: code, holdsLight: holdsLight, model: ShadingCode.Hair);

        CardCode.Read(b: b, a: a).Should().Be(code);
        (b & 1).Should().Be(holdsLight ? 1 : 0, "B keeps its light mark in bit 0");
        (a & 0b11).Should().Be((int)ShadingCode.Hair, "A keeps the model's code in bits 0 and 1");
    }

    [Theory]
    [InlineData(ShadingCode.Standard)]
    [InlineData(ShadingCode.Skin)]
    public void APixelOfAnotherModelHasNoCard(uint model)
    {
        (byte b, byte a) = CardCode.Channels(code: 5, holdsLight: true, model: model);

        CardCode.Read(b: b, a: a).Should().BeNull("only hair has cards");
        (b, a).Should().Be((1, (byte)model), "a pixel of another model writes 0 into the spare bits");
    }
}

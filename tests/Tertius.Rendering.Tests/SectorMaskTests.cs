namespace Tertius.Rendering.Tests;

/// <summary>
/// The 32-bit mask of a slice's blocked sectors: its runs, its counts, its unions and its exceptions
/// (docs/plans/screen-space-illumination.md, "A near step").
/// </summary>
public sealed class SectorMaskTests
{
    [Fact]
    public void ARunHoldsTheSectorsFromItsStartToBeforeItsEnd() =>
        SectorMask.Run(start: 3, end: 7).Bits.Should().Be(0b111_1000u);

    [Theory]
    [InlineData(5, 5)]
    [InlineData(9, 4)]
    [InlineData(32, 32)]
    public void AnEmptyRunBlocksNothing(int start, int end) =>
        SectorMask.Run(start: start, end: end).Should().Be(SectorMask.None);

    [Fact]
    public void ARunToTheLastSectorHoldsIt() => SectorMask.Run(start: 30, end: 32).Bits.Should().Be(0xC000_0000u);

    [Fact]
    public void AFullRunBlocksAllThirtyTwoSectors() =>
        SectorMask
            .Run(start: 0, end: 32)
            .Bits.Should()
            .Be(
                uint.MaxValue,
                "a run of 32 must not shift by 32, which C# takes as a shift by 0 and GLSL leaves undefined"
            );

    [Fact]
    public void TheCountIsTheNumberOfBlockedSectors() => new SectorMask(0b1011_0001u).Count.Should().Be(4);

    [Fact]
    public void TheOpenSectorsAreTheOnesNotBlocked() =>
        new SectorMask(0x0000_FFF0u).Open.Should().Be(new SectorMask(0xFFFF_000Fu));

    [Fact]
    public void AUnionBlocksTheSectorsOfEither() =>
        new SectorMask(0b0011u).Union(new SectorMask(0b0110u)).Should().Be(new SectorMask(0b0111u));

    [Fact]
    public void AnExceptionKeepsTheSectorsThatTheOtherDoesNotBlock() =>
        new SectorMask(0b0111u).Except(new SectorMask(0b0110u)).Should().Be(new SectorMask(0b0001u));
}

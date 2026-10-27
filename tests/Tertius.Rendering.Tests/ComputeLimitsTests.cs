namespace Tertius.Rendering.Tests;

/// <summary>
/// The one rule that spreads a compute pass's workgroups over rows of at most the spec's least
/// <c>maxComputeWorkGroupCount</c> (<see cref="ComputeLimits.Grid"/>), which the deform pass and the prefilter share.
/// </summary>
public sealed class ComputeLimitsTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(4_096u)]
    [InlineData(65_535u)]
    public void AGridOfAtMostTheLeastMaxWorkgroupCountIsOneRow(uint groups) =>
        ComputeLimits.Grid(groups).Should().Be((groups, 1u));

    [Theory]
    [InlineData(65_536u, 32_768u, 2u)]
    [InlineData(131_071u, 43_691u, 3u)]
    [InlineData(172_032u, 57_344u, 3u)]
    [InlineData(196_606u, 49_152u, 4u)]
    [InlineData(200_000u, 50_000u, 4u)]
    public void AGridPastTheLeastMaxWorkgroupCountTakesAsFewRowsAsHoldItAndRowsAsShortAsHoldIt(
        uint groups,
        uint x,
        uint y
    ) => ComputeLimits.Grid(groups).Should().Be((x, y));

    [Theory]
    [InlineData(65_536u)]
    [InlineData(196_606u)]
    [InlineData(4_194_240u)]
    [InlineData(70_000_000u)]
    [InlineData(4_294_836_225u)] // 65,535 rows of 65,535: the largest count that a grid holds
    public void AGridStaysWithinEveryDeviceAndEndsLessThanOneRowPastTheLastWorkgroup(uint groups)
    {
        (uint x, uint y) = ComputeLimits.Grid(groups);

        x.Should().BeLessThanOrEqualTo(65_535, "the spec's least maxComputeWorkGroupCount[0]");
        y.Should().BeLessThanOrEqualTo(65_535, "the spec's least maxComputeWorkGroupCount[1]");
        ((ulong)x * y).Should().BeGreaterThanOrEqualTo(groups, "the grid holds every workgroup");
        ((ulong)x * y - groups).Should().BeLessThan(y, "the rows are as short as hold the workgroups");
    }
}

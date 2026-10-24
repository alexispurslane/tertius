using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary><see cref="RefList{T}.SetCount"/>: the count changes, the items the list keeps stay, and dropped references go.</summary>
public sealed class RefListTests
{
    [Fact]
    public void GrowingTheCountKeepsTheItemsBelowTheOldCount()
    {
        var list = new RefList<int>(8);
        list.Add(1);
        list.Add(2);
        list.Add(3);

        list.SetCount(5);

        list.Count.Should().Be(5);
        list.AsSpan()[..3].ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void GrowingTheCountPastTheCapacityKeepsTheItems()
    {
        var list = new RefList<int>(4);
        for (int i = 1; i <= 4; i++)
            list.Add(i);

        list.SetCount(10);

        list.Count.Should().Be(10);
        list.AsSpan()[..4].ToArray().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ShrinkingTheCountDropsTheReferencesOfTheItemsPastIt()
    {
        var list = new RefList<string>(8);
        list.Add("a");
        list.Add("b");
        list.Add("c");

        list.SetCount(1);

        list.Count.Should().Be(1);
        list.AsSpan().ToArray().Should().Equal("a");
        list.Items[1].Should().BeNull();
        list.Items[2].Should().BeNull();
    }

    [Fact]
    public void GrowingTheCountAgainExposesClearedItemsForATypeWithReferences()
    {
        var list = new RefList<string>(8);
        list.Add("a");
        list.Add("b");
        list.Add("c");
        list.SetCount(1);

        list.SetCount(3);

        list.AsSpan().ToArray().Should().Equal("a", null, null);
    }

    [Fact]
    public void ANegativeCountIsRefused()
    {
        var list = new RefList<int>(4);

        Action set = () => list.SetCount(-1);

        set.Should().Throw<ArgumentOutOfRangeException>();
    }
}

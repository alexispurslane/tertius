using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>The 512-bit sets of component types, and the component masks that test them. Pure logic, no world.</summary>
public class ComponentFlagsTests
{
    private static ComponentFlags Of(params int[] ids)
    {
        ComponentFlags flags = default;
        foreach (int id in ids)
            flags.Add(id);
        return flags;
    }

    private static List<int> Ids(ComponentFlags flags)
    {
        var ids = new List<int>();
        foreach (int id in flags)
            ids.Add(id);
        return ids;
    }

    [Fact]
    public void AFlagSetHoldsTheTypesThatWereAddedAndNoOthers()
    {
        ComponentFlags flags = Of(3, 64, 511);

        flags.Contains(3).Should().BeTrue();
        flags.Contains(64).Should().BeTrue();
        flags.Contains(511).Should().BeTrue();
        flags.Contains(4).Should().BeFalse();
        flags.Contains(63).Should().BeFalse();
        flags.IsEmpty.Should().BeFalse();
        ComponentFlags.None.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void RemovingATypeTakesOnlyThatTypeOut()
    {
        ComponentFlags flags = Of(3, 4);

        flags.Remove(3);

        Ids(flags).Should().Equal(4);
    }

    [Fact]
    public void EnumeratingASetYieldsItsIdsFromTheLowestUpAcrossEveryWord()
    {
        Ids(Of(511, 0, 64, 63, 300)).Should().Equal(0, 63, 64, 300, 511);
        Ids(ComponentFlags.None).Should().BeEmpty();
    }

    [Fact]
    public void ContainsAllIsTrueOnlyWhenEveryTypeOfTheOtherSetIsInThisOne()
    {
        ComponentFlags flags = Of(1, 70, 400);

        flags.ContainsAll(Of(1, 400)).Should().BeTrue();
        flags.ContainsAll(ComponentFlags.None).Should().BeTrue();
        flags.ContainsAll(Of(1, 401)).Should().BeFalse("type 401 is missing");
        flags.ContainsAll(Of(71)).Should().BeFalse();
    }

    [Fact]
    public void OverlapsIsTrueOnlyWhenTheSetsShareAType()
    {
        ComponentFlags flags = Of(1, 400);

        flags.Overlaps(Of(400)).Should().BeTrue();
        flags.Overlaps(Of(2, 401)).Should().BeFalse();
        flags.Overlaps(ComponentFlags.None).Should().BeFalse();
    }

    [Fact]
    public void ExceptKeepsTheTypesThatTheOtherSetLacks()
    {
        Ids(Of(1, 70, 400).Except(Of(70, 5))).Should().Equal(1, 400);
    }

    [Fact]
    public void TwoSetsAreEqualWhenTheyHoldTheSameTypes()
    {
        Of(1, 300).Should().Be(Of(300, 1));
        (Of(1, 300) == Of(300, 1)).Should().BeTrue();
        (Of(1) != Of(2)).Should().BeTrue();
        Of(1, 300).GetHashCode().Should().Be(Of(300, 1).GetHashCode());
    }

    [Fact]
    public void TheFlagsOfAComponentListHoldTheTypeOfEachComponent()
    {
        ComponentFlags flags = ComponentFlags.Of([new Flammable(), new Metal(), new Metal()]);

        flags.Contains<Flammable>().Should().BeTrue();
        flags.Contains<Metal>().Should().BeTrue();
        flags.Contains<Wet>().Should().BeFalse();
        Ids(flags).Should().HaveCount(2);
    }

    [Fact]
    public void AComponentMaskMatchesFlagsThatHoldEveryWithTypeAndNoWithoutType()
    {
        ComponentMask mask = EntityQuery.With<Flammable>().With<Metal>().Without<Wet>().Components;

        mask.Matches(ComponentFlags.Of([new Flammable(), new Metal()])).Should().BeTrue();
        mask.Matches(ComponentFlags.Of([new Flammable(), new Metal(), new Crew()])).Should().BeTrue();
        mask.Matches(ComponentFlags.Of([new Flammable()])).Should().BeFalse("it lacks Metal");
        mask.Matches(ComponentFlags.Of([new Flammable(), new Metal(), new Wet()])).Should().BeFalse("it has Wet");
    }

    [Fact]
    public void TheEmptyComponentMaskMatchesEverySet()
    {
        default(ComponentMask).Matches(ComponentFlags.None).Should().BeTrue();
        default(ComponentMask).Matches(ComponentFlags.Of([new Wet()])).Should().BeTrue();
    }
}

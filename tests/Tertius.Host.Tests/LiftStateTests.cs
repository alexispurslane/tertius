using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The table of the lift's changes, <see cref="LiftState.Next"/> (docs/plans/blue-danube-demo.md, "The lift"), as pure
/// data: one test per row, and the pairs without a row.
/// </summary>
public sealed class LiftStateTests
{
    private static readonly Entity Rider = new(7, 1);

    /// <summary>An order at the top starts the descent from the order's start, for the rider that sent it.</summary>
    [Fact]
    public void AnOrderAtTheTopStartsTheDescent() =>
        LiftState
            .Next(new AtTop(), new LiftOrder(Start: 12.5, Rider: Rider))
            .Should()
            .Be((LiftState)new Descending(Start: 12.5, Rider: Rider));

    /// <summary>The end of the profile stops a descending lift at the foot.</summary>
    [Fact]
    public void TheEndOfTheProfileStopsTheLiftAtTheFoot() =>
        LiftState
            .Next(new Descending(Start: 12.5, Rider: Rider), new ReachedFoot())
            .Should()
            .Be((LiftState)new AtFoot());

    /// <summary>A second order on the way down changes nothing, and neither does an order at the foot.</summary>
    [Fact]
    public void AnOrderOnTheWayDownOrAtTheFootHasNoRow()
    {
        LiftState.Next(new Descending(Start: 12.5, Rider: Rider), new LiftOrder(Start: 20, Rider: Rider)).Should().BeNull();
        LiftState.Next(new AtFoot(), new LiftOrder(Start: 20, Rider: Rider)).Should().BeNull();
    }

    /// <summary>A lift that does not descend has no foot to reach.</summary>
    [Fact]
    public void ReachingTheFootWithoutADescentHasNoRow()
    {
        LiftState.Next(new AtTop(), new ReachedFoot()).Should().BeNull();
        LiftState.Next(new AtFoot(), new ReachedFoot()).Should().BeNull();
    }

    /// <summary>The default lift waits at the top.</summary>
    [Fact]
    public void TheDefaultLiftWaitsAtTheTop() => default(LiftState).Value.Should().BeOfType<AtTop>();
}

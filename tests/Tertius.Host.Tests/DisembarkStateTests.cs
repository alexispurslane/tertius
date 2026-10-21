using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The table of the exit's flat state machine, <see cref="DisembarkState.Next"/> (docs/plans/blue-danube-demo.md, "The
/// exit", "The exit component"), with no World: each row, and no row for every other pair of a state and an event.
/// </summary>
public sealed class DisembarkStateTests
{
    private static readonly DisembarkState[] States =
        [new Aboard(), new Walking(0), new Walking(3), new Sliding(), new Strolling(0), new Strolling(1), new Arrived(), new Halted()];

    private static readonly DisembarkEvent[] Events = [new LeaveShip(), new Dropped(), new Landed(), new WalkDone(), new StationGone()];

    [Fact]
    public void AnOrderToLeaveTheShipStartsTheWalkAtItsFirstWaypoint() =>
        DisembarkState.Next(new Aboard(), new LeaveShip()).Should().Be((DisembarkState)new Walking(0));

    [Fact]
    public void ADropOverTheChutesTopStartsTheSlide() =>
        DisembarkState.Next(new Walking(3), new Dropped()).Should().Be((DisembarkState)new Sliding());

    [Fact]
    public void ALandingOnTheWheelsFloorStartsTheStrollAtItsFirstWaypoint() =>
        DisembarkState.Next(new Sliding(), new Landed()).Should().Be((DisembarkState)new Strolling(0));

    [Fact]
    public void TheEndOfTheStrollEndsTheExit() =>
        DisembarkState.Next(new Strolling(1), new WalkDone()).Should().Be((DisembarkState)new Arrived());

    [Fact]
    public void AStationThatIsGoneHaltsTheWalkTheSlideAndTheStroll()
    {
        DisembarkState.Next(new Walking(2), new StationGone()).Should().Be((DisembarkState)new Halted());
        DisembarkState.Next(new Sliding(), new StationGone()).Should().Be((DisembarkState)new Halted());
        DisembarkState.Next(new Strolling(0), new StationGone()).Should().Be((DisembarkState)new Halted());
    }

    /// <summary>Every pair that the rows above do not name has no row: <see cref="DisembarkState.Next"/> gives null.</summary>
    [Fact]
    public void EveryOtherPairHasNoRow()
    {
        int rows = 0;
        foreach (DisembarkState state in States)
            foreach (DisembarkEvent change in Events)
            {
                bool row = (state.Value, change.Value) switch
                {
                    (Aboard, LeaveShip) or (Walking, Dropped) or (Sliding, Landed) or (Strolling, WalkDone) => true,
                    (Walking or Sliding or Strolling, StationGone) => true,
                    _ => false,
                };
                DisembarkState? next = DisembarkState.Next(state, change);
                if (row)
                    rows++;
                else
                    next.Should().BeNull($"{state} has no row for {change}");
            }
        rows.Should().Be(11, "the eight states hold eleven pairs with a row");
    }

    /// <summary>
    /// The table matches the cases of the two custom unions without boxing them, so a tick of the exit allocates nothing
    /// for it: a thousand rounds over every pair allocate no byte.
    /// </summary>
    [Fact]
    public void TheTableAllocatesNothing()
    {
        int rows = 0;
        foreach (DisembarkState state in States)
            foreach (DisembarkEvent change in Events)
                rows += DisembarkState.Next(state, change).HasValue ? 1 : 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int round = 0; round < 1000; round++)
            foreach (DisembarkState state in States)
                foreach (DisembarkEvent change in Events)
                    rows += DisembarkState.Next(state, change).HasValue ? 1 : 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0);
        rows.Should().Be(11 * 1001);
    }

    /// <summary>The default state is <see cref="Aboard"/>, and states with the same case and data are equal.</summary>
    [Fact]
    public void TheDefaultStateIsAboardAndEqualStatesHaveTheSameCaseAndData()
    {
        default(DisembarkState).Value.Should().Be(new Aboard());
        ((DisembarkState)new Walking(2)).Should().Be((DisembarkState)new Walking(2));
        ((DisembarkState)new Walking(2)).Should().NotBe((DisembarkState)new Walking(3));
        ((DisembarkState)new Walking(2)).Should().NotBe((DisembarkState)new Strolling(2));
    }
}

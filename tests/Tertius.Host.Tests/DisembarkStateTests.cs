using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The table of the exit's flat state machine, <see cref="DisembarkState.Next"/> (docs/plans/blue-danube-demo.md, "The
/// exit", "The exit component"), with no World: each row, and no row for every other pair of a state and an event.
/// </summary>
public sealed class DisembarkStateTests
{
    private static readonly DisembarkState[] States =
    [
        new Aboard(),
        new Walking(new(0, 0)),
        new Walking(new(3, 0)),
        new Sliding(),
        new Strolling(new(0, 0)),
        new Strolling(new(1, 0)),
        new Arrived(),
        new Halted(),
    ];

    private static readonly DisembarkEvent[] Events =
    [
        new LeaveShip(),
        new Dropped(),
        new Landed(),
        new WalkDone(),
        new StationGone(),
    ];

    [Fact]
    public void AnOrderToLeaveTheShipStartsTheWalkAtItsFirstWaypoint() =>
        DisembarkState.Next(new Aboard(), new LeaveShip()).Should().Be((DisembarkState)new Walking(new(0, 0)));

    [Fact]
    public void ADropOverTheChutesTopStartsTheSlide() =>
        DisembarkState.Next(new Walking(new(3, 0)), new Dropped()).Should().Be((DisembarkState)new Sliding());

    [Fact]
    public void ALandingOnTheWheelsFloorStartsTheStrollAtItsFirstWaypoint() =>
        DisembarkState.Next(new Sliding(), new Landed()).Should().Be((DisembarkState)new Strolling(new(0, 0)));

    [Fact]
    public void TheEndOfTheStrollEndsTheExit() =>
        DisembarkState.Next(new Strolling(new(1, 0)), new WalkDone()).Should().Be((DisembarkState)new Arrived());

    [Fact]
    public void AStationThatIsGoneHaltsTheWalkTheSlideAndTheStroll()
    {
        DisembarkState.Next(new Walking(new(2, 0)), new StationGone()).Should().Be((DisembarkState)new Halted());
        DisembarkState.Next(new Sliding(), new StationGone()).Should().Be((DisembarkState)new Halted());
        DisembarkState.Next(new Strolling(new(0, 0)), new StationGone()).Should().Be((DisembarkState)new Halted());
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
        long allocated = Allocations.Fewest(() =>
        {
            rows = 0;
            for (int round = 0; round < 1000; round++)
                foreach (DisembarkState state in States)
                foreach (DisembarkEvent change in Events)
                    rows += DisembarkState.Next(state, change).HasValue ? 1 : 0;
        });
        allocated.Should().Be(0);
        rows.Should().Be(11 * 1000);
    }

    /// <summary>The default state is <see cref="Aboard"/>, and states with the same case and data are equal.</summary>
    [Fact]
    public void TheDefaultStateIsAboardAndEqualStatesHaveTheSameCaseAndData()
    {
        default(DisembarkState).Value.Should().Be(new Aboard());
        ((DisembarkState)new Walking(new(2, 0))).Should().Be((DisembarkState)new Walking(new(2, 0)));
        ((DisembarkState)new Walking(new(2, 0))).Should().NotBe((DisembarkState)new Walking(new(3, 0)));
        ((DisembarkState)new Walking(new(2, 0)))
            .Should()
            .NotBe((DisembarkState)new Walking(new(2, 0.5)), "the pace is data of the case too");
        ((DisembarkState)new Strolling(new(1, 0.5))).Value.Should().Be(new Strolling(new(1, 0.5)));
        ((DisembarkState)new Walking(new(2, 0))).Should().NotBe((DisembarkState)new Strolling(new(2, 0)));
    }
}

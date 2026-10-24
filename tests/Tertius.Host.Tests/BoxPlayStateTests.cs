using Tertius.Demo;

namespace Tertius.Host.Tests;

/// <summary>
/// The table of the coda's flat state machine, <see cref="BoxPlayState.Next"/> (docs/plans/blue-danube-demo.md, "The
/// coda", "The box play component"), with no World: each row, and no row for every other pair of a state and an event.
/// </summary>
public sealed class BoxPlayStateTests
{
    private static readonly Entity Copy = new(7, 2);

    private static readonly BoxPlayState[] States =
    [
        new Idle(), new Shoving(WalkProgress.Start), new Shoving(new(1, 3)), new ToStand(WalkProgress.Start),
        new ToStand(new(0, 2.5)), new Taking(0), new Taking(1),
        new Aiming(0, Copy, 0), new Aiming(0, Copy, 12), new Aiming(1, Copy, 3), new Watching(0, 0),
        new Watching(0, 40), new Watching(1, 7),
        new Played(), new Halted()
    ];

    private static readonly BoxPlayEvent[] Events =
    [
        new PlayWithBoxes(), new WalkDone(), new Took(Copy), new Thrown(), new Watched(), new StationGone(),
        new BoxGone()
    ];

    [Fact]
    public void AnOrderToPlayStartsTheWalkIntoTheStackAtItsFirstWaypoint() =>
        BoxPlayState.Next(new Idle(), new PlayWithBoxes()).Should().Be((BoxPlayState)new Shoving(WalkProgress.Start));

    [Fact]
    public void TheEndOfTheWalkThroughTheStackStartsTheWalkToTheStand() =>
        BoxPlayState.Next(new Shoving(new(2, 1)), new WalkDone()).Should().Be(
            (BoxPlayState)new ToStand(WalkProgress.Start),
            "the walk to the stand starts from rest at its first waypoint");

    [Fact]
    public void AtTheStandThePersonTakesTheFirstBox() =>
        BoxPlayState.Next(new ToStand(new(1, 0)), new WalkDone()).Should().Be((BoxPlayState)new Taking(0));

    [Fact]
    public void ATakenBoxIsAimedWithItsCopyInHandFromTheFirstTick()
    {
        BoxPlayState.Next(new Taking(0), new Took(Copy)).Should().Be((BoxPlayState)new Aiming(0, Copy, 0));
        BoxPlayState.Next(new Taking(1), new Took(Copy)).Should().Be((BoxPlayState)new Aiming(1, Copy, 0));
    }

    [Fact]
    public void AfterEachThrowThePersonWatchesTheBoxFromTheFirstTick()
    {
        BoxPlayState.Next(new Aiming(0, Copy, 12), new Thrown()).Should().Be((BoxPlayState)new Watching(0, 0));
        BoxPlayState.Next(new Aiming(1, Copy, 3), new Thrown()).Should().Be((BoxPlayState)new Watching(1, 0));
    }

    [Fact]
    public void AfterWatchingTheFirstThrowThePersonTakesTheSecondBox() =>
        BoxPlayState.Next(new Watching(0, 40), new Watched()).Should().Be((BoxPlayState)new Taking(1));

    [Fact]
    public void AfterWatchingTheSecondThrowThePlayIsOver() =>
        BoxPlayState.Next(new Watching(1, 7), new Watched()).Should().Be((BoxPlayState)new Played());

    [Fact]
    public void AStationThatIsGoneHaltsEveryCaseOfThePlay()
    {
        foreach (BoxPlayState state in new BoxPlayState[]
                 {
                     new Shoving(WalkProgress.Start), new ToStand(new(1, 0)), new Taking(0), new Aiming(1, Copy, 3),
                     new Watching(0, 2)
                 })
            BoxPlayState.Next(state, new StationGone()).Should().Be((BoxPlayState)new Halted(), $"{state} halts");
    }

    [Fact]
    public void ABoxThatIsGoneBeforeItIsTakenHaltsThePlay() =>
        BoxPlayState.Next(new Taking(1), new BoxGone()).Should().Be((BoxPlayState)new Halted());

    /// <summary>Every pair that the rows above do not name has no row: <see cref="BoxPlayState.Next"/> gives null.</summary>
    [Fact]
    public void EveryOtherPairHasNoRow()
    {
        int rows = 0;
        foreach (BoxPlayState state in States)
        foreach (BoxPlayEvent change in Events)
        {
            bool row = (state.Value, change.Value) switch
            {
                (Idle, PlayWithBoxes) or (Shoving, WalkDone) or (ToStand, WalkDone) or (Taking, Took)
                    or (Aiming, Thrown) or (Watching, Watched) => true,
                (Shoving or ToStand or Taking or Aiming or Watching, StationGone) or (Taking, BoxGone) => true,
                _ => false
            };
            BoxPlayState? next = BoxPlayState.Next(state, change);
            if (row)
                rows++;
            else
                next.Should().BeNull($"{state} has no row for {change}");
        }

        rows.Should().Be(27, "the fifteen states hold twenty-seven pairs with a row");
    }

    /// <summary>A thousand rounds of the table over every pair allocate no byte: it matches the unions' cases without boxing them.</summary>
    [Fact]
    public void TheTableAllocatesNothing()
    {
        int rows = 0;
        foreach (BoxPlayState state in States)
        foreach (BoxPlayEvent change in Events)
            rows += BoxPlayState.Next(state, change).HasValue ? 1 : 0;
        long allocated = Allocations.Fewest(() =>
        {
            rows = 0;
            for (int round = 0; round < 1000; round++)
                foreach (BoxPlayState state in States)
                foreach (BoxPlayEvent change in Events)
                    rows += BoxPlayState.Next(state, change).HasValue ? 1 : 0;
        });
        allocated.Should().Be(0);
        rows.Should().Be(27 * 1000);
    }

    /// <summary>The default state is <see cref="Idle"/>, and states with the same case and data are equal.</summary>
    [Fact]
    public void TheDefaultStateIsIdleAndEqualStatesHaveTheSameCaseAndData()
    {
        default(BoxPlayState).Value.Should().Be(new Idle());
        ((BoxPlayState)new Aiming(1, Copy, 3)).Should().Be((BoxPlayState)new Aiming(1, Copy, 3));
        ((BoxPlayState)new Aiming(1, Copy, 3)).Should().NotBe((BoxPlayState)new Aiming(1, Copy, 4));
        ((BoxPlayState)new Aiming(1, Copy, 3)).Should().NotBe((BoxPlayState)new Aiming(0, Copy, 3));
        ((BoxPlayState)new Aiming(1, Copy, 3)).Should().NotBe((BoxPlayState)new Aiming(1, new(8, 2), 3));
        ((BoxPlayState)new Shoving(new(1, 3))).Should().NotBe((BoxPlayState)new ToStand(new(1, 3)));
        ((BoxPlayState)new Shoving(new(1, 3))).Should().NotBe((BoxPlayState)new Shoving(new(1, 2.5)));
        ((BoxPlayState)new ToStand(new(0, 2))).Should().NotBe((BoxPlayState)new ToStand(new(1, 2)));
        ((BoxPlayState)new Taking(1)).Should().NotBe((BoxPlayState)new Taking(0));
        ((BoxPlayState)new Watching(1, 3)).Should().NotBe((BoxPlayState)new Watching(1, 4));
        ((BoxPlayState)new Watching(1, 3)).Should().NotBe((BoxPlayState)new Watching(0, 3));
        default(BoxPlayEvent).Value.Should().Be(new PlayWithBoxes());
        ((BoxPlayEvent)new Took(Copy)).Should().NotBe((BoxPlayEvent)new Took(new(8, 2)));
    }
}

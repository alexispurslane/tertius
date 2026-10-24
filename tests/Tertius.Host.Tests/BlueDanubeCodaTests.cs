using Tertius.Demo;
using SpinStation = Tertius.Demo.DemoScenes.SpinStation;
using WheelCoda = Tertius.Demo.DemoScenes.WheelCoda;

namespace Tertius.Host.Tests;

/// <summary>
/// The coda in the checked-in long script, scripts/blue-danube.json (docs/plans/blue-danube-demo.md, "The coda"): the run
/// of <see cref="BlueDanubeRendezvousRun"/> goes on after the exit's film <c>floor</c> through the coda's films to the end
/// of the script. So one run goes from the terminal on the ground to the two throws in the wheel. The coda's own run of
/// the scene <c>demo-exit</c>, in <see cref="WheelCodaTests"/>, checks the coda in detail. These tests check that the long
/// run reaches the same end. Every place is in the station's axes.
/// </summary>
[Collection(BlueDanubeRendezvousRun.Collection)]
public sealed class BlueDanubeCodaTests(BlueDanubeRendezvousRun run)
{
    /// <summary>The ticks after the film <c>floor</c>, from the order <see cref="PlayWithBoxes"/> to the end of the run.</summary>
    private IReadOnlyList<RendezvousTick> Coda { get; } = [.. run.AfterFloor];

    /// <summary>
    /// The loose boxes spawn with the long scene, at its first tick, and rest through the whole run: at the end of the
    /// film <c>floor</c>, after about 5,090 s, each box of the stack lies within 1 cm of its spawn place.
    /// </summary>
    [Fact]
    public void TheStackRestsWhereItSpawnedUntilTheCoda()
    {
        (string Id, DPose Pose)[] stack = [.. WheelCoda.Stack()];
        run.StackAtFloor.Should().HaveCount(stack.Length);
        for (int i = 0; i < stack.Length; i++)
            Double3
                .Distance(run.StackAtFloor[i], stack[i].Pose.Position)
                .Should()
                .BeLessThan(0.01, $"{stack[i].Id} rests where it spawned");
    }

    /// <summary>
    /// The coda plays to its end in the long run: the person's play is idle until the order, goes through both throws and
    /// ends played. Each thrown box lands within 1 mm of where the straight line of its release says, and the world records
    /// no error and logs no line of the play.
    /// </summary>
    [Fact]
    public void TheLongRunPlaysTheCodaToItsEnd()
    {
        run.Ticks.Where(tick => tick.Tick <= run.FloorTick).Should().OnlyContain(tick => tick.Play.Value is Idle);
        Coda.Select(tick => tick.Play.Value)
            .OfType<Aiming>()
            .Select(aiming => aiming.Throw)
            .Distinct()
            .Should()
            .Equal(0, 1);
        Coda[^1].Play.Value.Should().Be(new Played());

        BoxPlay play = run.World.GetComponent<BoxPlay>(run.Scene["person"])!;
        play.Thrown.Should().HaveCount(BoxPlayState.Throws);
        foreach (Entity box in play.Thrown)
        {
            FlightRecorder flight = run.World.GetComponent<FlightRecorder>(box)!;
            ThrowLanding line = flight.Release!.Value.Landing(
                SpinStation.Rate,
                DemoScenes.DemoStation.WheelFloor,
                WheelCoda.BoxHalf
            );
            Double3.Distance(flight.Landing!.Value.Place, line.Place).Should().BeLessThan(1e-3);
        }

        run.World.Errors.Should().BeEmpty();
        run.Logged.Should().NotContain(line => line.Contains(nameof(BoxPlay)));
    }

    /// <summary>
    /// The person walks into the stack, as in the coda's own run: at the end of the run, the middle box of the floor's row
    /// and the three boxes above it lie more than 0.3 m from their spawn places.
    /// </summary>
    [Fact]
    public void ThePersonKnocksTheStackOver()
    {
        (string Id, DPose Pose)[] stack = [.. WheelCoda.Stack()];
        foreach (int moved in new[] { 1, 3, 4, 5 })
            Double3
                .Distance(
                    run.World.PoseIn(run.Scene[stack[moved].Id], run.StationFrame).Position,
                    stack[moved].Pose.Position
                )
                .Should()
                .BeGreaterThan(0.3, $"{stack[moved].Id} is knocked away");
    }
}

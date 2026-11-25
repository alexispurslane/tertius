using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using StationExit = Tertius.Demo.DemoScenes.StationExit;

namespace Tertius.Host.Tests;

/// <summary>
/// The exit in the checked-in long script, scripts/blue-danube.json (docs/plans/blue-danube-demo.md, "The exit", "Sections
/// of the long script"): the run of <see cref="BlueDanubeRendezvousRun"/> goes on after the docking's film <c>docked</c>
/// through the exit's films to <c>floor</c>, and the coda follows. So one run goes from the terminal on the ground through
/// the launch, the rendezvous and the docking to the person on the wheel's floor. The exit's own run of the scene
/// <c>demo-exit</c>, in
/// <see cref="DisembarkTests"/>, checks the exit in detail. These tests check that the long run reaches the same end.
/// Every place is in the station's axes.
/// </summary>
[Collection(BlueDanubeRendezvousRun.Collection)]
public sealed class BlueDanubeExitTests(BlueDanubeRendezvousRun run)
{
    /// <summary>The ticks after the film <c>docked</c>, from the order <see cref="LeaveShip"/> to the end of the film
    /// <c>floor</c>.</summary>
    private IReadOnlyList<RendezvousTick> Exit { get; } =
    [.. run.AfterDocked.TakeWhile(tick => tick.Tick <= run.FloorTick)];

    /// <summary>
    /// The long script films the exit's sections right after the docking's <c>docked</c>, in their order, and ends with
    /// the coda's sections, in their order.
    /// </summary>
    [Fact]
    public void TheLongScriptFilmsTheExitsSectionsAfterTheDockingAndTheCodasAfterThem()
    {
        string[] films =
        [
            .. Script
                .Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json"))
                .Steps.Select(step => step.Value)
                .OfType<FilmStep>()
                .Select(film => film.Name),
        ];
        string[] exit = [.. BlueDanube.ExitSections.Select(section => section.Name)];
        string[] coda = [.. BlueDanube.CodaSections.Select(section => section.Name)];

        films.Should().EndWith([BlueDanubeRendezvousRun.DockedFilm, .. exit, .. coda]);
        exit[^1].Should().Be(BlueDanubeRendezvousRun.FloorFilm);
        coda[^1].Should().Be(BlueDanubeRendezvousRun.LastFilm);
    }

    /// <summary>
    /// One run of the long script takes the person from its seat in the docked shuttle to the wheel's floor. The run
    /// reaches the film <c>docked</c> and goes on past it. At the end of the film <c>floor</c>, the person's exit component is in
    /// <see cref="Arrived"/>, and the person stands in the station's frame on the wheel's floor and feels a full gravity
    /// there: ω² × 1,999.1 m = 9.802 m/s², within 1 percent. It stops within 0.1 m of the stroll's end, still for the last
    /// half second of the film <c>floor</c>. The world records no error.
    /// </summary>
    [Fact]
    public void TheLongRunTakesThePersonFromTheDockedSeatToTheWheelsFloor()
    {
        run.DockedTick.Should().BePositive("the run films the docking's end");
        Exit.Should().NotBeEmpty("the run goes on past the docking");
        run.DockedEnd.Exit.Value.Should()
            .BeOfType<Aboard>("the docking ends with the person's exit component waiting for the order");
        Exit.Should().Contain(tick => tick.Exit.Value is Walking, "the order starts the walk");

        RendezvousTick end = Exit[^1];
        end.Exit.Value.Should().Be(new Arrived());
        end.PersonStanding.Should().BeTrue();
        end.PersonFrame.Should().Be(run.StationFrame);
        end.PersonFeltGravity.Should().BeApproximately(9.802, 0.098);
        Double3 strollEnd = StationExit.Stroll(run.StationFrame)[0].Position;
        (end.PersonInStation - strollEnd).Length.Should().BeLessThan(0.1, "the person stops at the stroll's end");
        Exit.TakeLast(30)
            .Should()
            .OnlyContain(tick => tick.PersonVelocityInStation.Length < 0.05, "the person stands still at the end");
        run.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The exit takes as long in the long run as in the scene <c>demo-exit</c>, which starts at the docking's end state:
    /// the person arrives within 2 s of its time there, and the lift tops out at its cruise speed of 20 m/s.
    /// </summary>
    [Fact]
    public void TheExitInTheLongRunTakesAsLongAsInItsOwnScene()
    {
        // The script posts the order between the docking's last tick and the exit's first.
        RendezvousTick order = run.DockedEnd;
        RendezvousTick arrived = Exit.First(tick => tick.Exit.Value is Arrived);

        (arrived.Time - order.Time).Should().BeApproximately(DisembarkTests.ArrivalAfterOrder, 2);
        Exit.Where(tick => tick.Exit.Value is Riding)
            .Max(tick => tick.PersonVelocityInStation.Length)
            .Should()
            .BeApproximately(StationExit.CruiseSpeed, 0.1);
    }

    /// <summary>
    /// The lift waits at the top of its spoke, in its place in the station's axes, through the docking and the walk to it:
    /// in every tick from the parking orbit until the person rides, its floor's middle lies within 1 mm of
    /// <see cref="StationExit.LiftTop"/>. The docking ship's bubble takes the lift in as the hull moves out to the deck,
    /// and a lift that held still in that frame rode 9 m off its top with the hull.
    /// </summary>
    [Fact]
    public void TheLiftWaitsAtItsTopUntilThePersonRidesIt()
    {
        RendezvousTick[] waiting = [.. run.Ticks.TakeWhile(tick => tick.Exit.Value is not Riding)];
        waiting.Should().NotBeEmpty();
        foreach (RendezvousTick tick in waiting)
            (tick.LiftInStation - StationExit.LiftTop.Position)
                .Length.Should()
                .BeLessThan(1e-3, $"the lift waits at its top at tick {tick.Tick}");
    }

    /// <summary>
    /// The person rides the lift down standing and steps off onto the wheel's floor: the stroll starts in the tick after
    /// a step in which the person stood on the lift's floor 0.3 m above the wheel's floor, in the lift's middle across
    /// the axis, and at rest in the station's axes.
    /// </summary>
    [Fact]
    public void ThePersonStepsOffTheLiftOntoTheWheelsFloor()
    {
        int start = Exit.ToList().FindIndex(tick => tick.Exit.Value is Strolling);
        start.Should().BePositive();
        RendezvousTick landed = Exit[start - 1];
        landed.Exit.Value.Should().BeOfType<Riding>();
        landed.PersonStanding.Should().BeTrue();
        Double3 at = landed.PersonInStation;
        Math.Sqrt(at.X * at.X + at.Z * at.Z)
            .Should()
            .BeInRange(
                DemoStation.Layout.FloorRadius - 2,
                DemoStation.Layout.FloorRadius,
                "the person stands at the foot"
            );
        Math.Abs(at.Y).Should().BeLessThan(DemoStation.Layout.LiftHalf, "the person stands on the lift");
        landed.PersonVelocityInStation.Length.Should().BeLessThan(0.05, "the lift has stopped");
    }

    /// <summary>
    /// The person enters the station's frame only once, in the spoke, and no numeric frame takes it: in every tick of the
    /// exit it is in the cabin's frame or the station's.
    /// </summary>
    [Fact]
    public void ThePersonGoesFromTheCabinsFrameToTheStationsInTheSpoke()
    {
        FrameHandle cabin = run.World.GetOwnedFrame(run.Scene["shuttle"]);
        Exit.Should()
            .OnlyContain(
                tick => tick.PersonFrame == cabin || tick.PersonFrame == run.StationFrame,
                "no numeric frame takes the person"
            );
        int entry = Exit.ToList().FindIndex(tick => tick.PersonFrame == run.StationFrame);
        entry.Should().BePositive();
        Exit[entry].Exit.Value.Should().BeOfType<Riding>();
        Exit[entry]
            .PersonInStation.X.Should()
            .BeInRange(
                -DemoStation.Layout.CeilingRadius,
                DemoStation.Layout.Deck - 0.5,
                "the person enters the station's frame in the spoke"
            );
        Exit.Skip(entry)
            .Should()
            .OnlyContain(tick => tick.PersonFrame == run.StationFrame, "the person stays in the station's frame");
    }

    /// <summary>
    /// The docked shuttle stays parked through the exit: its hull is static, and its pose in the station's frame does not
    /// change from the docking's end.
    /// </summary>
    [Fact]
    public void TheShuttleStaysParkedThroughTheExit()
    {
        DPose docked = run.DockedEnd.InStation;
        Exit.Should().OnlyContain(tick => tick.HullMotion == BodyMotion.Static && tick.Docking.Value is Docked);
        Exit.Max(tick => (tick.InStation.Position - docked.Position).Length).Should().BeLessThan(1e-6);
    }

    /// <summary>The person keeps the drawn copy of the pen through the exit: the copy lives in every tick.</summary>
    [Fact]
    public void ThePenStaysWithThePersonThroughTheExit() =>
        Exit.Should().OnlyContain(tick => tick.PenFromHand != null, "the person never lets go of the pen");
}

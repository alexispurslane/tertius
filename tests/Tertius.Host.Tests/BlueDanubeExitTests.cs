using Tertius.Demo;
using Tertius.Host.Scripting;
using Tertius.Simulation;
using static Tertius.Host.Tests.HostTesting;
using DemoStation = Tertius.Demo.DemoScenes.DemoStation;
using StationExit = Tertius.Demo.DemoScenes.StationExit;

namespace Tertius.Host.Tests;

/// <summary>
/// The exit in the checked-in long script, scripts/blue-danube.json (docs/plans/blue-danube-demo.md, "The exit", "Sections
/// of the long script"): the run of <see cref="BlueDanubeRendezvousRun"/> goes on after the docking's film <c>docked</c>
/// through the exit's films to <c>floor</c>, and the coda follows. So one run goes from the terminal on the ground through
/// the launch, the rendezvous and the docking to the person on the wheel's floor. The exit's own run of the scene <c>demo-exit</c>, in
/// <see cref="DisembarkTests"/>, checks the exit in detail. These tests check that the long run reaches the same end.
/// Every place is in the station's axes.
/// </summary>
[Collection(BlueDanubeRendezvousRun.Collection)]
public sealed class BlueDanubeExitTests(BlueDanubeRendezvousRun run)
{
    /// <summary>The ticks after the film <c>docked</c>, from the order <see cref="LeaveShip"/> to the end of the film <c>floor</c>.</summary>
    private IReadOnlyList<RendezvousTick> Exit { get; } = [.. run.AfterDocked.TakeWhile(tick => tick.Tick <= run.FloorTick)];

    /// <summary>
    /// The long script films the exit's sections right after the docking's <c>docked</c>, in their order, and ends with
    /// the coda's sections, in their order.
    /// </summary>
    [Fact]
    public void TheLongScriptFilmsTheExitsSectionsAfterTheDockingAndTheCodasAfterThem()
    {
        string[] films = [.. Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json")).Steps
            .Select(step => step.Value).OfType<FilmStep>().Select(film => film.Name)];
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
    /// there: ω² × 99.1 m = 9.72 m/s², within 1 percent. It stops within 0.1 m of the stroll's end, still for the last half
    /// second of the film <c>floor</c>. The world records no error. Measured on 2026-10-21, with the coda: the film floor
    /// ends at tick 305,459, and the run, with the coda, is 306,517 ticks. The script posts the order after tick 302,165, at
    /// 5,036.08 s, and the person arrives at tick 305,339, at 5,088.98 s, 52.90 s after the order. It stands 99.10 m from
    /// the axis and feels 9.721 m/s².
    /// </summary>
    [Fact]
    public void TheLongRunTakesThePersonFromTheDockedSeatToTheWheelsFloor()
    {
        run.DockedTick.Should().BePositive("the run films the docking's end");
        Exit.Should().NotBeEmpty("the run goes on past the docking");
        run.DockedEnd.Exit.Value.Should().BeOfType<Aboard>("the docking ends with the person's exit component waiting for the order");
        Exit.Should().Contain(tick => tick.Exit.Value is Walking, "the order starts the walk");

        RendezvousTick end = Exit[^1];
        end.Exit.Value.Should().Be(new Arrived());
        end.PersonStanding.Should().BeTrue();
        end.PersonFrame.Should().Be(run.StationFrame);
        end.PersonFeltGravity.Should().BeApproximately(9.72, 0.0972);
        Double3 strollEnd = StationExit.Stroll(run.StationFrame)[0].Position;
        (end.PersonInStation - strollEnd).Length.Should().BeLessThan(0.1, "the person stops at the stroll's end");
        Exit.TakeLast(30).Should().OnlyContain(tick => tick.PersonVelocityInStation.Length < 0.05, "the person stands still at the end");
        run.World.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// The exit takes as long in the long run as in the scene <c>demo-exit</c>, which starts at the docking's end state:
    /// the person arrives between 50 and 56 s after the order, and the slide tops out between 8 and 10 m/s. Measured on
    /// 2026-10-21: the person arrives 52.90 s after the order, against 53.0 s in its own scene. The slide starts 17.3 s
    /// after the order, takes 30.1 s and tops out at 8.87 m/s, as in its own scene.
    /// </summary>
    [Fact]
    public void TheExitInTheLongRunTakesAsLongAsInItsOwnScene()
    {
        // The script posts the order between the docking's last tick and the exit's first.
        RendezvousTick order = run.DockedEnd;
        RendezvousTick arrived = Exit.First(tick => tick.Exit.Value is Arrived);

        (arrived.Time - order.Time).Should().BeInRange(50, 56);
        Exit.Where(tick => tick.Exit.Value is Sliding).Max(tick => tick.PersonVelocityInStation.Length).Should().BeInRange(8, 10);
    }

    /// <summary>
    /// The person lands on its feet on the wheel's floor: the stroll starts in the tick after a step in which the person
    /// stood on the floor, between the rails, after the brake run slowed it below 3.5 m/s.
    /// </summary>
    [Fact]
    public void ThePersonLandsOnItsFeetOnTheWheelsFloor()
    {
        int start = Exit.ToList().FindIndex(tick => tick.Exit.Value is Strolling);
        start.Should().BePositive();
        RendezvousTick landed = Exit[start - 1];
        landed.Exit.Value.Should().BeOfType<Sliding>();
        landed.PersonStanding.Should().BeTrue();
        Double3 at = landed.PersonInStation;
        Math.Sqrt(at.X * at.X + at.Z * at.Z).Should().BeInRange(DemoStation.FloorRadius - 1, DemoStation.FloorRadius, "the person stands on the floor");
        Math.Abs(at.Y).Should().BeLessThan(StationExit.Chute.HalfWidth, "the person lands between the rails");
        Exit.Where(tick => tick.Exit.Value is Sliding && tick.PersonInStation.X < -(DemoStation.FloorRadius - 1.5))
            .Max(tick => tick.PersonVelocityInStation.Length).Should().BeLessThan(3.5);
    }

    /// <summary>
    /// The person enters the station's frame only once, in the spoke, and no numeric frame takes it: in every tick of the
    /// exit it is in the cabin's frame or the station's.
    /// </summary>
    [Fact]
    public void ThePersonGoesFromTheCabinsFrameToTheStationsInTheSpoke()
    {
        FrameHandle cabin = run.World.GetOwnedFrame(run.Scene["shuttle"]);
        Exit.Should().OnlyContain(tick => tick.PersonFrame == cabin || tick.PersonFrame == run.StationFrame, "no numeric frame takes the person");
        int entry = Exit.ToList().FindIndex(tick => tick.PersonFrame == run.StationFrame);
        entry.Should().BePositive();
        Exit[entry].Exit.Value.Should().BeOfType<Sliding>();
        Exit[entry].PersonInStation.X.Should().BeInRange(-DemoStation.CeilingRadius, DemoStation.Deck - 0.5, "the person enters the station's frame in the spoke");
        Exit.Skip(entry).Should().OnlyContain(tick => tick.PersonFrame == run.StationFrame, "the person stays in the station's frame");
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

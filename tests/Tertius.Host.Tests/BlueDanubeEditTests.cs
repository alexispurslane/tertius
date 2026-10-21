using Tertius.Demo;
using Tertius.Host.Scripting;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>
/// The cut list of the Blue Danube demo against the checked-in long script, scripts/blue-danube.json: it names only films
/// of the script, and the edit can cut the script with it.
/// </summary>
public sealed class BlueDanubeEditTests
{
    private static IReadOnlyList<ScriptFilm> Films { get; } = ScriptFilm.Of(Script.Load(Path.Combine(RepositoryRoot(), "scripts", "blue-danube.json")));

    private static int IndexOf(string film) => Films.ToList().FindIndex(f => f.Name == film);

    [Fact]
    public void TheCutListNamesOnlyFilmsOfTheCheckedInScript()
    {
        CutList cuts = BlueDanubeEdit.Cuts;
        IEnumerable<string> named = [.. cuts.Parts.Select(p => p.FirstFilm), .. cuts.Trims.Select(t => t.Film), .. cuts.Panels.SelectMany(p => (string[])[p.FirstFilm, p.LastFilm])];

        named.Should().OnlyContain(film => IndexOf(film) >= 0);
    }

    [Fact]
    public void TheEditCanCutTheCheckedInScriptWithTheCutList()
    {
        List<ReadoutRow> rows = [.. Films.SelectMany(film => Enumerable.Range(0, film.Frames).Select(k => new ReadoutRow($"{film.Name}-{k:000000}", film.FirstTick, new Double3(DemoScenes.EarthRadius, 0, 0), 0, 0, 0, 0)))];

        Action cut = () => EditSubtitles.Film(Stretch.Of(Films, BlueDanubeEdit.Cuts.Trims), rows, BlueDanubeEdit.Cuts);

        cut.Should().NotThrow();
    }

    [Fact]
    public void TheEditKeepsEveryFrameOfTheCheckedInScript()
    {
        // The user's decision of 2026-10-20: keep every frame where any part of the shuttle or the station shows, however
        // dark. A measure of the round-2 master found one of them in every frame (docs/plans/blue-danube-demo.md, "The
        // dark frames").
        IReadOnlyList<Stretch> stretches = Stretch.Of(Films, BlueDanubeEdit.Cuts.Trims);

        stretches.Sum(stretch => stretch.Count).Should().Be(Films.Sum(film => film.Frames));
    }

    [Fact]
    public void ThePartsStartWithTheFirstFilmAndFollowTheOrderOfTheScript()
    {
        int[] starts = [.. BlueDanubeEdit.Cuts.Parts.Select(part => IndexOf(part.FirstFilm))];

        starts[0].Should().Be(0);
        starts.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheWeightlessPartStartsWithTheFirstFilmInOrbit()
    {
        // The climb ends when the ascent autopilot is in orbit.
        EditPart weightless = BlueDanubeEdit.Cuts.Parts.Single(part => part.Title == "Weightless");

        IndexOf(weightless.FirstFilm).Should().Be(IndexOf("climb-high") + 1);
    }

    [Fact]
    public void TheDockingPartStartsWithTheFirstFilmAfterTheHold()
    {
        // The hold ends with the film hold-chase, and the next section orders the docking.
        EditPart docking = BlueDanubeEdit.Cuts.Parts.Single(part => part.Title == "The docking");

        IndexOf(docking.FirstFilm).Should().Be(IndexOf("hold-chase") + 1);
    }

    [Fact]
    public void TheWheelPartStartsWithTheFirstFilmOfTheExit()
    {
        EditPart wheel = BlueDanubeEdit.Cuts.Parts.Single(part => part.Title == "The wheel");

        wheel.FirstFilm.Should().Be(BlueDanube.ExitSections[0].Name);
    }

    [Fact]
    public void TheSpinGravityPanelSpansTheBrakeRunAndTheFloorToTheEndOfTheRun()
    {
        PanelSpan panel = BlueDanubeEdit.Cuts.Panels.Single(span => span.Panel == (CheckPanel)CheckPanels.SpinGravity);

        // The slide ends with the film chute, 10 m above the wheel's ceiling, and the brake run and the landing follow.
        IndexOf(panel.FirstFilm).Should().Be(IndexOf("chute") + 1);
        panel.LastFilm.Should().Be(Films[^1].Name);
    }

    [Fact]
    public void TheOutlinesShowWhereTheFramesMatter()
    {
        // The walk into the parked shuttle's bubble, the falling frame that forms round the shuttle early in the climb, the
        // hand-off out of the turning ground later in the climb, the shuttle's own frame in orbit, the station's bubble at
        // the rim, and the entry into the station's turning bubble (docs/plans/blue-danube-demo.md, "The frames made visible").
        Films.Where(film => film.Outlines).Select(film => film.Name).Should().Equal("boarding", "climb-low", "climb-high", "orbit", "glide-rim", "entry");
    }

    [Fact]
    public void TheParkingOrbitPanelSpansTheCoastFromTheInsertionToTheTransferBurn()
    {
        PanelSpan panel = BlueDanubeEdit.Cuts.Panels.Single(span => span.Panel == (CheckPanel)CheckPanels.ParkingOrbit);

        // The climb ends when the ascent autopilot is in orbit, and the film burn holds the transfer burn.
        IndexOf(panel.FirstFilm).Should().Be(IndexOf("climb-high") + 1);
        IndexOf(panel.LastFilm).Should().Be(IndexOf("burn") - 1);
    }
}
